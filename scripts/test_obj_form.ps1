# Offline checks for the OBJ options on the export forms and for STLQualityFine
# serialization compatibility. Does not connect to SOLIDWORKS or register COM.
# Optional -ScreenshotDirectory saves PNGs of both forms (default and OBJ selected).
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$ScreenshotDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -File $PSCommandPath -AssemblyPath $AssemblyPath -ScreenshotDirectory $ScreenshotDirectory
    exit $LASTEXITCODE
}
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.Serialization
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
[void][Reflection.Assembly]::LoadFrom($AssemblyPath)
$code = @'
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using SW2URDF.UI;
using SW2URDF.URDF;

public static class ObjFormCheck {
    static void Require(bool condition, string message) {
        if (!condition) throw new Exception(message);
    }

    // The forms need SOLIDWORKS to construct normally. Skip the constructor and run
    // only the designer-generated InitializeComponent so event wiring is exercised.
    static T Create<T>() where T : Form {
        T form = (T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(Form).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null).Invoke(form, null);
        typeof(T).GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        EventHandlerList events = (EventHandlerList)typeof(Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form, null);
        object loadKey = typeof(Form).GetField("EVENT_LOAD", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        events.RemoveHandler(loadKey, events[loadKey]);
        return form;
    }

    // Control.Visible is false while the form itself is not shown; read the control's own flag.
    static bool Shown(Control control) {
        MethodInfo getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
        return (bool)getState.Invoke(control, new object[] { 2 });
    }

    static T Find<T>(Control root, string name) where T : Control {
        Control[] found = root.Controls.Find(name, true);
        Require(found.Length == 1, "Control not found or duplicated: " + name);
        return (T)found[0];
    }

    static void Shot(Form form, string directory, string name) {
        if (string.IsNullOrEmpty(directory)) return;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    // Controls that only matter for the URDF <material> element. OBJ exports take
    // color and texture from the MTL file, so these must be inert while OBJ is selected.
    static readonly string[] PartColorControls = {
        "label28", "label29", "label30", "label31", "label32", "label33", "label34",
        "comboBox_materials", "domainUpDown_red", "domainUpDown_green", "domainUpDown_blue",
        "domainUpDown_alpha", "textBox_texture", "button_texturebrowse" };
    static readonly string[] AssemblyColorControls = {
        "label28", "label29", "label30", "label31", "label32", "label33", "label34",
        "comboBoxMaterials", "domainUpDownRed", "domainUpDownGreen", "domainUpDownBlue",
        "domainUpDownAlpha", "textBoxTexture", "buttonTextureBrowse" };

    static void RequireColor(Control root, string[] names, bool enabled, string context) {
        Control note = Find<Control>(root, "labelObjColorNote");
        Require(Shown(note) == !enabled, context + ": color note visibility");
        foreach (string name in names) {
            Require(Find<Control>(root, name).Enabled == enabled, context + ": " + name + " enabled must be " + enabled);
        }
    }

    public static void Forms(string shots) {
        using (PartExportForm part = Create<PartExportForm>()) {
            CheckBox obj = Find<CheckBox>(part, "objExport");
            Control fine = Find<Control>(part, "radioButton_fine");
            Control coarse = Find<Control>(part, "radioButton_course");
            Control note = Find<Control>(part, "labelObjQualityNote");
            Require(obj.Parent is FlowLayoutPanel, "objExport must live in a FlowLayoutPanel");
            Require(Find<CheckBox>(part, "checkBox_rotate").Parent == obj.Parent, "rotate and OBJ share one panel");
            Require(fine.Enabled && coarse.Enabled && !Shown(note), "Part: default quality enabled");
            RequireColor(part, PartColorControls, true, "Part default");
            Shot(part, shots, "part_default");
            obj.Checked = true;
            Require(!fine.Enabled && !coarse.Enabled && Shown(note), "Part: OBJ disables quality");
            RequireColor(part, PartColorControls, false, "Part OBJ");
            Shot(part, shots, "part_obj");
            obj.Checked = false;
            Require(fine.Enabled && coarse.Enabled && !Shown(note), "Part: STL re-enables quality");
            RequireColor(part, PartColorControls, true, "Part back to STL");
        }
        using (AssemblyExportForm assembly = Create<AssemblyExportForm>()) {
            // The link page is hidden behind the joint page until it is completed; show only it.
            foreach (Control page in assembly.Controls) page.Visible = page.Name == "panelLinkProperties";
            RadioButton objRadio = Find<RadioButton>(assembly, "radioButtonObj");
            RadioButton stl = Find<RadioButton>(assembly, "radioButtonStl");
            RadioButton xml = Find<RadioButton>(assembly, "radioButton3dxml");
            Control fine = Find<Control>(assembly, "radioButtonFine");
            Control coarse = Find<Control>(assembly, "radioButtonCourse");
            Control note = Find<Control>(assembly, "labelObjQualityNote");
            Require(objRadio.Parent is FlowLayoutPanel && stl.Parent == objRadio.Parent && xml.Parent == objRadio.Parent,
                "Mesh format radios must share a FlowLayoutPanel");
            Require(fine.Enabled && coarse.Enabled && !Shown(note), "Assembly: default quality enabled");
            RequireColor(assembly, AssemblyColorControls, true, "Assembly default");
            Shot(assembly, shots, "assembly_default");
            objRadio.Checked = true;
            Require(!fine.Enabled && !coarse.Enabled && Shown(note), "Assembly: OBJ disables quality");
            RequireColor(assembly, AssemblyColorControls, false, "Assembly OBJ");
            Shot(assembly, shots, "assembly_obj");
            xml.Checked = true;
            Require(fine.Enabled && coarse.Enabled && !Shown(note), "Assembly: 3dxml re-enables quality");
            RequireColor(assembly, AssemblyColorControls, true, "Assembly 3dxml");
            objRadio.Checked = true;
            stl.Checked = true;
            Require(fine.Enabled && coarse.Enabled && !Shown(note), "Assembly: STL re-enables quality");
            RequireColor(assembly, AssemblyColorControls, true, "Assembly STL");
        }
    }

    static string Serialize(Link link) {
        DataContractSerializer serializer = new DataContractSerializer(typeof(Link));
        using (MemoryStream stream = new MemoryStream()) {
            serializer.WriteObject(stream, link);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    static Link Deserialize(string xml) {
        DataContractSerializer serializer = new DataContractSerializer(typeof(Link));
        using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) {
            return (Link)serializer.ReadObject(stream);
        }
    }

    public static void Serialization() {
        Link link = (Link)FormatterServices.GetUninitializedObject(typeof(Link));
        link.STLQualityFine = true;
        string xml = Serialize(link);
        Require(xml.Contains("STLQualityFine>true<"), "STLQualityFine is serialized: " + xml);
        Require(Deserialize(xml).STLQualityFine, "STLQualityFine round-trips");

        // Data written before or after the field existed must still load.
        string without = System.Text.RegularExpressions.Regex.Replace(xml, "<(\\w+:)?STLQualityFine>[^<]*</(\\w+:)?STLQualityFine>", "");
        Require(without != xml, "Field removed from sample");
        Require(!Deserialize(without).STLQualityFine, "Missing STLQualityFine loads as false");

        // A future version that drops the field, or any unknown member, must be ignored.
        string unknown = xml.Replace("<isIncomplete", "<SomeRemovedSetting>1</SomeRemovedSetting><isIncomplete");
        Require(unknown != xml, "Unknown member injected");
        Require(Deserialize(unknown).STLQualityFine, "Unknown members are ignored");
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies @($AssemblyPath, 'System.Windows.Forms', 'System.Drawing', 'System.Runtime.Serialization', 'System.Xml')
if ($ScreenshotDirectory) { New-Item -ItemType Directory -Force -Path $ScreenshotDirectory | Out-Null }
[ObjFormCheck]::Forms($ScreenshotDirectory)
[ObjFormCheck]::Serialization()
Write-Output 'PASS: OBJ quality controls follow the format selection; STLQualityFine serialization is compatible'
