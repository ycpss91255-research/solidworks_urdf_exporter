#!/usr/bin/env python3
"""Regression entry point for the OBJ + MTL export (and the committed example packages).

Two modes, one result format (a PASS/FAIL line per check, a summary table, an optional JSON file, exit code 1
when any check failed):

  --offline   Needs only Python 3 (standard library). Validates the COMMITTED example packages against each
              other: OBJ parses and every face points at an existing vertex, every usemtl has a material in
              the MTL, URDF mesh paths exist, bounding boxes are finite and plausible, URDF mass equals CSV,
              colours in the CSV equal the MTL, OBJ and STL packages of the same model agree. No SolidWorks,
              no build. Takes about a second. This is the part a hosted CI runner can run.

  --live      Drives YOUR OWN SolidWorks through the real exporter code (a private SW2URDF.dll) on scratch
              copies of the repository examples and a few small models it builds, then checks the written
              packages numerically against SolidWorks' own values. Needs Windows, SolidWorks, pywin32, the
              .NET Framework 4 csc.exe and a built SW2URDF.dll (scripts\\test_build.ps1). It starts a
              SolidWorks instance of its own with a guarded launcher (or uses one you own with --pid), keeps it
              hidden, never shows a window that needs a click, stops after --timeout seconds and closes only
              the processes it started. See TESTING.md ("OBJ + MTL export regression").

Examples:
  python scripts/regress_obj_export.py --offline
  python scripts/regress_obj_export.py --live --dll <folder with SW2URDF.dll> --launcher <launch_solidworks.py> \\
         --json result.json --markdown result.md
"""
import argparse
import csv
import datetime
import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import threading
import time
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
EXAMPLES = os.path.join(REPO, "examples")


# --------------------------------------------------------------------------------------------- results

class Results:
    """PASS / FAIL / INFO / SKIP per check. Only FAIL makes the run fail."""

    def __init__(self):
        self.rows = []

    def add(self, case, check, status, detail=""):
        # no local paths in results that may be pasted into documents
        detail = re.sub(r"[A-Za-z]:[\\/][^ |)\]]*", "<path>", str(detail))
        self.rows.append({"case": case, "check": check, "status": status, "detail": detail})
        text = "%-4s  %s | %s" % (status, case, check)
        if detail:
            text += "  (%s)" % detail
        print(text, flush=True)

    def check(self, case, name, ok, detail=""):
        # The detail is printed for failures and kept for passes when it carries a measured value
        self.add(case, name, "PASS" if ok else "FAIL", detail)
        return ok

    def guard(self, case, name, function):
        """Run a check that may raise: an exception is a FAIL, never a crash."""
        try:
            return function()
        except Exception as error:  # noqa: BLE001
            self.add(case, name, "FAIL", "%s: %s" % (type(error).__name__, error))
            return None

    @property
    def failed(self):
        return [r for r in self.rows if r["status"] == "FAIL"]

    def table(self):
        lines = ["", "%-34s %-62s %s" % ("Case", "Check", "Result"), "-" * 110]
        for r in self.rows:
            lines.append("%-34s %-62s %s" % (r["case"][:34], r["check"][:62], r["status"]))
        counts = {s: sum(1 for r in self.rows if r["status"] == s) for s in ("PASS", "FAIL", "INFO", "SKIP")}
        lines.append("-" * 110)
        lines.append("PASS %(PASS)d   FAIL %(FAIL)d   INFO %(INFO)d   SKIP %(SKIP)d" % counts)
        return "\n".join(lines)

    def markdown(self, build, title):
        lines = ["%s (build %s)" % (title, build), "", "| Case | Check | Result |", "|------|-------|--------|"]
        for r in self.rows:
            detail = (" - " + r["detail"]) if r["detail"] and r["status"] != "PASS" else ""
            lines.append("| %s | %s | %s%s |" % (r["case"], r["check"], r["status"], detail.replace("|", "/")))
        return "\n".join(lines) + "\n"


# ------------------------------------------------------------------------------------------ file readers

def read_obj(path):
    """Returns dict(vertices=[(x,y,z)], faces=[(i,j,k)] zero based, materials=[usemtl], mtllib=name|None, objects=int)."""
    vertices, faces, materials, objects = [], [], [], 0
    mtllib = None
    with open(path, encoding="utf-8") as stream:
        for number, line in enumerate(stream, 1):
            fields = line.split()
            if not fields or fields[0].startswith("#"):
                continue
            key = fields[0]
            if key == "v":
                vertices.append(tuple(float(x) for x in fields[1:4]))
            elif key == "f":
                if len(fields) < 4:
                    raise ValueError("line %d: face with fewer than 3 vertices" % number)
                index = []
                for part in fields[1:]:
                    i = int(part.split("/")[0])
                    index.append(i - 1 if i > 0 else len(vertices) + i)
                for k in range(1, len(index) - 1):  # triangulate fans (the exporter writes triangles only)
                    faces.append((index[0], index[k], index[k + 1]))
            elif key == "usemtl":
                materials.append(fields[1] if len(fields) > 1 else "")
            elif key == "mtllib":
                mtllib = line.strip()[7:].strip()
            elif key == "o":
                objects += 1
            elif key in ("vn", "vt", "g", "s"):
                pass
            else:
                raise ValueError("line %d: unexpected statement %r" % (number, key))
    return {"vertices": vertices, "faces": faces, "materials": materials, "mtllib": mtllib, "objects": objects}


def read_mtl(path):
    """Returns {material name: {field: [floats]}}."""
    materials, current = {}, None
    with open(path, encoding="utf-8") as stream:
        for line in stream:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            key, _, value = line.partition(" ")
            if key == "newmtl":
                current = materials.setdefault(value.strip(), {})
            elif current is not None:
                try:
                    current[key] = [float(x) for x in value.split()]
                except ValueError:
                    current[key] = value.split()
    return materials


def read_stl(path):
    """Vertices of a binary or ASCII STL as a flat list of (x, y, z)."""
    data = open(path, "rb").read()
    if len(data) >= 84:
        count = struct.unpack("<I", data[80:84])[0]
        if 84 + 50 * count == len(data):
            points = []
            for n in range(count):
                values = struct.unpack("<12f", data[84 + 50 * n:84 + 50 * n + 48])
                points.extend([values[3:6], values[6:9], values[9:12]])
            return points
    points = []
    for line in data.decode("utf-8", "replace").splitlines():
        fields = line.split()
        if fields[:1] == ["vertex"]:
            points.append(tuple(float(x) for x in fields[1:4]))
    return points


def bbox(points):
    xs, ys, zs = zip(*points)
    return [min(xs), min(ys), min(zs), max(xs), max(ys), max(zs)]


def extent(box):
    return max(box[3] - box[0], box[4] - box[1], box[5] - box[2])


def centre(box):
    return [(box[i] + box[i + 3]) / 2 for i in range(3)]


