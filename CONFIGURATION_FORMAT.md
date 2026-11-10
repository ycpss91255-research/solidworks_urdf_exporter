# URDF export configuration data format

The exporter stores its link/joint tree in SolidWorks attributes inside the model.
This document describes how that data is versioned.

## Where the data lives

| Location | Attribute / folder name | Notes |
|---|---|---|
| One per SolidWorks configuration | instance named after the configuration (definition `URDF Export Configuration Data`), grouped in the feature tree folder `URDF Export Configuration` | Preferred when it exists and is not empty. |
| Shared by all configurations | `URDF Export Configuration (v1.4)` | Fallback. Written by exporters before per-configuration storage. |
| Older shared names | `URDF Export Configuration (v1.3)`, `URDF Export Configuration` | Read-only fallback. |

The shared attribute name contains a version for historical reasons. It is **not renamed**:
models saved by earlier exporters keep their data under that name, and renaming would hide it.
The feature tree folder name carries no version.

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
