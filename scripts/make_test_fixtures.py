"""Regenerates the minimal TWO_BOXES example model used by the SolidWorks tests.

Creates, in examples/TWO_BOXES (or --output):
  BOX_RED.SLDPRT   50 mm cube, red, one configuration "Default"
  BOX_BLUE.SLDPRT  25 mm square, blue, configurations "Short" (20 mm high) and "Tall" (60 mm high)
  TWO_BOXES.SLDASM both parts, configurations
      CFG_BOTH      both boxes present, the blue box uses its "Tall" configuration
      CFG_RED_ONLY  the blue box is suppressed

The script binds to ONE SolidWorks process that you name with --pid (through the Running Object Table entry
SolidWorks_PID_<pid>, never through a generic ProgID) and never starts or closes SolidWorks.
Existing files with these names in the output folder are replaced. Requires pywin32.

Usage:  python scripts/make_test_fixtures.py --pid <pid> [--output <folder>]
"""
import argparse
import os
import sys
import types

import pythoncom
import win32com.client
from win32com.client import VARIANT

# swUserPreferenceStringValue_e
SW_DEFAULT_TEMPLATE_PART = 8
SW_DEFAULT_TEMPLATE_ASSEMBLY = 9
# swSetValueInConfiguration_e
SW_SET_VALUE_IN_SPECIFIC_CONFIGURATIONS = 3
# swComponentSuppressionState_e
SW_COMPONENT_SUPPRESSED = 0
# swSaveAsOptions_e
SW_SAVE_AS_SILENT = 1

MM = 0.001
RED = (1.0, 0.0, 0.0)
BLUE = (0.0, 0.0, 1.0)

PART_RED = "BOX_RED"
PART_BLUE = "BOX_BLUE"
ASSEMBLY = "TWO_BOXES"
CONFIG_BOTH = "CFG_BOTH"
CONFIG_RED_ONLY = "CFG_RED_ONLY"
BLUE_SHORT = "Short"
BLUE_TALL = "Tall"


def prop(obj, name):
    """Late-bound COM: no-argument members may come back as a property value or as a bound method."""
    value = getattr(obj, name)
    return value() if isinstance(value, types.MethodType) else value


def null_dispatch():
    return VARIANT(pythoncom.VT_DISPATCH, None)


def bind(pid):
    rot = pythoncom.GetRunningObjectTable()
    context = pythoncom.CreateBindCtx(0)
    wanted = "SolidWorks_PID_%d" % pid
    for moniker in rot.EnumRunning():
        if moniker.GetDisplayName(context, None) != wanted:
            continue
        app = win32com.client.Dispatch(rot.GetObject(moniker).QueryInterface(pythoncom.IID_IDispatch))
        actual = prop(app, "GetProcessID")
        if actual != pid:
            raise RuntimeError("%s reports process id %s, refusing to use it" % (wanted, actual))
        return app
    raise RuntimeError("%s is not in the Running Object Table; start that instance first" % wanted)


def new_document(app, template_pref):
    template = app.GetUserPreferenceStringValue(template_pref)
    if not template or not os.path.isfile(template):
        raise RuntimeError("No default template configured (preference %d): %r" % (template_pref, template))
    model = app.NewDocument(template, 0, 0, 0)
    if model is None:
        raise RuntimeError("NewDocument failed for " + template)
    return model


def save(model, path):
    if os.path.exists(path):
        os.remove(path)
    errors = model.SaveAs3(path, 0, SW_SAVE_AS_SILENT)
    if errors != 0 or not os.path.isfile(path):
        raise RuntimeError("Saving %s failed (error %s)" % (path, errors))


def rename_active_configuration(model, name):
    configuration = prop(prop(model, "ConfigurationManager"), "ActiveConfiguration")
    configuration.Name = name


def first_reference_plane(model):
    """The first reference plane in the tree is the front plane, whatever the UI language."""
    feature = model.FirstFeature
    while feature is not None:
        if feature.GetTypeName2 == "RefPlane":
            return feature
        feature = feature.GetNextFeature
    raise RuntimeError("No reference plane found")