def mesh_centroid(obj, link):
    """Volume centroid of the OBJ in the link frame (uniform density: the centre of mass)."""
    meshes = [m for m in link["meshes"] if m[0] == "visual"]
    if not meshes:
        return None
    vertices = place(obj["vertices"], meshes[0][2], meshes[0][3])
    total, centre_sum = 0.0, [0.0, 0.0, 0.0]
    for a, b, c in obj["faces"]:
        (x1, y1, z1), (x2, y2, z2), (x3, y3, z3) = vertices[a], vertices[b], vertices[c]
        volume = (x1 * (y2 * z3 - y3 * z2) - y1 * (x2 * z3 - x3 * z2) + z1 * (x2 * y3 - x3 * y2)) / 6.0
        total += volume
        for i, values in enumerate(((x1, x2, x3), (y1, y2, y3), (z1, z2, z3))):
            centre_sum[i] += volume * sum(values) / 4.0
    return [v / total for v in centre_sum] if total else None


def mesh_volume(vertices, faces):
    total = 0.0
    for a, b, c in faces:
        (x1, y1, z1), (x2, y2, z2), (x3, y3, z3) = vertices[a], vertices[b], vertices[c]
        total += (x1 * (y2 * z3 - y3 * z2) - y1 * (x2 * z3 - x3 * z2) + z1 * (x2 * y3 - x3 * y2)) / 6.0
    return abs(total)


def rotation(rpy):
    r, p, y = rpy
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return [[cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr],
            [sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr],
            [-sp, cp * sr, cp * cr]]


def place(points, xyz, rpy):
    """URDF origin applied to mesh points: p' = R p + xyz."""
    m = rotation(rpy)
    return [tuple(m[i][0] * x + m[i][1] * y + m[i][2] * z + xyz[i] for i in range(3)) for x, y, z in points]


def floats(text, default="0 0 0"):
    return [float(x) for x in (text or default).split()]


def read_urdf(path):
    """Returns dict(name, links={name: dict(mass, com, meshes=[(kind, file, xyz, rpy)])}, joints=[(name, parent, child)])."""
    root = ET.parse(path).getroot()
    if root.tag != "robot":
        raise ValueError("root element is %r, not robot" % root.tag)
    links, joints = {}, []
    for link in root.findall("link"):
        entry = {"mass": None, "com": None, "meshes": []}
        mass = link.find("inertial/mass")
        if mass is not None:
            entry["mass"] = float(mass.get("value"))
            origin = link.find("inertial/origin")
            entry["com"] = floats(origin.get("xyz") if origin is not None else None)
        for kind in ("visual", "collision"):
            for element in link.findall(kind):
                mesh = element.find("geometry/mesh")
                if mesh is None:
                    continue
                origin = element.find("origin")
                entry["meshes"].append((kind, mesh.get("filename"),
                                        floats(origin.get("xyz") if origin is not None else None),
                                        floats(origin.get("rpy") if origin is not None else None)))
        links[link.get("name")] = entry
    for joint in root.findall("joint"):
        joints.append((joint.get("name"), joint.find("parent").get("link"), joint.find("child").get("link")))
    return {"name": root.get("name"), "links": links, "joints": joints}


def read_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


NUMBER = re.compile(r"(-?\d+\.?\d*(?:[Ee][-+]?\d+)?)")


def numerically_equal(a_path, b_path, relative=1e-9, absolute=1e-15):
    """Same text and numbers equal within tolerance (the URDF and CSV carry 1e-19 level noise from SolidWorks)."""
    a = NUMBER.split(open(a_path, encoding="utf-8").read())
    b = NUMBER.split(open(b_path, encoding="utf-8").read())
    if len(a) != len(b):
        return False
    for index, (x, y) in enumerate(zip(a, b)):
        if index % 2 == 0:
            if x != y:
                return False
        else:
            u, v = float(x), float(y)
            if abs(u - v) > absolute + relative * max(abs(u), abs(v)):
                return False
    return True


def compare_packages(first, second):
    """Files that differ between two package folders: bytes for everything except URDF/CSV, numbers for those."""
    differing = []
    names = set()
    for folder in (first, second):
        for base, _dirs, files in os.walk(folder):
            for name in files:
                relative = os.path.relpath(os.path.join(base, name), folder)
                if not relative.lower().endswith((".log", ".3dxml")):
                    names.add(relative)
    for relative in sorted(names):
        a, b = os.path.join(first, relative), os.path.join(second, relative)
        if not (os.path.isfile(a) and os.path.isfile(b)):
            differing.append(relative + " (missing)")
        elif relative.lower().endswith((".urdf", ".csv")):
            if not numerically_equal(a, b):
                differing.append(relative)
        elif sha256(a) != sha256(b):
            differing.append(relative)
    return differing


def long_path(path):
    """Expand 8.3 short names (a user name longer than 8 characters makes TEMP a short path on Windows);
    SolidWorks refuses to add components given by a short path."""
    if sys.platform != "win32":
        return path
    import ctypes
    buffer = ctypes.create_unicode_buffer(32768)
    length = ctypes.windll.kernel32.GetLongPathNameW(path, buffer, len(buffer))
    return buffer.value if 0 < length < len(buffer) else path


