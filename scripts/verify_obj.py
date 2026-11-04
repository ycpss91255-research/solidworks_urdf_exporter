"""Read back OBJ and compare all triangles with native 3DXML scene instances."""
import argparse
import json
import gc
from pathlib import Path
import numpy as np
import trimesh


def verify(source, target, reload=False):
    scene = trimesh.load_scene(source, process=False)
    nodes = list(scene.graph.nodes_geometry)
    vertex_count = sum(len(scene.geometry[scene.graph[node][1]].vertices) for node in nodes)
    face_count = sum(len(scene.geometry[scene.graph[node][1]].faces) for node in nodes)
    expected = np.empty((face_count, 3, 3), dtype=np.float64)
    cursor = 0
    for node in nodes:
        transform, geometry = scene.graph[node]
        native = scene.geometry[geometry]
        mesh = trimesh.Trimesh(vertices=native.vertices.copy(), faces=native.faces.copy(), process=False)
        mesh.apply_transform(transform)
        expected[cursor:cursor + len(mesh.faces)] = mesh.triangles / 1000.0
        cursor += len(mesh.faces)
    del scene, mesh
    gc.collect()
    vertices = np.empty((vertex_count, 3), dtype=np.float64)
    normals = np.empty_like(vertices)
    faces = np.empty((face_count, 3), dtype=np.int32)
    materials = set()
    library = None
    vi = ni = fi = 0
    with Path(target).open(encoding='utf-8') as stream:
        for line in stream:
            fields = line.split()
            if not fields:
                continue
            if fields[0] == 'v':
                vertices[vi] = list(map(float, fields[1:]))
                vi += 1
            elif fields[0] == 'vn':
                normals[ni] = list(map(float, fields[1:]))
                ni += 1
            elif fields[0] == 'f':
                assert len(fields) == 4
                indices = [part.split('//') for part in fields[1:]]
                assert all(int(v) == int(n) for v, n in indices)
                faces[fi] = [int(v) - 1 for v, n in indices]
                fi += 1
            elif fields[0] == 'mtllib':
                library = line.strip()[7:]
            elif fields[0] == 'usemtl':
                materials.add(fields[1])
    assert vi == ni == vertex_count and fi == face_count
    assert np.isfinite(vertices).all() and np.isfinite(normals).all()
    assert faces.min() >= 0 and faces.max() < len(vertices)
    assert len(vertices) == len(normals)
    error = 0.0
    for start in range(0, face_count, 10000):
        stop = min(start + 10000, face_count)
        error = max(error, float(np.max(np.abs(vertices[faces[start:stop]] - expected[start:stop]))))
    del expected, normals
    assert error < 1e-12, error
    mtl = Path(target).parent / library
    defined = {line.split()[1] for line in mtl.read_text().splitlines() if line.startswith('newmtl ')}
    assert materials == defined
    result = dict(triangles=len(faces), vertices=len(vertices), materials=len(materials),
                  max_coordinate_error_m=error,
                  bounds_m=[vertices.min(axis=0).tolist(), vertices.max(axis=0).tolist()])
    del vertices, faces
    gc.collect()
    # Optional large-memory importer. A separate importer may be used instead;
    # the geometry check above always covers every triangle.
    result['independent_import'] = False
    if reload:
        loaded = trimesh.load_scene(target, process=False)
        assert sum(len(g.faces) for g in loaded.geometry.values()) == result['triangles']
        result['independent_import'] = True
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source')
    parser.add_argument('target')
    parser.add_argument('report')
    parser.add_argument('--trimesh-reload', action='store_true')
    args = parser.parse_args()
    result = verify(args.source, args.target, args.trimesh_reload)
    Path(args.report).write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
