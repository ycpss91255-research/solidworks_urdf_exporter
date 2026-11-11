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
| Settings of one SolidWorks configuration | one SolidWorks attribute per configuration, named exactly like the configuration (definition `URDF Export Configuration Data`) | written when you save while that configuration is active; read first |
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

## Known limitations

- Existing export tests (`TestExportHelper`) still write into the home folder and the example folders;
  the STL "file in use" failure described in `TestRunner/README.md` is not addressed here.
- The example was generated with SolidWorks 2023; opening it in older versions is not supported.
