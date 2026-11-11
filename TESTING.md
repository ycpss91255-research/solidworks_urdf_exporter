# Testing SW2URDF

The tests are xUnit tests in `SW2URDF/Test/`. Some need SolidWorks, some do not. See
`TestRunner/README.md` for building and starting the runner.

## Choosing the SolidWorks instance

`SWTestFixture` starts SolidWorks through the generic `SldWorks.Application` ProgID by default, which lets
Windows pick any instance. To bind the tests to one process instead, set the process id before running:

    set SW2URDF_TEST_PID=12345
    TestRunner.exe TestExportHelper

The fixture then looks up `SolidWorks_PID_12345` in the Running Object Table, checks that
`ISldWorks.GetProcessID` agrees, and uses only that instance. If it is not running, or is registered with
another process id, the tests fail with a clear message; they never fall back to another instance. Without
the variable the previous behaviour is unchanged (issue #16).

The SolidWorks tests open and close documents, so use an instance that holds nothing you need.

`SW2URDF_EXAMPLES_DIR` (optional) points the tests at an `examples` folder when the test DLL is not in the
usual `SW2URDF\bin\...` folder inside the repository, for example after a build into another output folder.

## Tests that do not need SolidWorks

`TestSolidWorksBinding` (the variable above) and `TestTwoBoxesFixture` (the example below exists and stays
small) never start or connect to SolidWorks. Parameterless `[Fact]` tests like these can be run from a
built DLL without a runner:

    powershell -File scripts\test_logic.ps1 -AssemblyPath <path to SW2URDF.dll> [-Filter <class name part>]

(`[Theory]` cases need the xUnit runner.)

## The TWO_BOXES example

`examples/TWO_BOXES` is a minimal, abstract model for tests of configuration handling and mesh export
(about 0.2 MB in total):

| File | Content |
|------|---------|
| `BOX_RED.SLDPRT` | 50 mm cube, red, configuration `Default` |
| `BOX_BLUE.SLDPRT` | 25 mm square, blue, configurations `Short` (20 mm high) and `Tall` (60 mm high) |
| `TWO_BOXES.SLDASM` | both parts at 0 and 100 mm along X; configuration `CFG_BOTH` has both boxes (the blue one in `Tall`), `CFG_RED_ONLY` suppresses the blue box |

The model carries no URDF export data. Tests that need a link tree build one in code. Tests that export or
save should work on a temporary copy, not on the files in `examples`.

Names and expectations are in `SW2URDF/Test/TwoBoxesModel.cs`.

### Regenerating it

`scripts/make_test_fixtures.py` (pywin32) rebuilds the three files. It binds to the SolidWorks process you
name through its Running Object Table entry, and never starts or closes SolidWorks:

    python scripts\make_test_fixtures.py --pid 12345 [--output <folder>]

It replaces existing files of the same names in the output folder (default `examples/TWO_BOXES`) and needs
the default part and assembly templates configured in that SolidWorks. Keep `TwoBoxesModel.cs` in sync when
changing it.

## Testing configurations (SolidWorks configuration x URDF export configuration)

The exporter keeps its link/joint settings **per SolidWorks configuration**. Three ways to test that, from
cheapest to most thorough: SolidWorks-free unit tests, a live script that does everything by itself, and a
manual checklist for the user interface.

### What is stored where

| Item | Where | Behaviour |
|------|-------|-----------|
| Settings of one SolidWorks configuration (created when the exporter opens with that configuration active, not for all configurations at once) | one SolidWorks attribute per configuration, named exactly like the configuration (definition `URDF Export Configuration Data`) | written when you save while that configuration is active; read first |
| Grouping | feature tree folder `URDF Export Configuration` holding those attributes | appearance only; created on the first save, rebuilt when attributes are added |
| Legacy shared settings | attribute `URDF Export Configuration (v1.4)` (older: `(v1.3)`, `URDF Export Configuration`) | read only as a fallback when the active configuration has no attribute of its own; never changed by a save, never renamed |

The same applies to assemblies and parts (the part exporter stores the inertial, visual and collision values
this way). Details and versioning rules: `CONFIGURATION_FORMAT.md`.
Renaming a SolidWorks configuration does not rename its attribute: the settings are then not found for the new
name (the legacy fallback applies, else "new settings") and the old attribute is reported in the log as an
orphan. Nothing is deleted or blocked. The log is `sw2urdf_logs\sw2urdf.log` in your user folder.

### 1. Unit tests (no SolidWorks needed)

`TestConfigurationFormat` (format versions and fallback order) and `TestConfigurationStatus` (texts of the
configuration indicator) run with `scripts\test_logic.ps1 -AssemblyPath <path to SW2URDF.dll> -Filter Configuration`.
The `TestPerConfiguration` and `TestPartPerConfiguration` classes cover the same behaviour with the real
SolidWorks API through the xUnit runner (see "Choosing the SolidWorks instance").

### 2. Automated live check: `scripts/test_configuration_live.py`

It starts a SolidWorks instance of its own (or uses one you own), builds the small two-configuration scratch
models in a temporary folder, runs `scripts/ConfigurationLiveProbe.cs` against a built `SW2URDF.dll` and prints
`PASS`/`FAIL` for each check. It closes the instance it started and deletes the temporary folder; it never
touches other SolidWorks instances or real documents.

Build a private copy of the exporter without registering it, then run the script:

    powershell -File scripts\test_build.ps1 -OutputDirectory <build folder>
    python scripts\test_configuration_live.py --dll <build folder> --launcher <path to your guarded launch script>

* `--launcher` (or environment variable `SW2URDF_SW_LAUNCHER`) is a script that starts a new SolidWorks and
  writes `{"pid": ...}` to the file given with `--output` (the arguments `--launch --session-file <file>
  --output <file>` are passed). Alternatively pass `--pid <pid>` of an instance you own that holds no documents;
  that instance is left running.
* `--sign <certificate thumbprint>` signs the compiled probe with a certificate from the current user's
  store, for environments that only run signed code. Sign `SW2URDF.dll` yourself in that case.
* `--keep` keeps the temporary folder. Needs pywin32 and the .NET Framework 4 compiler (`csc.exe`).
* Exit code 0 when every check passes. The checks, grouped:
  * assembly, `CFG_BOTH` / `CFG_RED_ONLY`: different link names per configuration, independent after switching
    back, the indicator source (`New` / `Configuration` / `Shared`), one attribute per configuration and the
    feature tree folder, survival of save, close and reopen;
  * legacy: a model with only the shared attribute loads it in every configuration; saving one configuration
    changes only that one;
  * non-ASCII: a Chinese/accented/Greek link name and a configuration with a non-ASCII name round trip,
    also through save and reopen;
  * rename: the old attribute is detected as orphan, loading is not blocked;
  * part, `Short` / `Tall`: independent values, legacy fallback, non-ASCII text (the part exporter does not
    store the link name, so the visual material name is used).

### 3. Manual checklist in SolidWorks

Use copies of `examples/TWO_BOXES` (never the files in the repository). Open the add-in from
`Tools > Export as URDF`. The exporter shows a status line with the active configuration and where its settings
come from: *"Configuration: X. Settings saved for this configuration."*, *"... loaded from the shared (legacy)
data; they are saved to this configuration on the next save."* or *"... New settings: nothing is saved ..."*.
(The language follows the SolidWorks language; with zh-TW the line is Chinese.)

**Assembly** (`TWO_BOXES.SLDASM`):

1. Activate `CFG_BOTH` in the ConfigurationManager. Open the exporter. Expect the status line to say
   *new settings* for `CFG_BOTH`. Build a link (the base link containing both boxes), name it `link_both`,
   save/close the exporter.
1. Activate `CFG_RED_ONLY`. Open the exporter. Expect *new settings* and an empty tree: the other
   configuration's link must not appear. Name the link `link_red` (red box only), save and close.
1. Activate `CFG_BOTH` again, open the exporter. Expect `link_both` and *Settings saved for this
   configuration*. Repeat for `CFG_RED_ONLY`: expect `link_red`. This is the independence check.
1. In the feature tree expand `URDF Export Configuration`: one entry per configuration you saved, named like it.
1. Save the model, close it, reopen it, repeat step 3. Expect the same.
1. Legacy fallback: open a model saved by an exporter before per-configuration storage (only the attribute
   `URDF Export Configuration (v1.4)`), or in a copy delete the folder and the per-configuration attributes
   from the feature tree. Expect *loaded from the shared (legacy) data* in every configuration, and the
   same tree in all of them. Save in one configuration: only that configuration now reports *Settings saved*,
   the others still show the legacy data.
1. Non-ASCII: name the link `連桿_底座` and save. Reopen the exporter and the model. Expect the name unchanged
   (no `?` and no empty name). Also create a configuration named `配置_甲`, save a link there and switch back
   and forth.
1. Rename a configuration (`CFG_RED_ONLY` to `CFG_RENAMED`). Open the exporter: expect the legacy data or
   *new settings* for `CFG_RENAMED`, no error dialog. In the log expect a warning "URDF configuration
   attributes that match no SolidWorks configuration (a configuration may have been renamed): CFG_RED_ONLY".
   Save once to give the new name its own settings.
1. Suppressed part: with `CFG_RED_ONLY` active and a link that holds the blue box (saved under the shared
   data), expect the missing-component warning only in that configuration.

**Part** (`BOX_BLUE.SLDPRT`, configurations `Short` and `Tall`), same ideas with the part exporter:

1. Activate `Short`, open the part exporter, change a visual colour, save. Activate `Tall`: expect *new
   settings* and the original colour. Change the colour differently, save.
1. Switch back and forth: each configuration shows its own colour. Save, close and reopen the part and repeat.
1. Legacy fallback and the status line behave as in steps 4 to 6 above.

**Save before opening the exporter**: if your build asks to save the model before the exporter opens (so that
the attributes are written to the file), accept it and check that the saved file reopens with the settings;
decline it and check that nothing is lost in the open session.

### Cleaning up

The live script cleans up after itself. After manual tests close the models without saving the copies and delete
them. The log file and the private build folder are yours to delete.

## Regression results of the example models

Each example folder with a committed package was copied, opened in SolidWorks 2023 and exported through the real
exporter forms (open with the saved link tree, next through the pages, export with meshes; OBJ+MTL for the OBJ
examples; once per SolidWorks configuration for `TWO_BOXES_PER_CONFIG`). Numbers were compared with the committed
files at 1e-9 relative tolerance, meshes by triangle count and bounding box. Build: master `ee4f946`; the two
part rows marked * were repeated with the fixes of issues 124 and 126.

| Model | Compared | Result |
|-------|----------|--------|
| `3_DOF_ARM` (STL) | URDF 146 numbers, CSV, 4 meshes | equivalent; mesh triangle counts differ by tessellation (volume within 0.04 %) |
| `3_DOF_ARM` (OBJ) | URDF 130 numbers, CSV, 4 OBJ, MTL colours | equivalent; the intermediate `.3dxml` files stay beside the OBJ (documented) |
| `4_WHEELER` | URDF 185 numbers, CSV, 5 meshes | equivalent |
| `ORIGINAL_3_DOF_ARM` | URDF, CSV, 4 meshes | differs, new output correct: the committed link inertia and meshes are stale. The exported mass and principal moments equal SolidWorks' own for each link; joint values equal the committed ones up to their 5-digit rounding and a gimbal-lock equivalent rpy |
| `TOY_BLOCK` (part)* | URDF, mesh | equivalent after the fixes; before them the products of inertia had wrong signs (issue 124) and an empty texture element was written (issue 126) |
| `BOX_RED`, `BOX_BLUE` (part, OBJ)* | URDF values, OBJ, MTL | equivalent (link and file names follow the package name typed in the form) |
| `TWO_BOXES_PER_CONFIG`, `CFG_RED_ONLY` | URDF, CSV, mesh | equivalent |
| `TWO_BOXES_PER_CONFIG`, `CFG_BOTH` | URDF, CSV, mesh | differs, new output correct: the committed centre of mass and inertia do not match its own mesh (y and z swapped); the exported values equal the tensor computed from the exported mesh |

After the first open every model has the `URDF Export Configuration` folder with one entry per SolidWorks
configuration, generated features (`URDF Reference`, `Origin_*`, `Axis_*`) at the root of the tree and no leftover
legacy attributes; no message box or exception dialog appeared.

## OBJ + MTL export regression (`scripts/regress_obj_export.py`)

One entry point for re-testing the OBJ + MTL export (see `OBJ_EXPORT.md`). Both modes print one `PASS`, `FAIL`,
`INFO` or `SKIP` line per check, then a summary table, and exit with a non-zero code when any check failed
(`INFO` lines are observations that do not fail the run; `SKIP` names a case that could not run). Both can write
the result as JSON (`--json <file>`) and as a Markdown table (`--markdown <file>`); the JSON holds
`mode`, `build`, `date`, `seconds`, `failed` and a list of `checks` (`case`, `check`, `status`, `detail`).

### Offline mode (no SolidWorks)

    python scripts/regress_obj_export.py --offline

Needs Python 3 and nothing else (standard library only). It finds the repository root from the script location, so
it can run from any directory and on a hosted CI runner (layer 2 of issue 123). It takes about a second. It reads
every committed package under `examples/` (every `urdf/*.urdf`) and checks:

* the URDF is well formed, joints connect existing links, there is one root link, every `package://` mesh path
  exists and the file is not empty;
* every OBJ parses, has vertices and faces, every face points at an existing vertex, the bounding box is finite
  and between 0.1 mm and 100 m; the OBJ files written equal the OBJ files the URDF references;
* the MTL file named by the OBJ exists, every `usemtl` has a material in it, `Kd` is within 0 to 1;
* the CSV has the same link names as the URDF, the URDF mass equals the CSV mass (1e-9 relative), the MTL `Kd`
  equals the CSV colour for links with one material, every mass is positive;
* the centre of mass lies inside the mesh box in the link frame (OBJ packages);
* where a model has an STL and an OBJ package (`3_DOF_ARM`), the links and the box and centre of each mesh in the
  link frame agree (0.2 mm plus 0.5 % of the size, because the two formats are tessellated separately).

`--examples <folder>` validates another folder with the same layout (this is how the checks themselves were tried
against deliberately broken copies).

### Live mode (your own SolidWorks)

    powershell -File scripts\test_build.ps1 -OutputDirectory <build folder>
    python scripts/regress_obj_export.py --live --dll <build folder> --launcher <launch_solidworks.py> ^
        --json result.json --markdown result.md

Requirements: Windows, SolidWorks 2023 or later with a licence, Python 3 with pywin32, the .NET Framework 4
`csc.exe` (it compiles `scripts/ObjRegressProbe.cs` against a copy of the build), and a build of `SW2URDF.dll` in
`<build folder>` (not registered). Expected run time: about 2.5 minutes (the first start of SolidWorks included).

* `--launcher` is the script that starts a new SolidWorks and writes `{"pid": ...}` to `--output` (it is called
  with `--launch --session-file <file> --output <file>`; `--launcher-python` names the Python that runs it).
  Alternatively `--pid <pid>` uses an instance you own that holds no documents; it is left running. The script
  never touches another instance.
* SolidWorks is hidden (`Visible = false`) for the whole run. Every message box of the exporter is recorded by
  the host program instead of being shown, the progress dialog is created off-screen, and no step needs a click.
  `--timeout` (default 1800 s) stops the run and kills what the script started; `--case-timeout` (default 300 s)
  limits one scenario.
* Everything happens in a scratch folder on copies of the repository examples and of the models the script builds
  (a textured box, a 3 mm box, a two-level assembly). Nothing in `examples/` is changed or saved. The scratch
  folder is deleted unless `--keep` is given; `--artifacts <folder>` copies the exported packages, each
  scenario's `probe.tsv` and the dialog screenshot out first. At the end only the instance the script started is
  closed.
* `--exe-hook <command>` runs a command on each program the script compiles before starting it (`{exe}` stands
  for the file), for machines that only run programs prepared in a particular way.
* `--build-commit <hash>` sets the commit written into the result (default: the checkout's `HEAD`).

Cases:

| Case | Content |
|------|---------|
| `TWO_BOXES assembly OBJ` | `TWO_BOXES_PER_CONFIG.SLDASM` (red and blue box), one link, two materials; also exported twice and with a cancelled run |
| `3_DOF_ARM assembly OBJ` | `3_DOF_ARM.SLDASM`, 4 links; also exported twice and with a cancelled run |
| `BOX_RED part OBJ`, `BOX_BLUE part OBJ` | the two parts, compared with the committed `*_description_obj` packages |
| `nested sub-assembly frame` | `SUB` (both boxes, coordinate system `Origin_sub` inside it) placed and turned by 90 degrees in `TOP`; one link whose frame is `Origin_sub <SUB-1>` |
| `very small model (3 mm box)` | part export of a 3 mm cube |
| `texture error path` | a box with an image texture appearance; exported into a fresh folder and into a folder that already holds an export |
| `OBJ progress dialog` | the real progress dialog at the process DPI (screenshot through `PrintWindow`, text measured against the controls, the Cancel button pressed) |

Checks per exported package (`PASS` or `FAIL` each):

* the URDF exists, its `package://` mesh paths exist and equal the files written (OBJ and MTL; the intermediate
  `.3dxml` is documented as left beside the OBJ, issue 130);
* vertex and face counts above 0, faces reference existing vertices, bounding box finite and plausible;
* every `usemtl` has a material in the MTL and `Kd` equals the SolidWorks colour (and the committed example MTL
  where there is one);
* OBJ volume and bounding box equal SolidWorks' own values (mass properties of the link's components, `GetBox` or
  `GetPartBox`) within 0.5 %;
* the URDF mass equals SolidWorks' mass; the centre of mass equals the one SolidWorks reports in the link frame
  and the centroid of the OBJ in the link frame;
* the mesh centre in the link frame equals the one of the STL export of the same link (0.2 mm plus 0.5 % of the
  size) and, for the nested case, the expected value;
* repeat: the second export into the same folder is the same (OBJ, MTL and the rest byte for byte, URDF and CSV
  equal to 1e-9 relative, because SolidWorks' mass properties carry noise near 1e-19);
* cancel: a cancelled export keeps the previous output byte for byte and leaves no temporary file;
* texture: no exception, a message in the SolidWorks language that names the link, no URDF, OBJ, MTL or CSV
  written into a fresh folder, the failure marker written, an existing OBJ, MTL and URDF kept byte for byte, and
  the converter's own message for the left-over 3DXML says that image textures are not supported.

How the host drives the export: it makes the same calls as the add-in (`ConfigurationSerialization`,
`ExportHelper`, `ObjConverter`, the progress dialog class). Two things the property page does before exporting have
to be done by a host that skips the page, and the host does them: it refreshes the parent references of the loaded
link tree and it generates the base frame `Origin_global` (and writes that name into the tree) before the robot
is built. A fresh tree and robot are built for every export, because an export rewrites the visual origins of the
robot it is given.

### What it does not cover

* Cancelling while a conversion is running: the models are small and the conversion finishes in milliseconds, so
  the live mode cancels with a token that is already cancelled (the previous output is kept) and checks that the
  dialog's Cancel button requests cancellation, but it does not click Cancel in the middle of a long conversion.
* The progress dialog is checked at the display scale of the machine it runs on (the result records it). The
  dialog uses a fixed pixel layout (issue 131); a run at 125 % or 150 % needs a machine with that setting. The
  clipping result is recorded as an observation and never fails the run.
* The property page and the export forms themselves (the example regression above drove them by hand);
  SolidWorks configurations other than the one saved active in the example; models with mirrored components; a very
  large model.
* Colour spaces and viewers (RViz, Gazebo): only the values in the MTL are compared.
* The hosted CI runner cannot run the live mode (no licence). Only `--offline` can.

### Result of a run

Master `48bb75f` (the private build was made from the next commit, which differs only in repository housekeeping
files), SolidWorks 2023 SP5, display scale 96 %, instance started by the script with the guarded launcher:
187 checks `PASS`, 0 `FAIL`, 13 `INFO`, run time 153 s. The offline mode ran 125 checks, all `PASS`.

| Case | Checks | Result |
|------|--------|--------|
| `TWO_BOXES assembly OBJ` | files and paths, counts, faces, MTL, volume and bounding box equal SolidWorks, mass, centre of mass (link frame and mesh centroid), colours red and blue, frame placement equals the STL export, repeat, cancel | 22 `PASS` |
| `3_DOF_ARM assembly OBJ` | the same for 4 links (volume within 0.4 % of SolidWorks because of tessellation, bounding box within 0.02 mm), bounding box and volume equal the committed `3_DOF_ARM_description_obj`, MTL colours, repeat, cancel | 69 `PASS` |
| `BOX_RED part OBJ` | the same for a part, committed example comparison | 22 `PASS` |
| `BOX_BLUE part OBJ` | the same for a part, committed example comparison | 22 `PASS` |
| `nested sub-assembly frame` | mesh in the link frame (centre 0.04375, 0, 0.0125 m, equal to the expected value and to the STL export), not empty, two materials, volume, box, mass, centre of mass | 20 `PASS` |
| `very small model (3 mm box)` | the same checks on a 27 mm3 part | 19 `PASS` |
| `texture error path` | clean failure, localized message naming the link, no half package, failure marker written, existing output kept, converter says textures are not supported | 8 `PASS` |
| `OBJ progress dialog` | dialog shown and captured, Cancel requests cancellation | 2 `PASS`; the text fits at 96 dpi (observation; issue 131 covers other scales) |
| setup | host program builds, instance started and hidden, extra models built | 3 `PASS` |

Observations (`INFO`, not failures):

* Every export shows the "Creating URDF Package ... at ..." message box of the exporter (the host records it).
* A cancelled assembly export shows the generic "Exporting the URDF failed unexpectedly" message, not one that
  says it was cancelled; the previous output is kept as required.
* The failed texture export tells the user that the mesh could not be exported and points to the log; the reason
  (image textures are not supported) is only in the converter's message and the log.
* A host that does not generate the base frame before building the robot gets the base link's inertia in the
  global frame while the mesh is in the link frame (the `TWO_BOXES` centre of mass then differs from SolidWorks'
  by a 90 degree turn). The add-in's property page generates the frame first, so the add-in itself is not
  affected. Any other caller that builds a robot without the page has to do the same.

## Known limitations

- Existing export tests (`TestExportHelper`) still write into the home folder and the example folders;
  the STL "file in use" failure described in `TestRunner/README.md` is not addressed here.
- The example was generated with SolidWorks 2023; opening it in older versions is not supported.