def sha256(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest() if os.path.isfile(path) else "missing"


# --------------------------------------------------------------------------------- package level checks

def package_of(urdf_path):
    return os.path.dirname(os.path.dirname(urdf_path))


def resolve_mesh(package_dir, uri):
    """package://<name>/<relative> -> file path inside the package (None when the uri is of another form)."""
    prefix = "package://" + os.path.basename(package_dir) + "/"
    if not uri or not uri.startswith(prefix):
        return None
    return os.path.join(package_dir, *uri[len(prefix):].split("/"))


def find_packages():
    """Every committed package: a folder with urdf/*.urdf."""
    found = []
    for folder, _dirs, files in os.walk(EXAMPLES):
        if os.path.basename(folder) == "urdf":
            for name in sorted(files):
                if name.lower().endswith(".urdf"):
                    found.append(os.path.join(folder, name))
    return sorted(found)


def check_obj_file(results, case, path, label):
    """Parse, counts, face indices, bounding box. Returns the parsed OBJ or None."""
    obj = results.guard(case, label + ": OBJ parses", lambda: read_obj(path))
    if obj is None:
        return None
    ok = results.check(case, label + ": vertex and face count > 0", bool(obj["vertices"] and obj["faces"]),
                       "%d vertices, %d faces" % (len(obj["vertices"]), len(obj["faces"])))
    bad = [f for f in obj["faces"] if min(f) < 0 or max(f) >= len(obj["vertices"])]
    ok &= results.check(case, label + ": faces reference existing vertices", not bad, "%d bad faces" % len(bad))
    if not obj["vertices"]:
        return None
    finite = all(math.isfinite(c) for v in obj["vertices"] for c in v)
    box = bbox(obj["vertices"])
    size = extent(box) if finite else float("nan")
    # Plausible for a robot link in metres: between 0.1 mm and 100 m (catches millimetre / unit mistakes)
    ok &= results.check(case, label + ": bounding box finite and plausible",
                        finite and 1e-4 <= size <= 100.0, "largest extent %.6g m" % size)
    return obj if ok else obj


def check_mtl_for(results, case, obj_path, obj, label):
    """mtllib exists, every usemtl has a material, Kd in range. Returns the materials."""
    if not obj["materials"]:
        results.check(case, label + ": OBJ uses at least one material", False)
        return {}
    if not obj["mtllib"]:
        results.check(case, label + ": OBJ names an MTL file", False)
        return {}
    mtl_path = os.path.join(os.path.dirname(obj_path), obj["mtllib"])
    if not results.check(case, label + ": MTL file exists", os.path.isfile(mtl_path), obj["mtllib"]):
        return {}
    materials = results.guard(case, label + ": MTL parses", lambda: read_mtl(mtl_path)) or {}
    missing = sorted(set(obj["materials"]) - set(materials))
    results.check(case, label + ": every usemtl has a material in the MTL", not missing,
                  "missing: " + ", ".join(missing) if missing else "%d materials" % len(set(obj["materials"])))
    bad = [n for n, m in materials.items()
           if "Kd" not in m or len(m["Kd"]) != 3 or not all(0.0 <= x <= 1.0 for x in m["Kd"])]
    results.check(case, label + ": MTL Kd present and within 0..1", not bad, ", ".join(bad))
    return materials


def kd_set(materials):
    return sorted(tuple(round(x, 6) for x in m["Kd"]) for m in materials.values() if "Kd" in m)


def link_frame_box(package_dir, entry, kind="visual"):
    """Bounding box of a link's mesh in the link frame (visual origin applied), or None."""
    for k, uri, xyz, rpy in entry["meshes"]:
        if k != kind:
            continue
        path = resolve_mesh(package_dir, uri)
        if path is None or not os.path.isfile(path):
            return None
        if path.lower().endswith(".obj"):
            points = read_obj(path)["vertices"]
        elif path.lower().endswith(".stl"):
            points = read_stl(path)
        else:
            return None
        return bbox(place(points, xyz, rpy))
    return None


def check_package(results, urdf_path):
    package = package_of(urdf_path)
    case = os.path.relpath(package, EXAMPLES).replace("\\", "/")
    data = results.guard(case, "URDF is well formed", lambda: read_urdf(urdf_path))
    if data is None:
        return None
    results.check(case, "URDF is well formed", True, "%d links, %d joints" % (len(data["links"]), len(data["joints"])))
    names = set(data["links"])
    bad_joints = [j for j in data["joints"] if j[1] not in names or j[2] not in names]
    results.check(case, "joint parents and children are links", not bad_joints, str(bad_joints))
    if data["links"]:
        children = {j[2] for j in data["joints"]}
        results.check(case, "exactly one root link", len(names - children) == 1, ", ".join(sorted(names - children)))

    # mesh paths
    missing, objs, stls = [], {}, {}
    for name, link in data["links"].items():
        for kind, uri, _xyz, _rpy in link["meshes"]:
            path = resolve_mesh(package, uri)
            if path is None or not os.path.isfile(path) or os.path.getsize(path) == 0:
                missing.append("%s/%s: %s" % (name, kind, uri))
            elif path.lower().endswith(".obj"):
                objs[name] = path
            elif path.lower().endswith(".stl"):
                stls[name] = path
    results.check(case, "URDF package:// mesh paths exist and are not empty", not missing, "; ".join(missing))

    # OBJ + MTL
    all_materials = {}
    if objs:
        meshes_dir = os.path.join(package, "meshes")
        written = {f for f in os.listdir(meshes_dir) if f.lower().endswith(".obj")}
        referenced = {os.path.basename(p) for p in objs.values()}
        results.check(case, "OBJ files written equal OBJ files referenced by the URDF", written == referenced,
                      "unreferenced: %s" % sorted(written - referenced) if written != referenced else "")
        for name, path in sorted(objs.items()):
            obj = check_obj_file(results, case, path, name)
            if obj and obj["vertices"]:
                all_materials[name] = check_mtl_for(results, case, path, obj, name)
    # mass: URDF positive and equal to the CSV
    csv_files = [f for f in os.listdir(os.path.dirname(urdf_path)) if f.lower().endswith(".csv")]
    if csv_files:
        rows = results.guard(case, "CSV parses", lambda: read_csv(os.path.join(os.path.dirname(urdf_path), csv_files[0])))
        if rows is not None:
            by_name = {r["Link Name"]: r for r in rows}
            results.check(case, "CSV link names equal URDF link names", set(by_name) == names,
                          "only in CSV: %s; only in URDF: %s" % (sorted(set(by_name) - names), sorted(names - set(by_name))))
            worst = 0.0
            for name, link in data["links"].items():
                if name in by_name and link["mass"] is not None:
                    reference = float(by_name[name]["Mass"])
                    worst = max(worst, abs(link["mass"] - reference) / max(abs(reference), 1e-12))
            results.check(case, "URDF mass equals CSV mass", worst <= 1e-9, "largest relative difference %.2g" % worst)
            # colour in CSV equals MTL Kd (links with one material)
            for name, materials in all_materials.items():
                row = by_name.get(name)
                if row and len(materials) == 1 and row.get("Color Red") not in (None, ""):
                    csv_kd = tuple(round(float(row[c]), 6) for c in ("Color Red", "Color Green", "Color Blue"))
                    kd = tuple(round(x, 6) for x in next(iter(materials.values()))["Kd"])
                    results.check(case, "%s: MTL Kd equals CSV colour" % name,
                                  all(abs(a - b) < 1e-5 for a, b in zip(csv_kd, kd)), "%s vs %s" % (csv_kd, kd))
    for name, link in data["links"].items():
        if link["mass"] is not None:
            results.check(case, "%s: mass in URDF is positive" % name, link["mass"] > 0, "%g kg" % link["mass"])
    # centre of mass lies inside the mesh box (link frame), OBJ packages only
    for name, path in sorted(objs.items()):
        link = data["links"][name]
        if link["com"] is None:
            continue
        box = link_frame_box(package, link)
        if box:
            slack = 1e-3 + 0.01 * extent(box)
            inside = all(box[i] - slack <= link["com"][i] <= box[i + 3] + slack for i in range(3))
            results.check(case, "%s: centre of mass lies inside the mesh box (link frame)" % name, inside,
                          "com %s box %s" % (["%.4g" % c for c in link["com"]], ["%.4g" % b for b in box]))
    return {"case": case, "urdf": urdf_path, "package": package, "data": data, "objs": objs, "stls": stls}


def check_obj_vs_stl(results, packages):
    """Same model exported as STL and as OBJ: meshes in the link frame agree (the committed pairs)."""
    by_key = {}
    for p in packages:
        key = re.sub(r"_description(_obj)?$", "", os.path.basename(p["package"]))
        by_key.setdefault(key, []).append(p)
    for key, group in sorted(by_key.items()):
        stl = next((p for p in group if p["stls"] and not p["objs"]), None)
        obj = next((p for p in group if p["objs"]), None)
        if not stl or not obj:
            continue
        case = "%s: OBJ vs STL package" % key
        names = sorted(set(stl["stls"]) & set(obj["objs"]))
        results.check(case, "same link names in both packages", set(stl["data"]["links"]) == set(obj["data"]["links"]))
        for name in names:
            a = link_frame_box(stl["package"], stl["data"]["links"][name])
            b = link_frame_box(obj["package"], obj["data"]["links"][name])
            tolerance = 2e-4 + 0.005 * extent(a)
            worst = max(abs(x - y) for x, y in zip(a + centre(a), b + centre(b)))
            results.check(case, "%s: box and centre in link frame equal (STL vs OBJ)" % name, worst <= tolerance,
                          "largest difference %.3g m, limit %.3g m" % (worst, tolerance))


def run_offline():
    results = Results()
    urdfs = find_packages()
    if not urdfs:
        results.check("examples", "committed example packages found", False, EXAMPLES)
        return results
    packages = [p for p in (check_package(results, u) for u in urdfs) if p]
    check_obj_vs_stl(results, packages)
    obj_packages = [p for p in packages if p["objs"]]
    results.check("examples", "at least one committed OBJ package is covered", bool(obj_packages),
                  "%d of %d packages contain OBJ meshes" % (len(obj_packages), len(packages)))
    return results


# ---------------------------------------------------------------------------------------------- live mode

def tsv(path):
    """probe.tsv -> dict (a repeated key keeps the last value)."""
    values = {}
    if os.path.isfile(path):
        for line in open(path, encoding="utf-8"):
            key, _, value = line.rstrip("\n").partition("\t")
            values[key] = value
    return values


class Live:
    def __init__(self, args, results):
        self.args = args
        self.results = results
        self.work = None
        self.pid = None
        self.started = None
        self.exe = None
        self.children = []
        self.language = "?"

    # -- process control -------------------------------------------------------------------------
    def kill_own(self):
        for child in list(self.children):
            try:
                child.kill()
            except OSError:
                pass
        if self.started:
            subprocess.run(["taskkill", "/PID", str(self.started), "/T", "/F"], capture_output=True, check=False)

    def launch(self):
        manifest = os.path.join(self.work, "launch.json")
        session = os.path.join(self.work, "session.json")
        python = self.args.launcher_python or sys.executable
        subprocess.run([python, self.args.launcher, "--launch", "--session-file", session, "--output", manifest,
                        "--journal-warning-action", "dismiss"], check=True, timeout=300)
        with open(manifest, encoding="utf-8") as stream:
            return int(json.load(stream)["pid"])

    def exit_instance(self):
        if not self.started:
            return
        try:
            import make_test_fixtures as fixtures
            app = fixtures.bind(self.started)
            app.CloseAllDocuments(True)
            app.ExitApp()
        except Exception as error:  # noqa: BLE001
            print("ExitApp failed (%s); terminating process %d" % (error, self.started))
        deadline = time.time() + 60
        while time.time() < deadline:
            listing = subprocess.run(["tasklist", "/FI", "PID eq %d" % self.started, "/NH"], capture_output=True, text=True).stdout
            if str(self.started) not in listing:
                return
            time.sleep(1)
        subprocess.run(["taskkill", "/PID", str(self.started), "/F"], check=False, capture_output=True)

    # -- build the host --------------------------------------------------------------------------
    def compile_probe(self):
        dll_dir = os.path.abspath(self.args.dll)
        windir = os.environ.get("WINDIR", r"C:\Windows")
        framework = os.path.join(windir, "Microsoft.NET", "Framework64", "v4.0.30319")
        csc = os.path.join(framework, "csc.exe")
        sw_dir = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "SOLIDWORKS Corp", "SOLIDWORKS")
        interop = []
        for name in ("SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll"):
            path = os.path.join(dll_dir, name)
            if not os.path.isfile(path):
                shutil.copy(os.path.join(sw_dir, name), path)
            interop.append(path)
        exe = os.path.join(self.work, "bin", "ObjRegressProbe.exe")
        os.makedirs(os.path.dirname(exe), exist_ok=True)
        # The host sits next to the DLL it tests: private copy of the build folder (the satellite
        # assemblies in the language folders carry the translated texts)
        shutil.copytree(dll_dir, os.path.dirname(exe), dirs_exist_ok=True)
        references = ["System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll", "System.Runtime.Serialization.dll",
                      os.path.join(framework, "WPF", "WindowsBase.dll"), os.path.join(framework, "WPF", "PresentationFramework.dll"),
                      os.path.join(os.path.dirname(exe), "SW2URDF.dll")]
        command = [csc, "/nologo", "/target:winexe", "/platform:x64", "/out:" + exe]
        command += ["/r:" + r for r in references] + ["/link:" + i for i in interop]
        command.append(os.path.join(HERE, "ObjRegressProbe.cs"))
        subprocess.run(command, check=True)
        if self.args.exe_hook:
            # Optional command run on every binary this script compiles before it starts it
            for binary in (exe,):
                subprocess.run(self.args.exe_hook.replace("{exe}", binary), shell=True, check=True)
        return exe

    def probe(self, mode, *arguments, timeout=None):
        """Runs the host for one scenario; returns (exit code, dict of probe.tsv)."""
        out = arguments[-2] if mode != "dialog" else arguments[-1]
        os.makedirs(out, exist_ok=True)
        command = [self.exe, mode, str(self.pid)] + list(arguments)
        process = subprocess.Popen(command, cwd=os.path.dirname(self.exe), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        self.children.append(process)
        try:
            output, _ = process.communicate(timeout=timeout or self.args.case_timeout)
        except subprocess.TimeoutExpired:
            process.kill()
            output, _ = process.communicate()
            return -1, {"status": "timeout", "error": "no result within %d s" % (timeout or self.args.case_timeout)}
        finally:
            self.children.remove(process)
        values = tsv(os.path.join(out, "probe.tsv"))
        if process.returncode != 0 and "error" not in values:
            values["error"] = output[-500:]
        return process.returncode, values

    # -- fixtures --------------------------------------------------------------------------------
    def copy_example(self, name, files):
        folder = os.path.join(self.work, "models", name)
        os.makedirs(folder, exist_ok=True)
        source = os.path.join(EXAMPLES, name)
        for entry in files:
            shutil.copy(os.path.join(source, entry), folder)
        return folder

    def build_fixtures(self):
        """Models that are not in the repository: a textured box, a tiny box and a two-level assembly."""
        import pythoncom
        import make_test_fixtures as f
        from win32com.client import VARIANT
        app = f.bind(self.pid)
        folder = os.path.join(self.work, "models", "built")
        os.makedirs(folder, exist_ok=True)
        titles = []
        try:
            # textured box: an appearance with an image texture
            tex = f.make_box_part(app, 0.04, 0.04, f.RED, None)
            tex_path = os.path.join(folder, "BOX_TEX.SLDPRT")
            carpet = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "SOLIDWORKS Corp", "SOLIDWORKS",
                                  "data", "graphics", "Materials", "fabric", "carpet", "carpet color1 2d.p2m")
            if not os.path.isfile(carpet):
                raise RuntimeError("texture appearance not found in this SolidWorks installation: " + carpet)
            material = tex.Extension.CreateRenderMaterial(carpet)
            if not material.AddEntity(tex) or not tex.Extension.AddRenderMaterial(material, VARIANT(pythoncom.VT_BYREF | pythoncom.VT_I4, 0)):
                raise RuntimeError("could not apply the texture appearance")
            f.save(tex, tex_path)
            titles.append(tex.GetTitle)
            # very small model: 3 mm cube (SolidWorks refuses to extrude a smaller sketch with snapping on)
            tiny = make_small_box(app, f, 0.003)
            f.save(tiny, os.path.join(folder, "BOX_TINY.SLDPRT"))
            titles.append(tiny.GetTitle)
            # two-level assembly from the TWO_BOXES parts: SUB = both boxes, TOP = SUB placed and turned 90 degrees
            red = os.path.join(folder, "BOX_RED.SLDPRT")
            blue = os.path.join(folder, "BOX_BLUE.SLDPRT")
            for name in ("BOX_RED.SLDPRT", "BOX_BLUE.SLDPRT"):
                shutil.copy(os.path.join(EXAMPLES, "TWO_BOXES", name), folder)
            for path in (red, blue):   # a component can only be added from an open document
                if app.OpenDoc(path, 1) is None:
                    raise RuntimeError("could not open " + path)
            sub = f.new_document(app, f.SW_DEFAULT_TEMPLATE_ASSEMBLY)
            titles.append(sub.GetTitle)
            c1 = sub.AddComponent5(red, 0, "", False, "", 0.0, 0.0, 0.0)
            c2 = sub.AddComponent5(blue, 0, "", False, "", 0.1, 0.0, 0.0)
            if c1 is None or c2 is None:
                raise RuntimeError("could not add components to the sub-assembly")
            c2.ReferencedConfiguration = f.BLUE_TALL
            sub.ForceRebuild3(False)
            if not sub.Extension.SelectByID2("Point1@Origin", "EXTSKETCHPOINT", 0, 0, 0, False, 1, f.null_dispatch(), 0):
                raise RuntimeError("could not select the sub-assembly origin")
            frame = sub.FeatureManager.InsertCoordinateSystem(False, False, False)
            if frame is None:
                raise RuntimeError("could not create the coordinate system")
            frame.Name = "Origin_sub"
            sub.ClearSelection2(True)
            sub_path = os.path.join(folder, "SUB.SLDASM")
            f.save(sub, sub_path)
            top = f.new_document(app, f.SW_DEFAULT_TEMPLATE_ASSEMBLY)
            titles.append(top.GetTitle)
            # placed away from the origin; the host program turns it by 90 degrees (in memory only) before the export
            component = top.AddComponent5(sub_path, 0, "", False, "", 0.2, 0.05, 0.0)
            if component is None:
                raise RuntimeError("could not add the sub-assembly")
            top.ForceRebuild3(False)
            f.save(top, os.path.join(folder, "TOP.SLDASM"))
        finally:
            for title in reversed(titles):
                app.CloseDoc(title)
            app.CloseAllDocuments(True)   # also whatever a failed step left open
        return folder


