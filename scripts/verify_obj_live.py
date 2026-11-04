"""Validate the native CLI test's packages, including relocation and STL regression."""
import argparse
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import numpy as np
import trimesh
from verify_obj import verify


def equivalent(a, b):
    assert a.tag == b.tag and a.attrib.keys() == b.attrib.keys(), (a.tag, b.tag)
    for key, value in a.attrib.items():
        try:
            left, right = np.array(list(map(float, value.split()))), np.array(list(map(float, b.get(key).split())))
        except ValueError:
            assert value == b.get(key), (key, value, b.get(key))
        else:
            np.testing.assert_allclose(left, right, atol=1e-12, rtol=1e-10)
    assert len(a) == len(b)
    for left, right in zip(a, b):
        equivalent(left, right)


def mesh_in_link(element, package):
    mesh = element.find('geometry/mesh')
    uri = mesh.get('filename')
    assert uri.startswith('package://' + package.name + '/')
    file = package / uri.split('/', 3)[3]
    assert file.is_file(), file
    scene = trimesh.load_scene(file, process=False)
    vertices, faces, offset = [], [], 0
    for node in scene.graph.nodes_geometry:
        transform, name = scene.graph[node]
        geometry = scene.geometry[name]
        vertices.append(trimesh.transform_points(geometry.vertices, transform))
        faces.append(geometry.faces + offset)
        offset += len(geometry.vertices)
    vertices = np.concatenate(vertices)
    origin = element.find('origin')
    if origin is not None:
        r, p, y = map(float, origin.get('rpy', '0 0 0').split())
        rotation = trimesh.transformations.euler_matrix(r, p, y, axes='sxyz')[:3, :3]
        vertices = vertices @ rotation.T + np.array(list(map(float, origin.get('xyz', '0 0 0').split())))
    return trimesh.Trimesh(vertices=vertices, faces=np.concatenate(faces), process=False)


def vertices_in_link(element, package):
    return mesh_in_link(element, package).vertices


def surface_delta(a, b):
    maximum = 0.0
    for source, target in ((a, b), (b, a)):
        samples = np.concatenate([source.vertices, source.triangles_center])
        for start in range(0, len(samples), 64):
            _, distance, _ = trimesh.proximity.closest_point_naive(target, samples[start:start + 64])
            maximum = max(maximum, float(distance.max()))
    return maximum


def check(output, models):
    result = {}
    for mode in ('part', 'assembly'):
        package_name = mode + '_test'
        baseline = output / (mode + '-STL') / package_name
        original = output / (mode + '-OBJ') / package_name
        relocated = output / 'relocated' / package_name
        shutil.copytree(original, relocated, dirs_exist_ok=True)
        left = ET.parse(baseline / 'urdf' / (package_name + '.urdf')).getroot()
        right = ET.parse(relocated / 'urdf' / (package_name + '.urdf')).getroot()
        assert left.attrib == right.attrib
        assert [l.get('name') for l in left.findall('link')] == [l.get('name') for l in right.findall('link')]
        assert len(left.findall('joint')) == len(right.findall('joint'))
        for a, b in zip(left.findall('joint'), right.findall('joint')):
            equivalent(a, b)
        assets = {}
        for a, b in zip(left.findall('link'), right.findall('link')):
            equivalent(a.find('inertial'), b.find('inertial'))
            assert b.find('visual/material') is None
            cross_format = {}
            for kind in ('visual', 'collision'):
                am = mesh_in_link(a.find(kind), baseline)
                bm = mesh_in_link(b.find(kind), relocated)
                av, bv = am.vertices, bm.vertices
                delta = surface_delta(am, bm)
                # Independent tessellations need a surface check rather than
                # exact vertex/bounds equality. Retain the handoff's 0.2 mm
                # engineering limit and report every measured discrepancy.
                cross_format[kind] = dict(surface_sample_max_m=delta, limit_m=0.0002,
                    passed=delta <= 0.0002,
                    bbox_max_delta_m=float(np.abs(np.array([av.min(0), av.max(0)]) -
                                                  np.array([bv.min(0), bv.max(0)])).max()))
                mesh = b.find(kind + '/geometry/mesh')
                assert mesh.get('filename').endswith('.obj')
            file = relocated / b.find('visual/geometry/mesh').get('filename').split('/', 3)[3]
            proof = verify(file.with_suffix('.3dxml'), file, reload=True)
            proof['stl_cross_format'] = cross_format
            assets[b.get('name')] = proof
        references = (output / (mode + '-OBJ-references.txt')).read_text().splitlines()
        assert all(Path(path).resolve().is_relative_to(models.resolve()) for path in references)
        result[mode] = dict(links=len(right.findall('link')), joints=len(right.findall('joint')),
                            joint_inertia_regression=True,
                            surface_regression=all(x['passed'] for asset in assets.values()
                                for x in asset['stl_cross_format'].values()),
                            relocated_reload=True, references_local=True, assets=assets)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('models', type=Path)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    result = check(args.output, args.models)
    args.report.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
    if not all(mode['surface_regression'] for mode in result.values()):
        raise SystemExit('FAIL: STL/OBJ surface discrepancy; see saved report')
