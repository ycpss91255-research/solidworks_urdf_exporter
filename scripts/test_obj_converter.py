"""Offline regressions: instance transform, units, colours, and failed writes."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import numpy as np
import trimesh

spec = importlib.util.spec_from_file_location('obj_converter', Path(__file__).parents[1] / 'SW2URDF/obj_converter.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)


class ObjTests(unittest.TestCase):
    def test_instances_colours_units_and_normals(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            source, target = folder / 'source.3dxml', folder / 'mesh.obj'
            xml = '''<Model><DefaultViewProperty><OccurenceId><id>1</id><id>2</id></OccurenceId>
              <GraphicProperties><SurfaceAttributes><Color red="1" green="0.25" blue="0" alpha="0.5"/>
              </SurfaceAttributes></GraphicProperties></DefaultViewProperty></Model>'''
            with zipfile.ZipFile(source, 'w') as archive:
                archive.writestr('Manifest.xml', '<Manifest><Root>root.xml</Root></Manifest>')
                archive.writestr('root.xml', xml)
            scene = trimesh.Scene()
            mesh = trimesh.creation.box(extents=[1000, 2000, 3000])
            mesh.visual.face_colors = [0, 0, 255, 255]
            transform = trimesh.transformations.rotation_matrix(np.pi/2, [0, 0, 1])
            transform[:3, 3] = [1000, 2000, 3000]
            scene.add_geometry(mesh, node_name='instance#1:2', transform=transform)
            scene.add_geometry(mesh.copy(), node_name='instance#1:3')
            with patch.object(converter.trimesh, 'load_scene', return_value=scene):
                converter.convert(source, target)
            loaded = trimesh.load_scene(target, process=False)
            assert sum(len(g.faces) for g in loaded.geometry.values()) == 24
            mtl = target.with_suffix('.mtl').read_text()
            self.assertIn('Kd 1 0.25 0\nd 0.5', mtl)
            self.assertIn('Kd 0 0 1\nd 1', mtl)
            vertices = np.array([list(map(float, line.split()[1:])) for line in target.read_text().splitlines() if line.startswith('v ')])
            normals = np.array([list(map(float, line.split()[1:])) for line in target.read_text().splitlines() if line.startswith('vn ')])
            expected = trimesh.transform_points(mesh.vertices, transform) / 1000
            np.testing.assert_allclose(vertices[:8], expected, atol=1e-15)
            np.testing.assert_allclose(vertices[8:], mesh.vertices / 1000, atol=1e-15)
            np.testing.assert_allclose(normals[:8], trimesh.transform_points(mesh.vertex_normals, transform, translate=False), atol=1e-15)

    def test_failure_preserves_existing_output(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'mesh.obj'
            target.write_text('existing OBJ')
            target.with_suffix('.mtl').write_text('existing MTL')
            with self.assertRaises(FileNotFoundError):
                converter.convert(Path(folder) / 'missing.3dxml', target)
            self.assertEqual(target.read_text(), 'existing OBJ')
            self.assertEqual(target.with_suffix('.mtl').read_text(), 'existing MTL')
            self.assertEqual(len(list(Path(folder).iterdir())), 2)


if __name__ == '__main__':
    unittest.main()
