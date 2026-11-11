"""Live test of the per-SolidWorks-configuration URDF export settings (see TESTING.md, "Testing configurations").

What it does, start to finish:
  1. starts its OWN SolidWorks instance with your guarded launcher (--launcher, or the environment variable
     SW2URDF_SW_LAUNCHER), or binds to an instance you own and name with --pid;
  2. builds the small TWO_BOXES scratch models (two-configuration assembly and part, see make_test_fixtures.py)
     in a temporary folder, never touching real documents;
  3. compiles scripts/ConfigurationLiveProbe.cs against a built SW2URDF.dll and runs it. The probe drives the
     real ConfigurationSerialization / ExportHelper through COM and checks: configurations keep independent
     settings, the legacy shared attribute is the fallback, non-ASCII names round trip, renaming a
     configuration is detected, for the assembly and for the part;
  4. prints PASS/FAIL per check, closes the instance it started (never one given with --pid) and deletes the
     temporary folder.

Usage:
    python scripts/test_configuration_live.py --dll <folder with a built SW2URDF.dll> --launcher <launch_solidworks.py>
    python scripts/test_configuration_live.py --dll <folder> --pid <pid of an instance with no open documents>

Build the DLL without registering it:  powershell -File scripts\\test_build.ps1 -OutputDirectory <folder>
(sign it first if your policy requires signed binaries). The folder must also contain the SolidWorks interop DLLs
(the build copies them; otherwise they are copied from the SolidWorks installation folder).
Needs pywin32 and the .NET Framework 4 csc.exe. Exit code 0 when every check passes.
"""
import argparse
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

import pythoncom
import win32com.client

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_test_fixtures as fixtures  # noqa: E402


def csc_path():
    return os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe")


def solidworks_dir():
    for root in (os.environ.get("ProgramFiles", r"C:\Program Files"),):
        candidate = os.path.join(root, "SOLIDWORKS Corp", "SOLIDWORKS")
        if os.path.isdir(candidate):
            return candidate
    return None


def launch(launcher, work):
    manifest = os.path.join(work, "launch.json")
    session = os.path.join(work, "session.json")
    subprocess.run([sys.executable, launcher, "--launch", "--session-file", session, "--output", manifest], check=True)
    with open(manifest, encoding="utf-8") as stream:
        return int(json.load(stream)["pid"])


def exit_instance(pid):
    """Close only the instance we started: ask it to exit, then make sure the process is gone."""
    try:
        app = fixtures.bind(pid)
        app.CloseAllDocuments(True)
        app.ExitApp()
    except Exception as error:  # noqa: BLE001
        print("ExitApp failed (%s); terminating process %d" % (error, pid))
    deadline = time.time() + 60
    while time.time() < deadline:
        if subprocess.run(["tasklist", "/FI", "PID eq %d" % pid, "/NH"], capture_output=True, text=True).stdout.count(str(pid)) == 0:
            return
        time.sleep(1)
    subprocess.run(["taskkill", "/PID", str(pid), "/F"], check=False)


def compile_probe(dll_dir, thumbprint):
    sw = solidworks_dir()
    for name in ("SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll"):
        if not os.path.isfile(os.path.join(dll_dir, name)) and sw:
            shutil.copy(os.path.join(sw, name), dll_dir)
    exe = os.path.join(dll_dir, "ConfigurationLiveProbe.exe")
    framework = os.path.dirname(csc_path())
    references = ["System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll", "System.Runtime.Serialization.dll",
                  os.path.join(framework, "WPF", "WindowsBase.dll"), os.path.join(framework, "WPF", "PresentationFramework.dll")]
    references.append(os.path.join(dll_dir, "SW2URDF.dll"))
    # SW2URDF.dll embeds the SolidWorks interop types, so the probe must embed them too (/link)
    interop = [os.path.join(dll_dir, n) for n in ("SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll")]
    command = [csc_path(), "/nologo", "/target:exe", "/platform:x64", "/out:" + exe]
    command += ["/r:" + r for r in references] + ["/link:" + i for i in interop]
    command.append(os.path.join(HERE, "ConfigurationLiveProbe.cs"))
    subprocess.run(command, check=True)
    if thumbprint:
        sign = ("$c = Get-Item Cert:/CurrentUser/My/%s; $r = Set-AuthenticodeSignature -FilePath '%s' -Certificate $c "
                "-TimestampServer http://timestamp.digicert.com; if ($r.Status -ne 'Valid') { exit 1 }" % (thumbprint, exe))
        subprocess.run(["powershell", "-NoProfile", "-Command", sign], check=True)
    return exe


def build_models(pid, folder):
    """Same models as examples/TWO_BOXES, written to a scratch folder."""
    os.makedirs(folder, exist_ok=True)
    sys.argv = ["make_test_fixtures.py", "--pid", str(pid), "--output", folder]
    fixtures.main()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dll", required=True, help="folder containing a built SW2URDF.dll (the probe is built into it)")
    parser.add_argument("--launcher", default=os.environ.get("SW2URDF_SW_LAUNCHER"),
                        help="guarded launcher script that starts a new SolidWorks (or env SW2URDF_SW_LAUNCHER)")
    parser.add_argument("--pid", type=int, help="use this instance (not closed afterwards); it must hold no documents")
    parser.add_argument("--sign", metavar="THUMBPRINT",
                        help="code signing certificate (CurrentUser/My) used to sign the probe exe, if your policy requires signed binaries")
    parser.add_argument("--keep", action="store_true", help="keep the scratch folder")
    args = parser.parse_args()
    if not args.pid and not args.launcher:
        parser.error("give --launcher (to start an instance) or --pid (to use one you own)")
    dll_dir = os.path.abspath(args.dll)
    if not os.path.isfile(os.path.join(dll_dir, "SW2URDF.dll")):
        parser.error("no SW2URDF.dll in " + dll_dir)

    work = tempfile.mkdtemp(prefix="sw2urdf_cfg_live_")
    started = None
    code = 1
    try:
        exe = compile_probe(dll_dir, args.sign)
        pid = args.pid
        if not pid:
            pid = started = launch(args.launcher, work)
        print("Using SolidWorks process %d%s" % (pid, " (started by this script)" if started else ""))
        models = os.path.join(work, "models")
        build_models(pid, models)
        code = subprocess.run([exe, str(pid), models, os.path.join(work, "run")], cwd=dll_dir).returncode
    finally:
        if started:
            exit_instance(started)
        for name in ("ConfigurationLiveProbe.exe",):
            try:
                os.remove(os.path.join(dll_dir, name))
            except OSError:
                pass
        if args.keep:
            print("Scratch folder kept: " + work)
        else:
            shutil.rmtree(work, ignore_errors=True)
    return code


if __name__ == "__main__":
    pythoncom.CoInitialize()
    sys.exit(main())
