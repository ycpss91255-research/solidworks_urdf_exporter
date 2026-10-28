// Usage: node verify_obj_three.mjs <Three loader directory> <obj> <geometry report> <report>
// Loader directory contains official OBJLoader.js and MTLLoader.js with working
// imports of the matching Three.js release (validation used r180).
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const [directory, objPath, geometryReport, output] = process.argv.slice(2);
if (!output) throw Error('Expected loader directory, OBJ, geometry report, output report');
const {OBJLoader} = await import(pathToFileURL(path.resolve(directory, 'OBJLoader.js')));
const {MTLLoader} = await import(pathToFileURL(path.resolve(directory, 'MTLLoader.js')));
const expected = JSON.parse(fs.readFileSync(geometryReport, 'utf8'));
const text = fs.readFileSync(objPath, 'utf8');
const reference = /^mtllib (.+)$/m.exec(text);
if (!reference) throw Error('Missing mtllib');
const mtlPath = path.resolve(path.dirname(objPath), reference[1].trim());
const materials = new MTLLoader().parse(fs.readFileSync(mtlPath, 'utf8'), '');
materials.preload();
const object = new OBJLoader().setMaterials(materials).parse(text);
let triangles = 0;
const names = new Set();
object.traverse(mesh => {
  if (!mesh.isMesh) return;
  triangles += mesh.geometry.attributes.position.count / 3;
  for (const material of (Array.isArray(mesh.material) ? mesh.material : [mesh.material])) {
    names.add(material.name);
  }
});
if (triangles !== expected.triangles || names.size !== expected.materials) {
  throw Error(JSON.stringify({triangles, materials: [...names], expected}));
}
const result = {loader: 'Three.js OBJLoader + MTLLoader', triangles, materials: [...names], errors: []};
fs.writeFileSync(output, JSON.stringify(result, null, 2));
console.log(result);
