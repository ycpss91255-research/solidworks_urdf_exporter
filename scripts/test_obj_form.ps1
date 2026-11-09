# Offline checks for the OBJ options on the export forms and for STLQualityFine
# serialization compatibility. Does not connect to SOLIDWORKS or register COM.
# Optional -ScreenshotDirectory saves PNGs of both forms (default and OBJ selected).
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$ScreenshotDirectory = '',
    # Optional Traditional Chinese resource file (Strings.zh-TW.resx from the i18n work).
    # Without it only the English layouts are checked.
    [string]$ZhResx = ''
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -File $PSCommandPath -AssemblyPath $AssemblyPath -ScreenshotDirectory $ScreenshotDirectory -ZhResx $ZhResx
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
using System.Collections.Generic;
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
        return Create<T>(null);
    }

    // A non-null font is applied before InitializeComponent, which is how a larger
    // system font (display scaling, CJK UI font) reaches AutoScaleMode.Font forms.
    static T Create<T>(Font font) where T : Form {
        T form = (T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(Form).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null).Invoke(form, null);
        if (font != null) form.Font = font;
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

    // ---- Layout checks -------------------------------------------------------------

    static Dictionary<string, string> LoadResx(string path) {
        Dictionary<string, string> map = new Dictionary<string, string>();
        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
        doc.Load(path);
        foreach (System.Xml.XmlNode node in doc.SelectNodes("/root/data")) {
            map[node.Attributes["name"].Value] = node.SelectSingleNode("value").InnerText;
        }
        // The OBJ notes are new in this branch and not in the i18n catalog yet.
        map["obj mesh density is determined by solidworks."] = "OBJ 7db2683c5bc65ea67531 SolidWorks 6c7a5b9a3002";
        map["obj color and texture are defined by the mtl file."] = "OBJ 7684984f827282078cbc57167531 MTL 6a9468486c7a5b9a3002";
        return map;
    }

    static void Translate(Control root, Dictionary<string, string> map) {
        foreach (Control control in root.Controls) {
            string key = control.Text == null ? null : control.Text.Trim().ToLowerInvariant();
            string value;
            if (!string.IsNullOrEmpty(key) && map.TryGetValue(key, out value)) control.Text = value;
            Translate(control, map);
        }
    }

    // Returns a description of every problem for the given control: outside its parent's
    // client area, or overlapping a visible sibling. Hidden controls are ignored.
    public static List<string> LayoutProblems(Control control) {
        List<string> problems = new List<string>();
        Control parent = control.Parent;
        if (parent == null || !Shown(control)) return problems;
        Rectangle client = parent.ClientRectangle;
        if (!client.Contains(control.Bounds)) {
            problems.Add(control.Name + " " + control.Bounds + " is outside " + parent.Name + " " + client);
        }
        foreach (Control sibling in parent.Controls) {
            if (sibling == control || !Shown(sibling)) continue;
            Rectangle overlap = Rectangle.Intersect(control.Bounds, sibling.Bounds);
            if (overlap.Width > 0 && overlap.Height > 0) {
                problems.Add(control.Name + " " + control.Bounds + " overlaps " + sibling.Name + " " + sibling.Bounds);
            }
        }
        return problems;
    }

    static void CheckLayout(Form form, string[] names, string context, string shots, string shotName) {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        List<string> problems = new List<string>();
        foreach (string name in names) {
            Control control = Find<Control>(form, name);
            problems.AddRange(LayoutProblems(control));
            // Children of a flow panel must stay inside it (otherwise they are clipped).
            if (control is FlowLayoutPanel) {
                foreach (Control child in control.Controls) problems.AddRange(LayoutProblems(child));
            }
        }
        Require(problems.Count == 0, context + " layout: " + string.Join("; ", problems.ToArray()));
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            if (!string.IsNullOrEmpty(shots)) bitmap.Save(Path.Combine(shots, shotName + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        form.Hide();
    }

    static readonly string[] PartObjLayout = {
        "flowLayoutPanelOptions", "objExport", "checkBox_rotate", "label1", "radioButton_fine",
        "radioButton_course", "labelObjQualityNote", "labelObjColorNote", "comboBox_materials" };
    static readonly string[] AssemblyObjLayout = {
        "flowLayoutPanelMeshFormat", "radioButtonStl", "radioButton3dxml", "radioButtonObj",
        "label10", "radioButtonFine", "radioButtonCourse", "labelObjQualityNote",
        "labelObjColorNote", "comboBoxMaterials" };

    // Self-check: the detector must actually report overlap and clipping.
    public static void LayoutDetectorWorks() {
        using (Panel panel = new Panel()) {
            panel.Size = new Size(100, 100);
            Label a = new Label { Name = "a", AutoSize = false, Bounds = new Rectangle(10, 10, 50, 20) };
            Label b = new Label { Name = "b", AutoSize = false, Bounds = new Rectangle(40, 15, 50, 20) };
            Label c = new Label { Name = "c", AutoSize = false, Bounds = new Rectangle(80, 80, 50, 50) };
            panel.Controls.AddRange(new Control[] { a, b, c });
            Require(LayoutProblems(a).Count == 1, "Detector reports overlap");
            Require(LayoutProblems(c).Count >= 1, "Detector reports clipping");
            b.Left = 70;
            Require(LayoutProblems(a).Count == 0, "Detector accepts a clean layout");
        }
    }

    public static void Layouts(string shots, string zhResx) {
        LayoutDetectorWorks();
        Dictionary<string, string> zh = string.IsNullOrEmpty(zhResx) ? null : LoadResx(zhResx);
        // Display scale is simulated through the font the AutoScaleMode.Font forms scale against.
        float[] scales = { 1.0f, 1.25f, 1.5f };
        string[] languages = zh == null ? new string[] { "en" } : new string[] { "en", "zh-TW" };
        foreach (string language in languages) {
            foreach (float scale in scales) {
                Font font = language == "en"
                    ? new Font("Microsoft Sans Serif", 8.25f * scale)
                    : new Font("Microsoft JhengHei UI", 9f * scale);
                string tag = language + "_" + (int)(scale * 100);
                string context = language + " at " + (int)(scale * 100) + "%";
                foreach (bool obj in new bool[] { false, true }) {
                    using (PartExportForm part = Create<PartExportForm>(font)) {
                        if (zh != null && language != "en") Translate(part, zh);
                        Find<CheckBox>(part, "objExport").Checked = obj;
                        CheckLayout(part, PartObjLayout, "Part " + context + (obj ? " OBJ" : ""), shots, "layout_part_" + tag + (obj ? "_obj" : ""));
                    }
                    using (AssemblyExportForm assembly = Create<AssemblyExportForm>(font)) {
                        if (zh != null && language != "en") Translate(assembly, zh);
                        foreach (Control page in assembly.Controls) page.Visible = page.Name == "panelLinkProperties";
                        Find<RadioButton>(assembly, "radioButtonObj").Checked = obj;
                        CheckLayout(assembly, AssemblyObjLayout, "Assembly " + context + (obj ? " OBJ" : ""), shots, "layout_assembly_" + tag + (obj ? "_obj" : ""));
                    }
                }
            }
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
[ObjFormCheck]::Layouts($ScreenshotDirectory, $ZhResx)
if (-not $ZhResx) { Write-Output 'NOTE: -ZhResx not given; Traditional Chinese layouts were not checked' }
[ObjFormCheck]::Serialization()
Write-Output 'PASS: OBJ quality controls follow the format selection; STLQualityFine serialization is compatible'
