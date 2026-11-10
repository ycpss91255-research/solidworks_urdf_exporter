# Testing SW2URDF

This project programmatically runs the tests of the SW2URDF project.
The tests rely on the models provided in the examples directory, so any changes to those files may cause these tests to fail.
Update any tests to reflect corresponding changes in the models.

## To Build

The build stamps the git revision into the DLL (`git describe --dirty`). A build from a working tree with
uncommitted changes ends in `-dirty`; `TestVersioning` accepts that, so it no longer fails during development.
Release builds should still come from a clean commit: check that the DLL's product version has no `-dirty` suffix.

## Tests that do not need SolidWorks

Classes in `SW2URDF.Test` without the SolidWorks fixture (for example `TestMissingComponentPolicy`,
`TestConfigurationFormat`, `TestOrphanAttributes`, `TestVersioning`) can be run without SolidWorks, by reflection
from a built DLL:

    powershell -File scripts\test_logic.ps1 -AssemblyPath <path to SW2URDF.dll> [-Filter <class name part>]

## Known environment issue: STL export tests

The export tests (`TestExportHelper`, STL and 3DXML cases) write meshes into the example model folders
(`examples\<model>\<model>_description\meshes`) and the test fixture starts or attaches to SolidWorks through the
generic `SldWorks.Application` ProgID. A failure such as "file in use" for an STL file is most likely caused by a
SolidWorks instance (or another run of the tests) that still holds the previously exported mesh open. This could not
be reproduced in the setup used for the per-configuration work (the fixture cannot be used there), so it is not
fixed. To avoid it: run the tests against a single SolidWorks instance, close all documents and any other test run
first, and delete the generated `meshes` files before rerunning.

When you build the solution, you should see two successful builds, SW2URDF and TestRunner.

## To Run

Run the TestRunner executable, it will locate the SW2URDF Dll automatically.

    TestRunner\bin\Debug\net48>TestRunner.exe

If you only want to run a subset of tests, the first argument of TestRunner.exe is an optional filter parameter.
Any test with a fully qualified NameSpace.ClassName.FunctionName that contains the provided string will be run.
For example, to run just the versioning tests.

    TestRunner\bin\Debug\net48>TestRunner.exe TestVersioning