def make_box_part(app, side, height, color, configuration_heights):
    """Square `side` x `side`, extruded `height` (all metres), coloured. Returns the model."""
    model = new_document(app, SW_DEFAULT_TEMPLATE_PART)
    plane = first_reference_plane(model)
    model.ClearSelection2(True)
    plane.Select2(False, 0)
    sketch_manager = model.SketchManager
    sketch_manager.InsertSketch(True)
    sketch_manager.CreateCenterRectangle(0, 0, 0, side / 2, side / 2, 0)
    sketch_manager.InsertSketch(True)
    feature = model.FeatureManager.FeatureExtrusion3(
        True, False, False, 0, 0, height, 0.0, False, False, False, False, 0.0, 0.0,
        False, False, False, False, True, True, True, 0, 0.0, False)
    if feature is None:
        raise RuntimeError("Extrusion failed")
    # r, g, b, ambient, diffuse, specular, shininess, transparency, emission
    values = (color[0], color[1], color[2], 1.0, 1.0, 0.0, 0.0, 0.0, 0.0)
    model.MaterialPropertyValues = VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, values)
    stored = tuple(model.MaterialPropertyValues)
    if any(abs(a - b) > 1e-6 for a, b in zip(stored[:3], color)):
        raise RuntimeError("Colour was not applied: %r" % (stored,))
    if configuration_heights:
        dimension_name = "D1@" + feature.Name
        names = list(configuration_heights)
        rename_active_configuration(model, names[0])
        for name in names[1:]:
            if model.AddConfiguration3(name, "", "", 0) is None:
                raise RuntimeError("Could not add configuration " + name)
        for name, value in configuration_heights.items():
            model.ShowConfiguration2(name)
            dimension = model.Parameter(dimension_name)
            ok = dimension.SetSystemValue3(
                value, SW_SET_VALUE_IN_SPECIFIC_CONFIGURATIONS, VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_BSTR, [name]))
            if ok not in (0, None):
                raise RuntimeError("Setting %s in %s failed (%s)" % (dimension_name, name, ok))
        model.ShowConfiguration2(names[0])
    else:
        rename_active_configuration(model, "Default")
    model.ForceRebuild3(False)
    return model


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pid", type=int, required=True, help="process id of the SolidWorks instance to use")
    default_output = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "examples", ASSEMBLY)
    parser.add_argument("--output", default=default_output, help="output folder (default: examples/TWO_BOXES)")
    args = parser.parse_args()

    output = os.path.abspath(args.output)
    os.makedirs(output, exist_ok=True)
    app = bind(args.pid)
    red_path = os.path.join(output, PART_RED + ".SLDPRT")
    blue_path = os.path.join(output, PART_BLUE + ".SLDPRT")
    assembly_path = os.path.join(output, ASSEMBLY + ".SLDASM")
    titles = []
    try:
        red = make_box_part(app, 50 * MM, 50 * MM, RED, None)
        save(red, red_path)
        titles.append(red.GetTitle)
        blue = make_box_part(app, 25 * MM, 20 * MM, BLUE, {BLUE_SHORT: 20 * MM, BLUE_TALL: 60 * MM})
        save(blue, blue_path)
        titles.append(blue.GetTitle)

        assembly = new_document(app, SW_DEFAULT_TEMPLATE_ASSEMBLY)
        assembly_manager = assembly
        red_component = assembly_manager.AddComponent5(red_path, 0, "", False, "", 0.0, 0.0, 0.0)
        blue_component = assembly_manager.AddComponent5(blue_path, 0, "", False, "", 100 * MM, 0.0, 0.0)
        if red_component is None or blue_component is None:
            raise RuntimeError("Could not add the components")
        rename_active_configuration(assembly, CONFIG_BOTH)
        blue_component.ReferencedConfiguration = BLUE_TALL
        if assembly.AddConfiguration3(CONFIG_RED_ONLY, "", "", 0) is None:
            raise RuntimeError("Could not add configuration " + CONFIG_RED_ONLY)
        assembly.ShowConfiguration2(CONFIG_RED_ONLY)
        blue_name = blue_component.Name2
        for component in assembly.GetComponents(True):
            if component.Name2 == blue_name:
                component.SetSuppression2(SW_COMPONENT_SUPPRESSED)
        assembly.ShowConfiguration2(CONFIG_BOTH)
        assembly.ForceRebuild3(False)
        save(assembly, assembly_path)
        titles.append(assembly.GetTitle)
    finally:
        for title in reversed(titles):
            app.CloseDoc(title)
    print("Wrote %s, %s and %s" % (red_path, blue_path, assembly_path))


if __name__ == "__main__":
    sys.exit(main())
