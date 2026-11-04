# OBJ export development build

The assembly Mesh Format group includes **OBJ + MTL**. The part form has an
**OBJ + MTL (native colors)** checkbox; unchecked keeps STL. Both visual and
collision references use OBJ when selected. Link/joint structure is unchanged.

This is native SOLIDWORKS **3DXML export followed by conversion**, not native
SOLIDWORKS OBJ Save As. Intermediate 3DXML files stay beside the OBJ for diagnosis.
Coordinates are converted from native millimetres to metres without rotating
axes. The existing global-to-link URDF origins position the meshes.
The MTL carries per-face/occurrence solid colours and opacity. A link-wide URDF
material is omitted for OBJ so it does not override the native colours.
Image texture/UV preservation is not implemented; this option is for solid-colour CAD.

Install Python 3.11+ and `python -m pip install -r requirements-obj.txt`.
Before starting SOLIDWORKS, set `SW2URDF_PYTHON` to the absolute Python executable
of that environment. The setting is process-local/inherited; changing it after
SOLIDWORKS starts will not update the running application's environment.
Missing Python, failed native export, or failed conversion aborts export.
The converter is copied beside SW2URDF.dll by MSBuild and the existing installer
wildcard includes it. No automatic dependency installation or registry change occurs.

Offline checks:

```powershell
./scripts/test_build.ps1
python ./scripts/test_obj_converter.py
powershell -NoProfile -File ./scripts/test_obj_urdf.ps1
python ./scripts/verify_obj.py source.3dxml output.obj validation.json
node ./scripts/verify_obj_three.mjs <Three-loader-directory> output.obj validation.json import.json
```

The development implementation has passed offline conversion of the MR15 base
3DXML (1,350,751 triangles, 3 materials), full ordered triangle comparison,
independent OBJ/MTL reload, synthetic instance transform/colour/opacity tests,
failure preservation, and C# URDF material serialization checks.
The final independent reload used official Three.js r180 OBJLoader/MTLLoader.
Trimesh's large OBJ reload exhausted available memory on later runs; the geometry
verifier streams OBJ lines and compares all triangles in batches. Its optional
`--trimesh-reload` enables that separate high-memory reload check.

Native integration checks (use copies of all four `examples/3_DOF_ARM` files):

```powershell
./scripts/test_obj_live.ps1 -TargetPid $ownedPid -ModelsDirectory $copies `
  -OutputDirectory $output -PythonExecutable $python
& $python ./scripts/verify_obj_live.py $output $copies validation.json
# After CLI passes, optional visible form acceptance using the same owned PID:
./scripts/test_obj_live.ps1 -TargetPid $ownedPid -ModelsDirectory $copies `
  -OutputDirectory $formOutput -PythonExecutable $python -Form part
# Repeat with -Form assembly, select OBJ + MTL, and export the meshes.
```

These checks bind an existing exact PID, reject unrelated open documents,
localize copied assembly references, cold reopen inputs and export part and
four-link assembly packages. They compare every OBJ triangle with native 3DXML,
reload relocated packages, compare joint/inertia values and mesh poses with STL,
and measure bidirectional vertex/face-centroid distance to the opposite surface.
The STL reference is explicitly Fine for every link; the unchanged engineering
comparison limit is 0.2 mm. Coarse STL is not an equivalent Fine reference.
Run into a new output directory to retain evidence from earlier runs.

Validation scope: the integration fork passed native part/assembly CLI and form
exports. The clean ROS master port builds against .NET Framework 4.5.2. Run
fresh native checks on this branch before using the fork measurements as PR
evidence: the upstream STL exporter and package defaults differ from the fork.