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

## Known limitations

- Existing export tests (`TestExportHelper`) still write into the home folder and the example folders;
  the STL "file in use" failure described in `TestRunner/README.md` is not addressed here.
- The example was generated with SolidWorks 2023; opening it in older versions is not supported.