def make_small_box(app, f, side):
    """Like make_test_fixtures.make_box_part for millimetre-sized boxes: sketch snapping is switched off."""
    model = f.new_document(app, f.SW_DEFAULT_TEMPLATE_PART)
    plane = f.first_reference_plane(model)
    model.ClearSelection2(True)
    plane.Select2(False, 0)
    sketch_manager = model.SketchManager
    sketch_manager.InsertSketch(True)
    sketch_manager.AddToDB = True
    sketch_manager.CreateCenterRectangle(0, 0, 0, side / 2, side / 2, 0)
    sketch_manager.AddToDB = False
    sketch_manager.InsertSketch(True)
    feature = model.FeatureManager.FeatureExtrusion3(
        True, False, False, 0, 0, side, 0.0, False, False, False, False, 0.0, 0.0,
        False, False, False, False, True, True, True, 0, 0.0, False)
    if feature is None:
        raise RuntimeError("Extrusion of the small box failed")
    from win32com.client import VARIANT
    import pythoncom
    model.MaterialPropertyValues = VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, (1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 0.0, 0.0))
    f.rename_active_configuration(model, "Default")
    model.ForceRebuild3(False)
    return model


# ------------------------------------------------------------------------------------------ live checks

def sw_link(values, index):
    prefix = "link%d." % index
    if prefix + "sw.mass" not in values:
        return None
    return {"name": values.get(prefix + "name"), "mass": float(values[prefix + "sw.mass"]),
            "volume": float(values[prefix + "sw.volume"]),
            "com": [float(x) for x in values[prefix + "sw.com_link"].split()],
            "box": [float(x) for x in values[prefix + "sw.box_global"].split()]}


