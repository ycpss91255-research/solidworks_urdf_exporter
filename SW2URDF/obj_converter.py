"""Convert native 3DXML to metre OBJ + MTL, including occurrence colours.

Requires trimesh==5.2.0 and numpy. No CAD automation is performed here.
"""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile
import tempfile
import os
import numpy as np
import trimesh


def _write(source, target):
    source, target = Path(source), Path(target)
    with zipfile.ZipFile(source) as archive:
        manifest = ET.fromstring(archive.read('Manifest.xml'))
        root = ET.fromstring(archive.read(manifest.find('{*}Root').text))
    overrides = []
    for prop in root.findall('.//{*}DefaultViewProperty'):
        color = prop.find('{*}GraphicProperties/{*}SurfaceAttributes/{*}Color')
        if color is not None:
            ids = tuple(e.text.split('#')[-1] for e in prop.findall('{*}OccurenceId/{*}id'))
            rgba = tuple(float(color.get(k, '1')) for k in ('red', 'green', 'blue', 'alpha'))
            if ids:
                overrides.append((ids, rgba))
    scene = trimesh.load_scene(source, process=False)
    # Trimesh's 3DXML loader returns native millimetres. Do not rotate axes:
    # sw2urdf already writes the global-to-link transform into URDF origins.
    materials = {}
    count = 0
    with target.open('w', encoding='utf-8', newline='\n') as obj:
        obj.write('# sw2urdf: metres, native axes\nmtllib ' + target.with_suffix('.mtl').name + '\n')
        offset = 1
        for node in scene.graph.nodes_geometry:
            transform, geometry = scene.graph[node]
            native = scene.geometry[geometry]
            # ColorVisuals.copy() derives unused vertex colours through SciPy.
            # Copy geometry only; face colours are read directly below.
            mesh = trimesh.Trimesh(vertices=native.vertices.copy(), faces=native.faces.copy(), process=False)
            mesh.apply_transform(transform)
            if getattr(native.visual, 'kind', None) == 'texture':
                raise ValueError('Image textures are not supported by the solid-colour OBJ exporter')
            if not np.isfinite(mesh.vertices).all():
                raise ValueError('3DXML contains non-finite vertices')
            ids = node.split('#')[-1].split(':')
            def matches(wanted):
                iterator = iter(ids)
                return all(any(v == w for v in iterator) for w in wanted)
            applicable = [(wanted, color) for wanted, color in overrides if matches(wanted)]
            colors = native.visual.face_colors.astype(float) / 255.0
            if applicable:
                colors[:] = max(applicable, key=lambda x: (len(x[0]), ids.index(x[0][-1])))[1]
            for vertex in mesh.vertices / 1000.0:
                obj.write('v %.17g %.17g %.17g\n' % tuple(vertex))
            # Angle-weighted vertex normals, using NumPy only. SciPy is not a
            # runtime dependency of this exporter.
            normals = np.zeros_like(mesh.vertices)
            for corner in range(3):
                np.add.at(normals, mesh.faces[:, corner], mesh.face_normals * mesh.face_angles[:, corner, None])
            lengths = np.linalg.norm(normals, axis=1)
            normals /= np.where(lengths == 0, 1, lengths)[:, None]
            for normal in normals:
                obj.write('vn %.17g %.17g %.17g\n' % tuple(normal))
            current = None
            for face, color in zip(mesh.faces, colors):
                key = tuple(color)
                if key not in materials:
                    materials[key] = 'material_%d' % len(materials)
                if current != key:
                    obj.write('usemtl ' + materials[key] + '\n')
                    current = key
                indices = face + offset
                obj.write('f ' + ' '.join('%d//%d' % (i, i) for i in indices) + '\n')
                count += 1
            offset += len(mesh.vertices)
    if count == 0:
        target.unlink()
        raise ValueError('3DXML contains no triangles')
    with target.with_suffix('.mtl').open('w', encoding='utf-8', newline='\n') as mtl:
        for rgba, name in materials.items():
            mtl.write('newmtl %s\nKd %.9g %.9g %.9g\nd %.9g\nillum 1\n\n' % ((name,) + rgba))


def convert(source, target):
    target = Path(target)
    if target.suffix.lower() != '.obj':
        raise ValueError('Target must have .obj extension')
    with tempfile.TemporaryDirectory(dir=target.parent, prefix='.obj-') as temporary:
        staged = Path(temporary) / target.name
        _write(source, staged)
        os.replace(staged.with_suffix('.mtl'), target.with_suffix('.mtl'))
        os.replace(staged, target)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source')
    parser.add_argument('target')
    args = parser.parse_args()
    convert(args.source, args.target)
