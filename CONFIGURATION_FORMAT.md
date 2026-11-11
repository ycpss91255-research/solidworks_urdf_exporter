# URDF export configuration data format

The exporter stores its link/joint tree in SolidWorks attributes inside the model.
This document describes how that data is versioned.

## Where the data lives

| Location | Attribute / folder name | Notes |
|---|---|---|
| One per SolidWorks configuration | instance named after the configuration (definition `URDF Export Configuration Data`), grouped in the feature tree folder `URDF Export Configuration` | Preferred when it exists and is not empty. |
| Shared by all configurations | `URDF Export Configuration (v1.4)` | Fallback. Written by exporters before per-configuration storage. |
| Older shared names | `URDF Export Configuration (v1.3)`, `URDF Export Configuration`, `URDF Export Configuration (legacy)` | Read-only fallback. The plain name is renamed to `(legacy)` the first time the exporter groups the feature tree, because the feature tree folder uses that name and feature names are unique. The data is kept. |

The shared attribute name contains a version for historical reasons. It is **not renamed**:
models saved by earlier exporters keep their data under that name, and renaming would hide it.
The feature tree folder name carries no version.

## Generated coordinate systems and axes

The 3D sketch `URDF Reference`, `Origin_global`, `Origin_<joint>` and `Axis_<joint>` are model features,
not part of a configuration: there is one set per model. They are points and lines at fixed assembly
coordinates, so they do not follow the components. They stay loose at the feature tree root, where SolidWorks creates them: the folder `URDF Export Configuration` holds only the configuration entries (and, until they are removed, the old shared attributes). Models grouped by an earlier build are repaired when the exporter opens: exporter-generated features inside the folder move back to the root, features of the user are never touched. The settings of a configuration store only their
*names* (after an export, the generated name replaces `Automatically Generate`).

- Every export checks a saved coordinate system or axis that the exporter generated (it hangs on the
  `URDF Reference` sketch) against the geometry of the **active** SolidWorks configuration. Only its position is compared (orientation conventions differ between exporter versions and describe the same robot). When it is
  at another position (another configuration, or the model was edited since) the exporter uses the generated
  frame that is already at the right place, or makes a new numbered one (`Origin_<joint>2`), and saves that
  name for the configuration. Old frames are left alone; switching back and forth creates nothing new.
  Frames the user made are never replaced.
- A configuration with "Suppress new features" ticked gets frames made in other configurations suppressed;
  the exporter unsuppresses the `URDF Reference` sketch in the active configuration so it can add to it.
- A saved coordinate system that no longer exists (for example the sketch was deleted, which removes its
  frames) is generated again, for the base link as for the joints.

## Format version

- The version is the `exporterVersion` parameter stored **inside** each attribute. It
  versions the *data format* only. It is independent of the exporter release number
  (a release does not change it, and a format change does not need a matching release number).
- Current format version: **1.4** (`ConfigurationFormat.CurrentVersion`).
- Versions below 1.3 use the old XML serializer scheme; 1.3 and above use DataContract
  serialization.
- Per-configuration storage changes only *where* data is stored, not its format, so it stays 1.4.

## When to raise the version

Raise `ConfigurationFormat.CurrentVersion` only when the serialized content changes in a way an
older exporter would misread (for example renaming or removing a `[DataMember]`, or changing its
meaning). Adding an optional member that older exporters can ignore does not require a raise.
When raising it:

1. Keep reading every earlier version (add a branch in `ConfigurationFormat.Classify` and the
   loader; never drop a reader).
2. Add a test that loads data of the old version and checks the content is unchanged after
   the upgrade (see `TestConfigurationFormat`).
3. Do not create a new shared attribute name. New data goes to the per-configuration attribute.

## Upgrade path

- Loading data with a lower version than the current one works as before.
- Saving writes the current version to the active configuration's attribute. The old data is
  left in place (older exporters can still read it) and can be deleted by the user.
- Data with a version **higher** than the exporter supports is refused with a message asking
  the user to update the exporter; it is never overwritten.

## Tests

`scripts/test_logic.ps1 -AssemblyPath <built SW2URDF.dll>` runs the SolidWorks-free tests,
including `TestConfigurationFormat` and `TestMissingComponentPolicy`.