def package_files(folder):
    return sorted(os.path.relpath(os.path.join(d, f), folder).replace("\\", "/") for d, _s, fs in os.walk(folder) for f in fs)


def check_live_package(results, case, package_dir, values, expected_kd=None, require_sw=True, link_names=None):
    """The numeric checks of one exported OBJ package against SolidWorks' own values."""
    urdfs = [os.path.join(package_dir, "urdf", f) for f in os.listdir(os.path.join(package_dir, "urdf"))
             if f.lower().endswith(".urdf")] if os.path.isdir(os.path.join(package_dir, "urdf")) else []
    if not results.check(case, "package has a URDF", len(urdfs) == 1, ", ".join(os.listdir(package_dir)) if os.path.isdir(package_dir) else "no package"):
        return None
    data = results.guard(case, "URDF is well formed", lambda: read_urdf(urdfs[0]))
    if data is None:
        return None
    meshed = {n: l for n, l in data["links"].items() if l["meshes"]}
    results.check(case, "URDF has links with meshes", bool(meshed), "%d links with meshes" % len(meshed))
    missing, objs = [], {}
    for name, link in meshed.items():
        for kind, uri, _x, _r in link["meshes"]:
            path = resolve_mesh(package_dir, uri)
            if path is None or not os.path.isfile(path):
                missing.append("%s/%s" % (name, uri))
            elif path.lower().endswith(".obj"):
                objs[name] = path
    results.check(case, "URDF package:// mesh paths exist and match the written files", not missing, "; ".join(missing))
    written = sorted(f for f in os.listdir(os.path.join(package_dir, "meshes")) if f.lower().endswith((".obj", ".mtl")))
    expected = sorted({os.path.basename(p) for p in objs.values()} | {os.path.splitext(os.path.basename(p))[0] + ".mtl" for p in objs.values()})
    results.check(case, "meshes folder holds exactly the referenced OBJ and MTL files", written == expected, "%s vs %s" % (written, expected))
    sources = [os.path.splitext(p)[0] + ".3dxml" for p in objs.values()]
    results.check(case, "intermediate .3dxml is left beside the OBJ (documented, issue 130)", all(os.path.isfile(s) for s in sources))
    references = {}
    for index in range(int(values.get("links", "0"))):
        ref = sw_link(values, index)
        if ref:
            references[ref["name"]] = ref
    summaries = {}
    for name, path in sorted(objs.items()):
        obj = check_obj_file(results, case, path, name)
        if not obj or not obj["vertices"]:
            continue
        materials = check_mtl_for(results, case, path, obj, name)
        summaries[name] = (obj, materials)
        ref = references.get(name) or (next(iter(references.values())) if len(references) == 1 and require_sw else None)
        if ref and require_sw:
            volume = mesh_volume(obj["vertices"], obj["faces"])
            results.check(case, "%s: OBJ volume equals SolidWorks (0.5 %%)" % name, abs(volume / ref["volume"] - 1) <= 0.005,
                          "OBJ %.6g m3, SolidWorks %.6g m3" % (volume, ref["volume"]))
            box = bbox(obj["vertices"])
            tolerance = 0.005 * extent(ref["box"]) + 1e-6
            worst = max(abs(a - b) for a, b in zip(box, ref["box"]))
            results.check(case, "%s: OBJ bounding box equals SolidWorks (0.5 %% of size)" % name, worst <= tolerance,
                          "largest difference %.3g m, limit %.3g m" % (worst, tolerance))
            link = data["links"][name]
            centroid = mesh_centroid(obj, link)
            if centroid:
                limit = 1e-4 + 0.01 * extent(box)
                worst = max(abs(a - b) for a, b in zip(link["com"], centroid))
                results.check(case, "%s: URDF centre of mass equals the centroid of the OBJ in the link frame" % name, worst <= limit,
                              "URDF %s, mesh %s" % (["%.5g" % c for c in link["com"]], ["%.5g" % c for c in centroid]))
            results.check(case, "%s: URDF mass equals SolidWorks mass" % name,
                          link["mass"] is not None and abs(link["mass"] / ref["mass"] - 1) <= 1e-4,
                          "URDF %s kg, SolidWorks %.6g kg" % (link["mass"], ref["mass"]))
            limit = 1e-6 + 1e-3 * extent(ref["box"])
            worst = max(abs(a - b) for a, b in zip(link["com"], ref["com"]))
            results.check(case, "%s: URDF centre of mass equals SolidWorks (link frame)" % name, worst <= limit,
                          "URDF %s, SolidWorks %s" % (["%.5g" % c for c in link["com"]], ["%.5g" % c for c in ref["com"]]))
    if expected_kd is not None:
        for name, (_obj, materials) in summaries.items():
            got = kd_set(materials)
            results.check(case, "%s: MTL Kd equals the SolidWorks colours" % name,
                          len(got) == len(expected_kd) and all(all(abs(a - b) < 1e-4 for a, b in zip(x, y)) for x, y in zip(got, expected_kd)),
                          "MTL %s, expected %s" % (got, expected_kd))
    return {"data": data, "objs": objs, "summaries": summaries}


