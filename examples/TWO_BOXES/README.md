# TWO_BOXES: one URDF configuration per SolidWorks configuration

`TWO_BOXES.SLDASM` (with `BOX_RED.SLDPRT` and `BOX_BLUE.SLDPRT`) is the minimal model used by the tests, see
[TESTING.md](../../TESTING.md). **`TWO_BOXES_PER_CONFIG.SLDASM`** is a copy of that assembly (same parts, same
SolidWorks configurations) that additionally has a **different saved URDF link tree for each configuration**.
The original `TWO_BOXES.SLDASM` is unchanged and has no saved link tree.

## What is saved in the model

In the feature tree, the folder `URDF Export Configuration` holds one attribute per SolidWorks configuration,
named like the configuration:

```
URDF Export Configuration
├── CFG_BOTH        link tree: base_link = BOX_RED-1 + BOX_BLUE-1
└── CFG_RED_ONLY    link tree: base_link = BOX_RED-1
```

Each tree has a single `base_link` whose coordinate system is *Automatically Generate*. Open the assembly,
activate a configuration and start the export: the link tree of that configuration is loaded. A configuration
without its own attribute falls back to the shared attribute of older files.

## Results

Exported with STL meshes, one package per configuration, in this folder:

| Package | Links | Meshes (`base_link.STL`) | Mass of `base_link` |
|---|---|---|---|
| `TWO_BOXES_PER_CONFIG_description_CFG_BOTH` | 1 | both boxes, 24 triangles | 0.1625 kg |
| `TWO_BOXES_PER_CONFIG_description_CFG_RED_ONLY` | 1 | red box only, 12 triangles | 0.125 kg |

The URDF header comment has its version information replaced by `n/a`, and `export.log` is left out.

## Regenerating

`scripts/make_config_example.ps1` rebuilds everything from the files in this folder (on scratch copies; the
existing examples are not modified) with the exporter code of a build you give it:

```powershell
.\scripts\make_config_example.ps1 -SolidWorksPid <pid> -BuildDirectory <folder with SW2URDF.dll>
```

It connects to the one SolidWorks instance with that process id and never starts or closes SolidWorks. On
machines that only run signed code, add `-SigningThumbprint <certificate thumbprint>`: the script then signs the
helper it compiles and requires `SW2URDF.dll` and the helper to be validly signed with that certificate before
anything runs. The script itself has to be signed as well if your execution policy requires it.
