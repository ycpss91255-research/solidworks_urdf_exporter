# OBJ export

The assembly Mesh Format group includes **OBJ + MTL**. The part form has an
**OBJ + MTL (native colors)** checkbox; unchecked keeps STL. Both visual and
collision references use OBJ when selected. Link/joint structure is unchanged.

This is native SOLIDWORKS **3DXML export followed by conversion**, not native
SOLIDWORKS OBJ Save As. The conversion is done in C# inside `SW2URDF.dll`
(`ThreeDxmlReader.cs`, `ObjConverter.cs`). It needs no Python, no environment
variable, no external program and no extra file to install or sign. The
intermediate 3DXML stays beside the OBJ for diagnosis.

## What the converter does

* Reads the 3DXML zip/XML directly (strips, triangle lists and fans, instance
  matrices, material library, per-occurrence colour overrides).
* Coordinates are converted from native millimetres to metres without rotating
  axes. The global-to-link transform is applied through the URDF origins, as before.
* Every instance transform is applied. A mirroring transform (negative
  determinant) reverses the triangle winding so faces stay outward-facing.
* Vertex normals are angle-weighted and computed from the final geometry.
  Vertices are not merged.
* Runs on a worker thread. While a window is open a small modal dialog shows
  progress and has a **Cancel** button (the UI thread keeps pumping messages);
  without a window (tests, tools) the caller just waits. `ExportHelper.CancellationToken`
  can also cancel from code.
* Output is streamed to temporary files in the target folder and only replaces
  the existing `.obj`/`.mtl` once the whole conversion succeeded; any failure or
  cancellation keeps the previous output and removes the temporary files.
* Output is deterministic: same input and link name give identical bytes
  (UTF-8, no BOM, LF line endings).
* Image textures and UV coordinates are not supported. A material with a
  texture image and texture coordinates stops the export with an explicit
  message; the option is meant for solid-colour CAD.
* Assembly link coordinates are handled as before: meshes keep native global
  vertices and the link frame is appended to the visual/collision origins.
* A link-wide URDF `<material>` is omitted for OBJ so it does not override the
  MTL colours. Colour and texture fields on the export forms therefore have no
  effect in OBJ mode (see the PR description for the form follow-up).

## MTL fields

Each material is written explicitly; no loader default is relied upon:

| Field | Value | Source |
| --- | --- | --- |
| `Ka` | equal to `Kd` | derived |
| `Kd` | RGB 0-1, written as SOLIDWORKS stores it (sRGB) | face `Color`, else the material `DiffuseColor`, else 0.5 grey; replaced by the occurrence colour override when one matches |
| `Ks` | `SpecularColor * SpecularCoef` | 3DXML material; `0 0 0` without a material |
| `Ns` | `SpecularExponent * 1000` (SOLIDWORKS shininess 0-1 mapped onto the MTL range 0-1000) | 3DXML material; `0` without a material |
| `d` | opacity: face/override `alpha`, else `1 - Transparency` of the material | 3DXML |
| `illum` | `2` when `Ks` is non-zero, otherwise `1` | derived |

`Tr` is **not** written. `d` is the standard dissolve field and every loader
checked reads it. Three.js `MTLLoader` (r180) was run here: `d` alone, `d` with
`Tr = 1 - d`, and `Tr` alone give the same opacity, so a `Tr` line adds nothing
and could only conflict in a loader that reads `Tr` as dissolve. assimp reads
`d` and treats `Tr` as `1 - d` according to its source; it was not run here.

Colour space: values are written unchanged. Three.js `MTLLoader` treats
`Kd`/`Ks` as sRGB and converts to linear itself. How RViz/Ogre and Gazebo treat them was not tested here.
SOLIDWORKS colour values are used as sRGB.

### Names

* Material: `<link>_<RRGGBB>` plus `_a<percent>` when translucent, for example
  `base_link_cad1ee` or `base_link_ff4000_a50`. A different material that would
  get the same name receives `_2`, `_3`... in order of first use. Names depend
  only on the link and the values, so repeated exports are identical and
  materials of different links never share a name.
* Object: one `o <link>_<instance name>` line per occurrence (`.2`, `.3` for
  repeats), so importers can separate the parts. `usemtl` is repeated at the
  start of each object.
* Whitespace in link and instance names is replaced by `_`.

## Tests

Offline, no SOLIDWORKS, build output or COM registration needed (the converter
sources are compiled in memory from the repository):

```powershell
./scripts/test_obj_converter.ps1
# optional independent readers
./scripts/test_obj_converter.ps1 -PythonExecutable <python with trimesh and numpy> `
  -NodeExecutable <node> -ThreeDirectory <folder with Three.js r180 loaders>
```

It builds small synthetic 3DXML archives and checks instance transforms, units,
normals, mirrored winding, colour overrides, opacity, every MTL field and name,
reproducibility, a background run with progress, and that missing input, empty
geometry, textures, cancellation and a non-`.obj` target all leave an existing
output untouched. With `-PythonExecutable` it also runs `scripts/verify_obj.py`,
which reads the same 3DXML with trimesh and compares **every triangle**
(positions after transform and unit conversion, in order) with the OBJ and
validates the MTL fields. With `-NodeExecutable`/`-ThreeDirectory` it loads the
result with the official Three.js `OBJLoader`/`MTLLoader`
(`scripts/verify_obj_three.mjs`) and compares each material with the MTL.

`scripts/verify_obj.py` can also be run on any exported 3DXML/OBJ pair:

```powershell
python ./scripts/verify_obj.py source.3dxml output.obj validation.json
```

Note: trimesh duplicates triangles when one `<Faces>` element holds several
`<Face>` children. SOLIDWORKS writes one `<Face>` per `<Faces>`, which is what
the tests use, and the C# converter does not duplicate in either case.

Native integration checks (`scripts/test_obj_live.ps1`, `scripts/ObjExportCli.cs`,
`scripts/verify_obj_live.py`) bind an exact, already running SOLIDWORKS process
and export a disposable part and assembly with STL and OBJ; see the script
headers. They compare the OBJ with the native 3DXML and with an STL export of the
same link (0.2 mm surface limit).

## Example output

Ready-made OBJ exports, viewable without SOLIDWORKS (URDF, `.obj`, `.mtl`):

| Folder | Source model | Contents |
| --- | --- | --- |
| `examples/3_DOF_ARM/3_DOF_ARM_description_obj` | `3_DOF_ARM.SLDASM` and its saved URDF tree | assembly export, 4 links |
| `examples/TWO_BOXES/BOX_RED_description_obj` | `BOX_RED.SLDPRT` (50 mm, red) | part export, 1 link, red MTL |
| `examples/TWO_BOXES/BOX_BLUE_description_obj` | `BOX_BLUE.SLDPRT` (25 mm, blue) | part export, 1 link, blue MTL |

They were generated by this exporter (this branch, OBJ + MTL, fine mesh quality)
through `ExportHelper` (`ExportRobot` for the assembly, `ExportLink` for the
parts) on copies of the example models, driven by a small headless program (the same approach as the native
integration tests in PR 19). The intermediate `.3dxml` files and the export log
were removed to keep the folders small. To regenerate, export the same models
with the OBJ option from the add-in; the part export adds an `Origin_global`
coordinate system to the document if it has none, so use a copy of the part.

## Limits

Tested with SOLIDWORKS 2023 SP5 and .NET Framework 4.5.2. Image textures are not
supported. Very large meshes are converted one occurrence at a time, but the
parsed geometry of each part is kept in memory for the duration of the conversion.