def compare_with_committed(results, case, package_dir, committed_dir):
    """MTL colours and mesh counts/box against the committed example package of the same model."""
    new = {os.path.basename(f): f for f in (os.path.join(package_dir, "meshes", n) for n in os.listdir(os.path.join(package_dir, "meshes")))
           if f.endswith(".mtl")}
    old_dir = os.path.join(committed_dir, "meshes")
    for name in sorted(os.listdir(old_dir)):
        if not name.endswith(".mtl"):
            continue
        if name not in new:
            results.check(case, "%s: written like the committed example" % name, False, "not written")
            continue
        a, b = kd_set(read_mtl(new[name])), kd_set(read_mtl(os.path.join(old_dir, name)))
        results.check(case, "%s: MTL colours equal the committed example MTL" % name,
                      len(a) == len(b) and all(all(abs(x - y) < 1e-6 for x, y in zip(p, q)) for p, q in zip(a, b)), "%s vs %s" % (a, b))
    for name in sorted(os.listdir(old_dir)):
        if not name.endswith(".obj"):
            continue
        path = os.path.join(package_dir, "meshes", name)
        if not os.path.isfile(path):
            continue
        a, b = read_obj(path), read_obj(os.path.join(old_dir, name))
        box_a, box_b = bbox(a["vertices"]), bbox(b["vertices"])
        worst = max(abs(x - y) for x, y in zip(box_a, box_b))
        results.check(case, "%s: bounding box equals the committed example OBJ" % name, worst <= 0.005 * extent(box_b) + 1e-6,
                      "largest difference %.3g m" % worst)
        volume_a, volume_b = mesh_volume(a["vertices"], a["faces"]), mesh_volume(b["vertices"], b["faces"])
        results.check(case, "%s: volume equals the committed example OBJ (0.5 %%)" % name, abs(volume_a / volume_b - 1) <= 0.005,
                      "%.6g vs %.6g m3" % (volume_a, volume_b))


def check_frame_placement(results, case, obj_info, stl_dir, expected_centre=None):
    """Mesh centre relative to the link origin: OBJ equals the STL export of the same link (and an expected value if given)."""
    stl_urdfs = [os.path.join(stl_dir, "urdf", f) for f in os.listdir(os.path.join(stl_dir, "urdf")) if f.lower().endswith(".urdf")]
    if not stl_urdfs:
        results.check(case, "STL export of the same links exists", False, "no STL package")
        return
    stl = read_urdf(stl_urdfs[0])
    obj_package = package_of(os.path.join(os.path.dirname(os.path.dirname(next(iter(obj_info["objs"].values())))), "urdf", "x.urdf"))
    for name in sorted(obj_info["objs"]):
        a = link_frame_box(obj_package, obj_info["data"]["links"][name])
        b = link_frame_box(stl_dir, stl["links"].get(name, {"meshes": []}))
        if not a or not b:
            results.check(case, "%s: mesh centre in link frame, OBJ vs STL" % name, False, "mesh missing")
            continue
        limit = 2e-4 + 0.005 * extent(b)
        worst = max(abs(x - y) for x, y in zip(centre(a) + a, centre(b) + b))
        results.check(case, "%s: mesh centre/box in link frame equals the STL export" % name, worst <= limit,
                      "OBJ centre %s, STL centre %s" % (["%.5f" % c for c in centre(a)], ["%.5f" % c for c in centre(b)]))
        if expected_centre:
            miss = max(abs(x - y) for x, y in zip(centre(a), expected_centre))
            results.check(case, "%s: mesh centre in link frame equals the expected %s" % (name, expected_centre), miss <= 2e-4,
                          "OBJ centre %s" % (["%.5f" % c for c in centre(a)],))


