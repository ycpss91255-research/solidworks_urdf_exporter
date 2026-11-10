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
const THREE = await import(pathToFileURL(path.resolve(directory, 'three.module.js')));
const expected = JSON.parse(fs.readFileSync(geometryReport, 'utf8'));
const text = fs.readFileSync(objPath, 'utf8');
const reference = /^mtllib (.+)$/m.exec(text);
if (!reference) throw Error('Missing mtllib');
const mtlPath = path.resolve(path.dirname(objPath), reference[1].trim());
const materials = new MTLLoader().parse(fs.readFileSync(mtlPath, 'utf8'), '');
materials.preload();
// Every MTL field must come back from Three.js as written (Kd/Ks are sRGB in the file
// and converted to the working space by the loader, so read them back as sRGB).
const fields = {};
let current;
for (const line of fs.readFileSync(mtlPath, 'utf8').split('\n')) {
  const [key, ...rest] = line.trim().split(/\s+/);
  if (key === 'newmtl') fields[current = rest.join(' ')] = {};
  else if (key && !key.startsWith('#')) fields[current][key] = rest.map(Number);
}
const near = (a, b) => Math.abs(a - b) < 1e-5;
for (const [name, f] of Object.entries(fields)) {
  const material = materials.materials[name];
  if (!material) throw Error('Material not loaded: ' + name);
  const kd = material.color.getRGB({}, THREE.SRGBColorSpace);
  const ks = material.specular.getRGB({}, THREE.SRGBColorSpace);
  const ok = near(kd.r, f.Kd[0]) && near(kd.g, f.Kd[1]) && near(kd.b, f.Kd[2]) &&
    near(ks.r, f.Ks[0]) && near(ks.g, f.Ks[1]) && near(ks.b, f.Ks[2]) &&
    near(material.shininess, f.Ns[0]) && near(material.opacity, f.d[0]) &&
    material.transparent === (f.d[0] < 1);
  if (!ok) throw Error('Three.js material differs from MTL: ' + name);
}const object = new OBJLoader().setMaterials(materials).parse(text);
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
const result = {loader: 'Three.js OBJLoader + MTLLoader', triangles, materials: [...names], errors: [], materialsChecked: Object.keys(fields).length};
fs.writeFileSync(output, JSON.stringify(result, null, 2));
console.log(result);