def run_live(args, results):
    sys.path.insert(0, HERE)
    live = Live(args, results)
    live.work = long_path(tempfile.mkdtemp(prefix="sw2urdf_obj_regress_"))
    deadline = threading.Timer(args.timeout, lambda: (print("FAIL  overall timeout of %d s reached; stopping" % args.timeout, flush=True),
                                                      live.kill_own(), os._exit(3)))
    deadline.daemon = True
    deadline.start()
    try:
        build_live(live, args, results)
    finally:
        deadline.cancel()
        if args.artifacts and os.path.isdir(os.path.join(live.work, "run")):
            shutil.copytree(os.path.join(live.work, "run"), os.path.abspath(args.artifacts), dirs_exist_ok=True)
        try:
            live.kill_own() if not live.started else live.exit_instance()
        finally:
            if args.keep:
                print("Scratch folder kept: " + live.work)
            else:
                shutil.rmtree(live.work, ignore_errors=True)


def build_live(live, args, results):
    import pythoncom
    pythoncom.CoInitialize()
    import make_test_fixtures as f
    try:
        live.exe = live.compile_probe()
    except Exception as error:  # noqa: BLE001
        results.add("setup", "host program builds against the private SW2URDF.dll", "FAIL", str(error))
        return
    results.add("setup", "host program builds against the private SW2URDF.dll", "PASS")
    if args.pid:
        live.pid = args.pid
    else:
        try:
            live.pid = live.started = live.launch()
        except Exception as error:  # noqa: BLE001
            results.add("setup", "own SolidWorks instance started", "FAIL", str(error))
            return
    try:
        app = f.bind(live.pid)
        app.Visible = False   # never in the foreground
        results.add("setup", "SolidWorks %s, process %d, hidden" % (f.prop(app, "RevisionNumber"), live.pid), "PASS")
    except Exception as error:  # noqa: BLE001
        results.add("setup", "bind to the SolidWorks process", "FAIL", str(error))
        return
    docs = app.GetDocuments
    if docs:
        results.add("setup", "instance holds no documents", "FAIL", "close them or use another instance")
        return

    def case_dir(name):
        return os.path.join(live.work, "run", name)

    # ---- models
    three_dof = live.copy_example("3_DOF_ARM", ["3_DOF_ARM.SLDASM", "3_DOF_ARM_BASE.SLDPRT", "3_DOF_ARM_END_EFFECTOR.SLDPRT", "3_DOF_ARM_SEGMENT.SLDPRT"])
    two_boxes = live.copy_example("TWO_BOXES", ["TWO_BOXES_PER_CONFIG.SLDASM", "BOX_RED.SLDPRT", "BOX_BLUE.SLDPRT"])
    try:
        built = live.build_fixtures()
        results.add("setup", "extra models built (textured box, tiny box, two-level assembly)", "PASS")
    except Exception as error:  # noqa: BLE001
        built = None
        results.add("setup", "extra models built (textured box, tiny box, two-level assembly)", "FAIL", str(error))
    app.Visible = False

    def scenario(case, mode, model, package, committed=None, expected_kd=None, expected_centre=None, repeat=False, require_sw=True):
        out = case_dir(case.replace(" ", "_").replace("(", "").replace(")", "").replace(",", ""))
        code, values = live.probe(mode, model, out, package)
        if not results.check(case, "export ran to the end", code == 0 and values.get("status") == "ok", values.get("error", "")[:300].replace("\n", " ")):
            return None
        root = os.path.join(out, "export")
        info = check_live_package(results, case, os.path.join(root, package + "_obj"), values, expected_kd, require_sw)
        if info:
            results.guard(case, "mesh centre in link frame", lambda: check_frame_placement(results, case, info, os.path.join(root, package + "_stl"), expected_centre))
            if committed:
                results.guard(case, "committed example comparison", lambda: compare_with_committed(results, case, os.path.join(root, package + "_obj"), committed))
            if values.get("messages"):
                results.add(case, "message boxes shown during the export", "INFO", values["messages"][:200])
        if repeat and info:
            run1, run2, cancelled = values.get("obj.run1"), values.get("obj.run2"), values.get("obj.cancelled")
            differing = compare_packages(os.path.join(root, package + "_obj_run1"), os.path.join(root, package + "_obj_run2"))
            results.check(case, "second export into the same folder is the same (OBJ, MTL and the rest byte for byte, URDF and CSV numerically)",
                          bool(run1) and bool(run2) and not differing, ", ".join(differing))
            results.check(case, "cancelled export keeps the previous output byte for byte", bool(run2) and run2 == cancelled)
            results.check(case, "cancelled export leaves no temporary files", values.get("obj.cancelled.leftover") == "none", values.get("obj.cancelled.leftover", ""))
            results.add(case, "message shown for the cancelled export", "INFO", values.get("obj.cancelled.messages", "")[-300:])
        return info, values

    # ---- (1) assemblies
    scenario("TWO_BOXES assembly OBJ", "assembly", os.path.join(two_boxes, "TWO_BOXES_PER_CONFIG.SLDASM"), "TWO_BOXES_PER_CONFIG",
             expected_kd=[(0.0, 0.0, 1.0), (1.0, 0.0, 0.0)], repeat=True)
    scenario("3_DOF_ARM assembly OBJ", "assembly", os.path.join(three_dof, "3_DOF_ARM.SLDASM"), "3_DOF_ARM_description",
             committed=os.path.join(EXAMPLES, "3_DOF_ARM", "3_DOF_ARM_description_obj"), repeat=True)
    # ---- (2) parts
    scenario("BOX_RED part OBJ", "part", os.path.join(two_boxes, "BOX_RED.SLDPRT"), "BOX_RED",
             committed=os.path.join(EXAMPLES, "TWO_BOXES", "BOX_RED_description_obj"), expected_kd=[(1.0, 0.0, 0.0)])
    scenario("BOX_BLUE part OBJ", "part", os.path.join(two_boxes, "BOX_BLUE.SLDPRT"), "BOX_BLUE",
             committed=os.path.join(EXAMPLES, "TWO_BOXES", "BOX_BLUE_description_obj"), expected_kd=[(0.0, 0.0, 1.0)])
    # ---- (4) and (5c): built models
    if built:
        scenario("nested sub-assembly frame", "nested", os.path.join(built, "TOP.SLDASM"), "NESTED",
                 expected_kd=[(0.0, 0.0, 1.0), (1.0, 0.0, 0.0)], expected_centre=[0.04375, 0.0, 0.0125])
        scenario("very small model (3 mm box)", "part", os.path.join(built, "BOX_TINY.SLDPRT"), "BOX_TINY", expected_kd=[(1.0, 0.0, 0.0)])
        check_texture(live, results, case_dir, built)
    else:
        for case in ("nested sub-assembly frame", "very small model (3 mm box)", "texture error path"):
            results.add(case, "case run", "SKIP", "the extra models could not be built (see setup)")
    check_dialog(live, results, case_dir)


def check_texture(live, results, case_dir, built):
    case = "texture error path"
    out = case_dir("texture")
    code, values = live.probe("texture", os.path.join(built, "BOX_TEX.SLDPRT"), out, "BOX_TEX")
    if not results.check(case, "host ran to the end", code == 0 and values.get("status") == "ok", values.get("error", "")[:300].replace("\n", " ")):
        return
    fresh = values.get("fresh.files", "").split(",") if values.get("fresh.files") else []
    existing_before, existing_after = values.get("existing.seeded.before"), values.get("existing.seeded.after")
    results.check(case, "export fails cleanly (no unhandled exception)", values.get("fresh.exception") == "none" and values.get("existing.exception") == "none",
                  "%s / %s" % (values.get("fresh.exception"), values.get("existing.exception")))
    message = values.get("fresh.messages", "")
    results.check(case, "a message is shown and names the link", "BOX_TEX" in message, message[:200])
    language = values.get("language", "?")
    results.add(case, "message text (SolidWorks language id %s)" % language, "INFO", message[:300])
    results.check(case, "no URDF and no OBJ/MTL written (no half package)",
                  not any(p.endswith((".urdf", ".obj", ".mtl", ".csv")) for p in fresh), ", ".join(fresh))
    results.check(case, "failure marker file written", any("FAILED" in p.upper() or "INCOMPLETE" in p.upper() for p in fresh), ", ".join(fresh))
    results.check(case, "existing output (OBJ, MTL, URDF) is kept byte for byte", bool(existing_before) and existing_before == existing_after,
                  "" if existing_before == existing_after else "changed")
    results.check(case, "no temporary files left", values.get("fresh.leftover") == "none" and values.get("existing.leftover") == "none",
                  "%s / %s" % (values.get("fresh.leftover"), values.get("existing.leftover")))
    reason = values.get("convert.error", "")
    results.check(case, "the converter itself rejects the texture with an explicit message", "exture" in reason and "UV" in reason, reason[:250])
    log = values.get("log.texture", "")
    results.add(case, "exporter log mentions the texture", "INFO", (log or "no line found (the log file may be held by another process)")[:250])


def check_dialog(live, results, case_dir):
    case = "OBJ progress dialog"
    out = case_dir("dialog")
    code, values = live.probe("dialog", out)
    if not results.check(case, "dialog shown and captured", code == 0 and values.get("status") == "ok" and os.path.isfile(values.get("dialog.screenshot", "")),
                         values.get("error", "")[:300].replace("\n", " ")):
        return
    results.check(case, "Cancel button requests cancellation", values.get("dialog.cancel_before") == "False" and values.get("dialog.cancel_after") == "True")
    results.add(case, "display scale of the dialog (DeviceDpi)", "INFO", "%s dpi, client %s" % (values.get("dialog.dpi"), values.get("dialog.client")))
    results.add(case, "text clipping at this DPI (fixed-pixel layout is tracked in issue 131)", "INFO", values.get("dialog.clipped", "?"))
    results.add(case, "screenshot of the dialog", "INFO", "progress_dialog.png in the scenario folder (copied out with --artifacts)")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--offline", action="store_true", help="validate the committed example packages; needs no SolidWorks")
    mode.add_argument("--live", action="store_true", help="run the export through your own SolidWorks")
    parser.add_argument("--dll", help="(live) folder with a built SW2URDF.dll; the host program is compiled against a copy of it")
    parser.add_argument("--launcher", default=os.environ.get("SW2URDF_SW_LAUNCHER"),
                        help="(live) guarded launcher script that starts a new SolidWorks (or env SW2URDF_SW_LAUNCHER)")
    parser.add_argument("--launcher-python", help="(live) Python used to run the launcher (default: this one)")
    parser.add_argument("--pid", type=int, help="(live) use this SolidWorks instance, which must hold no documents; it is left running")
    parser.add_argument("--timeout", type=int, default=1800, help="(live) overall limit in seconds (default 1800)")
    parser.add_argument("--case-timeout", type=int, default=300, help="(live) limit per scenario in seconds (default 300)")
    parser.add_argument("--exe-hook", help="(live) command run on each compiled host program before it starts; {exe} is the file")
    parser.add_argument("--examples", help="examples folder to validate (default: examples/ of this checkout)")
    parser.add_argument("--build-commit", help="commit the tested build comes from (default: git HEAD of this checkout)")
    parser.add_argument("--json", help="write the result as JSON to this file")
    parser.add_argument("--markdown", help="write the result as a Markdown table to this file")
    parser.add_argument("--keep", action="store_true", help="(live) keep the scratch folder")
    parser.add_argument("--artifacts", help="(live) copy the exported packages, the host's probe.tsv files and the dialog screenshot to this folder")
    args = parser.parse_args()
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")   # translated message texts on a legacy console code page
        except (AttributeError, ValueError):
            pass
    if args.examples:
        global EXAMPLES
        EXAMPLES = os.path.abspath(args.examples)

    if args.live:
        if sys.platform != "win32":
            parser.error("--live needs Windows with SolidWorks")
        if not args.dll or not os.path.isfile(os.path.join(args.dll, "SW2URDF.dll")):
            parser.error("--live needs --dll <folder with SW2URDF.dll>")
        if not args.pid and not args.launcher:
            parser.error("--live needs --launcher (to start an instance) or --pid (to use one you own)")
    commit = args.build_commit
    if not commit:
        try:
            commit = subprocess.run(["git", "-C", REPO, "rev-parse", "--short", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
        except (OSError, subprocess.CalledProcessError):
            commit = "unknown"

    results = Results()
    started = time.time()
    if args.offline:
        results = run_offline()
        title = "OBJ + MTL regression, offline"
    else:
        run_live(args, results)
        title = "OBJ + MTL regression, live"
    print(results.table())
    elapsed = time.time() - started
    print("Run time %.1f s, build %s" % (elapsed, commit))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as stream:
            json.dump({"mode": "offline" if args.offline else "live", "build": commit, "date": datetime.date.today().isoformat(),
                       "seconds": round(elapsed, 1), "failed": len(results.failed), "checks": results.rows}, stream, indent=2)
    if args.markdown:
        with open(args.markdown, "w", encoding="utf-8") as stream:
            stream.write(results.markdown(commit, title))
    return 1 if results.failed else 0


if __name__ == "__main__":
    sys.exit(main())
