// Live host for scripts/regress_obj_export.py. It runs the real export code of a private SW2URDF.dll
// (ExportHelper, the OBJ converter and its progress dialog) against ONE SolidWorks process that is
// named by its Running Object Table entry (SolidWorks_PID_<pid>), and writes what it did and the
// SolidWorks reference values to <out>\probe.tsv (key<TAB>value, one per line) for the script to check.
// It only opens and closes the scratch copies it is given, never saves them, and never changes the
// visibility of SolidWorks. Plain C# 5 so that the .NET Framework csc.exe can compile it.
//
// Usage:
//   ObjRegressProbe.exe assembly <pid> <assembly file> <out dir> <package>   exports OBJ and STL twice, then a cancelled run
//   ObjRegressProbe.exe part     <pid> <part file> <out dir> <package>       exports OBJ and STL
//   ObjRegressProbe.exe nested   <pid> <top assembly> <out dir> <package>    one link whose frame is in sub-assembly SUB-1
//   ObjRegressProbe.exe texture  <pid> <part file> <out dir> <package>       a model with an image texture, expected to fail
//   ObjRegressProbe.exe dialog   <pid|0> <out dir>                           the real progress dialog: screenshot, metrics, Cancel
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;

public static class ObjRegressProbe
{
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx context);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    static readonly List<string> rows = new List<string>();
    static readonly List<string> messages = new List<string>();
    static SldWorks app;
    static string outDir;
    static IMathUtility math;

    static void Put(string key, object value)
    {
        string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        rows.Add(key + "\t" + text.Replace("\r", " ").Replace("\n", " "));
    }

    static string Num(double v) { return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

    static SldWorks Bind(int pid)
    {
        IRunningObjectTable rot; IBindCtx context;
        GetRunningObjectTable(0, out rot); CreateBindCtx(0, out context);
        IEnumMoniker iterator; rot.EnumRunning(out iterator);
        var next = new IMoniker[1];
        while (iterator.Next(1, next, IntPtr.Zero) == 0)
        {
            string name; next[0].GetDisplayName(context, null, out name);
            if (name != "SolidWorks_PID_" + pid) continue;
            object value; rot.GetObject(next[0], out value);
            var found = (SldWorks)value;
            if (found.GetProcessID() != pid) throw new Exception("process id mismatch");
            return found;
        }
        throw new Exception("SolidWorks_PID_" + pid + " is not in the Running Object Table");
    }

    // Every message box of the exporter is recorded instead of shown, so nothing waits for a click.
    sealed class RecordingMessages : IMessageBox
    {
        public System.Windows.MessageBoxResult Show(string message) { messages.Add(message); return System.Windows.MessageBoxResult.OK; }
        public System.Windows.MessageBoxResult Show(string message, string caption, System.Windows.MessageBoxButton buttons) { return Show(message); }
    }

    static string Sha(string file)
    {
        if (!File.Exists(file)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "");
    }

    // Opens a scratch copy and points its references at the sibling copies in the same folder.
    static ModelDoc2 Open(string file, bool part)
    {
        string folder = Path.GetDirectoryName(file);
        foreach (ModelDoc2 open in (object[])(app.GetDocuments() ?? new object[0]))
            if (!open.GetPathName().StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Another document is open in the owned session: " + open.GetPathName());
        if (!part)
        {
            var dependencies = (string[])app.GetDocumentDependencies2(file, false, false, false);
            if (dependencies != null) for (int i = 1; i < dependencies.Length; i += 2)
            {
                string local = Path.Combine(folder, Path.GetFileName(dependencies[i]));
                if (!File.Exists(local)) throw new Exception("Missing copied dependency " + local);
                if (!String.Equals(dependencies[i], local, StringComparison.OrdinalIgnoreCase) &&
                    !app.ReplaceReferencedDocument(file, dependencies[i], local))
                    throw new Exception("Could not localize reference " + dependencies[i]);
            }
        }
        int error = 0, warning = 0;
        ModelDoc2 model = app.OpenDoc6(file, part ? 1 : 2, 1, "", ref error, ref warning);
        if (model == null || error != 0) throw new Exception("Open failed " + error + "/" + warning + ": " + file);
        int activate = 0; app.ActivateDoc3(model.GetTitle(), false, 0, ref activate);
        return model;
    }

    static void CollectLinks(Link link, List<Link> links)
    {
        links.Add(link);
        foreach (Link child in link.Children) CollectLinks(child, links);
    }

    // SW2URDF.dll embeds the SolidWorks interop types, so a generic member such as List<Component2> cannot be
    // used from here at compile time; it is reached through reflection and the non-generic interfaces.
    static System.Collections.IList ComponentList(Link link)
    {
        return (System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(link);
    }

    static Component2[] Components(Link link)
    {
        return ComponentList(link).Cast<object>().Select(o => (Component2)o).ToArray();
    }

    static double[] Array3(object value) { return ((double[])value).Take(3).ToArray(); }

    // The frame of a link in global coordinates. A coordinate system inside a sub-assembly is named
    // "<name> <component>"; its frame is the component transform composed with the frame in the sub-assembly.
    static MathTransform Frame(ModelDoc2 model, string name)
    {
        int open = name.IndexOf('<');
        if (open < 0) return model.Extension.GetCoordinateSystemTransformByName(name);
        string geo = name.Substring(0, open).Trim();
        string component = name.Substring(open + 1, name.IndexOf('>', open) - open - 1);
        Component2 c = ((AssemblyDoc)model).GetComponentByName(component);
        if (c == null) throw new Exception("No component " + component);
        ModelDoc2 inner = (ModelDoc2)c.GetModelDoc2();
        MathTransform local = (MathTransform)inner.Extension.GetCoordinateSystemTransformByName(geo);
        // IMathTransform.Multiply: the transform that applies the argument first, then this one
        return (MathTransform)local.Multiply(c.Transform2);
    }

    // SolidWorks' own numbers for one link: mass properties of its components (SI units) with the centre of mass
    // evaluated by SolidWorks in the link frame, and the global bounding box of the components.
    static void WriteLinkReference(string prefix, Link link, ModelDoc2 model, string frameName)
    {
        MathTransform frame = Frame(model, frameName);
        IMassProperty2 mp = (IMassProperty2)model.Extension.CreateMassProperty2();
        mp.UseSystemUnits = true;
        mp.IncludeHiddenBodiesOrComponents = true;
        if (!mp.SetCoordinateSystem(frame)) throw new Exception("Could not set the link frame for " + link.Name);
        mp.SelectedItems = Components(link);
        if (!mp.Recalculate()) throw new Exception("Mass properties failed for " + link.Name);
        double[] com = Array3(mp.CenterOfMass);
        double[] box = null;
        foreach (Component2 c in Components(link))
        {
            double[] b = (double[])c.GetBox(false, false);
            if (box == null) box = (double[])b.Clone();
            else for (int i = 0; i < 3; i++) { box[i] = Math.Min(box[i], b[i]); box[i + 3] = Math.Max(box[i + 3], b[i + 3]); }
        }
        Put(prefix + ".sw.mass", Num(mp.Mass));
        Put(prefix + ".sw.volume", Num(mp.Volume));
        Put(prefix + ".sw.com_link", Num(com[0]) + " " + Num(com[1]) + " " + Num(com[2]));
        Put(prefix + ".sw.box_global", string.Join(" ", box.Select(Num).ToArray()));
        Put(prefix + ".sw.frame", frameName);
    }

    // The add-in's property page generates the base frame (Origin_global) when it opens a model, before anything is
    // exported; a host that skips the page has to do the same, or the base link's inertia is computed against the
    // global frame and only afterwards the frame is created. Done through the same private method the page uses.
    static void EnsureBaseOrigin(ExportHelper helper)
    {
        MethodInfo create = typeof(ExportHelper).GetMethod("CreateBaseRefOrigin", BindingFlags.NonPublic | BindingFlags.Instance);
        if (create == null) { Put("base_origin", "CreateBaseRefOrigin not found; frame not pre-generated"); return; }
        create.Invoke(helper, new object[] { true });
        helper.UpdateReferenceGeometries();
        Put("base_origin", "generated before the export");
    }

    // A fresh tree and robot for every export, as the add-in builds them for every run.
    static ExportHelper BuildAssemblyHelper(ModelDoc2 model, out LinkNode tree)
    {
        bool loadError; tree = ConfigurationSerialization.LoadBaseNodeFromModel(model, out loadError);
        if (loadError || tree == null) throw new Exception("No saved URDF tree");
        // The loaded links point at their parents through separate objects; the add-in's tree editing refreshes this
        tree.UpdateLinkTree(null);
        var missing = new List<string>(); CommonSwOperations.LoadSWComponents(model, tree, missing);
        if (missing.Count > 0) throw new Exception("Missing saved references: " + string.Join(",", missing.ToArray()));
        var helper = new ExportHelper(app);
        EnsureBaseOrigin(helper);
        // The page also writes the generated name into the tree before the export
        string saved = tree.Link.Joint.CoordinateSystemName;
        Put("base_frame_saved", saved);
        if (saved == "Automatically Generate") tree.Link.Joint.CoordinateSystemName = "Origin_global";
        if (!helper.CreateRobotFromTreeView(tree)) throw new Exception("Robot build failed");
        return helper;
    }

    static void ExportRobot(ExportHelper helper, string root, string package, MeshExportFormat format, CancellationToken token)
    {
        helper.SavePath = root; helper.PackageName = package; helper.CancellationToken = token;
        helper.ExportRobot(true, format);
    }

    static string Hashes(string package)
    {
        var parts = new List<string>();
        foreach (string file in Directory.GetFiles(package, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = file.Substring(package.Length + 1);
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".3dxml", StringComparison.OrdinalIgnoreCase)) continue;
            parts.Add(name + "=" + Sha(file));
        }
        return string.Join(";", parts.ToArray());
    }

    static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, file.Substring(from.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }

    static void LeftOver(string key, string package)
    {
        var temp = new List<string>();
        if (Directory.Exists(package))
            foreach (string file in Directory.GetFiles(package, "*", SearchOption.AllDirectories))
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).StartsWith("~") || file.IndexOf(".tmp", StringComparison.OrdinalIgnoreCase) >= 0)
                    temp.Add(file.Substring(package.Length + 1));
        Put(key, temp.Count == 0 ? "none" : string.Join(",", temp.ToArray()));
    }

    static void DumpTree(LinkNode node, string key)
    {
        Put(key, node.Link.Name + " components=" + (ComponentList(node.Link) == null ? "NULL" : ComponentList(node.Link).Count.ToString()) + " parent=" + (node.Link.Parent == null ? "none" : node.Link.Parent.Name) +
            " parentComponents=" + (node.Link.Parent == null ? "-" : (typeof(Link).GetField("SWComponents").GetValue(node.Link.Parent) == null ? "NULL" : "ok")));
        int i = 0;
        foreach (LinkNode child in node.Nodes) DumpTree(child, key + "." + (i++));
    }

    static void Assembly(string file, string package, bool repeat)
    {
        ModelDoc2 model = Open(file, false);
        string title = model.GetTitle();
        try
        {
            LinkNode tree;
            ExportHelper helper = BuildAssemblyHelper(model, out tree);
            DumpTree(tree, "tree");
            var links = new List<Link>(); CollectLinks(helper.URDFRobot.BaseLink, links);
            Put("links", links.Count);
            int index = 0;
            foreach (Link link in links)
            {
                if (link.isFixedFrame) { index++; continue; }
                Put("link" + index + ".name", link.Name);
                WriteLinkReference("link" + index, link, model, link.Joint.CoordinateSystemName);
                index++;
            }
            string root = Path.Combine(outDir, "export");
            ExportRobot(helper, root, package + "_obj", MeshExportFormat.OBJ, CancellationToken.None);
            string objPackage = Path.Combine(root, package + "_obj");
            Put("obj.messages", messages.Count);
            Put("obj.run1", Hashes(objPackage));
            CopyTree(objPackage, objPackage + "_run1");
            helper = BuildAssemblyHelper(model, out tree);
            ExportRobot(helper, root, package + "_stl", MeshExportFormat.STL, CancellationToken.None);
            if (repeat)
            {
                // Same folder again (fresh robot, as for every run in the add-in): must be repeatable, byte for byte
                int before = messages.Count;
                helper = BuildAssemblyHelper(model, out tree);
                ExportRobot(helper, root, package + "_obj", MeshExportFormat.OBJ, CancellationToken.None);
                Put("obj.run2", Hashes(objPackage));
                CopyTree(objPackage, objPackage + "_run2");
                Put("obj.run2.messages", messages.Count - before);
                // A cancelled export (token cancelled before the conversion starts) must keep the previous output
                var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                before = messages.Count;
                helper = BuildAssemblyHelper(model, out tree);
                ExportRobot(helper, root, package + "_obj", MeshExportFormat.OBJ, cancelled.Token);
                Put("obj.cancelled", Hashes(objPackage));
                Put("obj.cancelled.messages", string.Join(" | ", messages.Skip(before).ToArray()));
                LeftOver("obj.cancelled.leftover", objPackage);
                string objects = Path.Combine(objPackage, "meshes");
                Put("obj.cancelled.files", string.Join(",", Directory.GetFiles(objects).Select(f => Path.GetFileName(f)).OrderBy(f => f).ToArray()));
            }
        }
        finally { app.CloseDoc(title); }
    }

    static ExportHelper BuildNestedHelper(ModelDoc2 model)
    {
        Component2 sub = ((AssemblyDoc)model).GetComponentByName("SUB-1");
        if (sub == null) throw new Exception("SUB-1 not found");
        var link = new Link { Name = "base_link" };
        ComponentList(link).Add(sub);
        link.Joint.CoordinateSystemName = "Origin_sub <SUB-1>";
        var helper = new ExportHelper(app);
        if (!helper.CreateRobotFromTreeView(new LinkNode(link))) throw new Exception("Robot build failed (see the exporter log)");
        return helper;
    }

    static void Nested(string file, string package)
    {
        ModelDoc2 model = Open(file, false);
        string title = model.GetTitle();
        try
        {
            // Turn the sub-assembly by 90 degrees about Z in the open copy (never saved), so that its frame and the
            // global frame differ in orientation as well as in position
            Component2 sub = ((AssemblyDoc)model).GetComponentByName("SUB-1");
            sub.Select4(false, null, false);
            ((AssemblyDoc)model).UnfixComponent();
            model.ClearSelection2(true);
            double[] t = (double[])sub.Transform2.ArrayData;
            double[] turned = new double[] { 0, 1, 0, -1, 0, 0, 0, 0, 1, t[9], t[10], t[11], 1, 0, 0, 0 };
            sub.Transform2 = (MathTransform)math.CreateTransform(turned);
            model.ForceRebuild3(false);
            Put("nested.transform", string.Join(" ", ((double[])sub.Transform2.ArrayData).Select(Num).ToArray()));
            ExportHelper helper = BuildNestedHelper(model);
            Link built = helper.URDFRobot.BaseLink;
            Put("links", 1);
            Put("link0.name", built.Name);
            WriteLinkReference("link0", built, model, "Origin_sub <SUB-1>");
            string root = Path.Combine(outDir, "export");
            ExportRobot(helper, root, package + "_obj", MeshExportFormat.OBJ, CancellationToken.None);
            Put("obj.messages", messages.Count);
            ExportRobot(BuildNestedHelper(model), root, package + "_stl", MeshExportFormat.STL, CancellationToken.None);
        }
        finally { app.CloseDoc(title); }
    }

    static void WritePartReference(ModelDoc2 model, double[] box)
    {
        MathTransform frame = model.Extension.GetCoordinateSystemTransformByName("Origin_global");
        IMassProperty2 mp = (IMassProperty2)model.Extension.CreateMassProperty2();
        mp.UseSystemUnits = true;
        if (frame != null && !mp.SetCoordinateSystem(frame)) throw new Exception("Could not set the link frame");
        if (!mp.Recalculate()) throw new Exception("Mass properties failed");
        double[] com = Array3(mp.CenterOfMass);
        Put("link0.name", "part");
        Put("link0.sw.mass", Num(mp.Mass));
        Put("link0.sw.volume", Num(mp.Volume));
        Put("link0.sw.com_link", Num(com[0]) + " " + Num(com[1]) + " " + Num(com[2]));
        Put("link0.sw.box_global", string.Join(" ", box.Select(Num).ToArray()));
        Put("link0.sw.frame", frame == null ? "part origin" : "Origin_global");
    }

    static void Part(string file, string package)
    {
        ModelDoc2 model = Open(file, true);
        string title = model.GetTitle();
        try
        {
            string root = Path.Combine(outDir, "export");
            // before the export, which adds a coordinate system that would enlarge the box
            double[] box = (double[])((PartDoc)model).GetPartBox(true);
            foreach (MeshExportFormat format in new[] { MeshExportFormat.OBJ, MeshExportFormat.STL })
            {
                var helper = new ExportHelper(app);
                helper.SavePath = root; helper.PackageName = package + (format == MeshExportFormat.OBJ ? "_obj" : "_stl");
                helper.CreateRobotFromActiveModel();
                helper.URDFRobot.Name = package; helper.URDFRobot.BaseLink.Name = package;
                helper.ExportLink(true, format);
                if (format == MeshExportFormat.OBJ)
                {
                    Put("links", 1);
                    Put("obj.messages", messages.Count);
                    WritePartReference(model, box); // after the export: Origin_global exists now
                }
            }
        }
        finally { app.CloseDoc(title); }
    }

    static void Texture(string file, string package)
    {
        ModelDoc2 model = Open(file, true);
        string title = model.GetTitle();
        try
        {
            string root = Path.Combine(outDir, "export");
            long logStart = LogLength();
            // A: fresh folder. The export must fail cleanly and write no half package.
            ExportOne(model, root, package + "_fresh", package, false);
            // B: a folder that already holds an export. Its files must stay as they are.
            ExportOne(model, root, package + "_existing", package, true);
            Put("log.texture", LogSince(logStart, "texture"));
            // The converter on the intermediate file that was left beside the failed export: its own message
            string source = Path.Combine(root, package + "_fresh", "meshes", package + ".3dxml");
            if (File.Exists(source))
            {
                try { ObjConverter.Convert(source, Path.Combine(outDir, "convert_check.obj"), new ObjConversionOptions { LinkName = package }); Put("convert.error", "none"); }
                catch (Exception e) { Put("convert.error", e.GetType().Name + ": " + e.Message); }
            }
            else Put("convert.error", "no intermediate file");
        }
        finally { app.CloseDoc(title); }
    }

    static long LogLength()
    {
        try { var info = new FileInfo(Logger.GetFileName()); return info.Exists ? info.Length : 0; } catch (Exception) { return 0; }
    }

    static string LogSince(long start, string needle)
    {
        try
        {
            using (var stream = new FileStream(Logger.GetFileName(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(Math.Min(start, stream.Length), SeekOrigin.Begin);
                string text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
                var hits = text.Split(new char[] { (char)10 }).Where(l => l.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).Select(l => l.Trim()).Distinct().ToArray();
                return hits.Length == 0 ? "" : string.Join(" | ", hits);
            }
        }
        catch (Exception e) { return "log not readable: " + e.Message; }
    }

    static string Seeded(string folder, string link)
    {
        return string.Join(";", new[] { "meshes\\" + link + ".obj", "meshes\\" + link + ".mtl", "urdf\\" + link + ".urdf" }.Select(f => f + "=" + Sha(Path.Combine(folder, f))).ToArray());
    }

    static void ExportOne(ModelDoc2 model, string root, string packageName, string link, bool seed)
    {
        string folder = Path.Combine(root, packageName);
        string tag = seed ? "existing" : "fresh";
        if (seed)
        {
            foreach (string sub in new[] { "meshes", "urdf" }) Directory.CreateDirectory(Path.Combine(folder, sub));
            File.WriteAllText(Path.Combine(folder, "meshes", link + ".obj"), "existing OBJ");
            File.WriteAllText(Path.Combine(folder, "meshes", link + ".mtl"), "existing MTL");
            File.WriteAllText(Path.Combine(folder, "urdf", link + ".urdf"), "existing URDF");
            Put(tag + ".seeded.before", Seeded(folder, link));
        }
        int before = messages.Count;
        var helper = new ExportHelper(app);
        helper.SavePath = root; helper.PackageName = packageName;
        helper.CreateRobotFromActiveModel();
        helper.URDFRobot.Name = link; helper.URDFRobot.BaseLink.Name = link;
        string thrown = "none";
        try { helper.ExportLink(true, MeshExportFormat.OBJ); }
        catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message; }
        Put(tag + ".exception", thrown);
        Put(tag + ".messages", string.Join(" | ", messages.Skip(before).ToArray()));
        Put(tag + ".seeded.after", Seeded(folder, link));
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => f.Substring(folder.Length + 1)).OrderBy(f => f).ToArray();
        Put(tag + ".files", string.Join(",", files));
        LeftOver(tag + ".leftover", folder);
    }

    // The real progress dialog (a private nested class of the converter), shown at this process DPI.
    static void Dialog(int pid)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        UiText ui = pid != 0 ? UiText.FromSolidWorks(app) : new UiText(null);
        Type type = typeof(ExportHelper).Assembly.GetType("SW2URDF.URDFExport.ObjConversionRunner+ProgressDialog", true);
        var cancel = new CancellationTokenSource();
        using (var owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(10, 10), ShowInTaskbar = false })
        {
            owner.Show();
            var dialog = (Form)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { cancel, ui }, null);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-32000, 100);
            dialog.ShowInTaskbar = false;
            dialog.Show(owner);
            Application.DoEvents();
            Put("dialog.dpi", dialog.DeviceDpi);
            Put("dialog.client", dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            Put("dialog.title", dialog.Text);
            // Screenshot of the window itself (works off-screen)
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(dialog.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                string shot = Path.Combine(outDir, "progress_dialog.png");
                bitmap.Save(shot, ImageFormat.Png);
                Put("dialog.screenshot", shot);
            }
            // Clipping: text that needs more room than its control has
            var clipped = new List<string>();
            foreach (Control c in dialog.Controls)
            {
                if (c.Text.Length == 0) continue;
                Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, c.ClientSize.Width - c.Padding.Horizontal), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                bool wide = need.Width > c.ClientSize.Width - c.Padding.Horizontal;
                bool tall = need.Height + c.Padding.Vertical > c.ClientSize.Height;
                Put("dialog.control." + c.GetType().Name, "'" + c.Text + "' box " + c.Width + "x" + c.Height + " text " + need.Width + "x" + need.Height + (wide || tall ? " CLIPPED" : " fits"));
                if (wide || tall) clipped.Add(c.GetType().Name + ":" + c.Text);
            }
            Size title = TextRenderer.MeasureText(dialog.Text, SystemFonts.CaptionFont);
            bool titleClipped = title.Width > dialog.ClientSize.Width - 60;
            Put("dialog.title_width", title.Width + " of " + dialog.ClientSize.Width + (titleClipped ? " CLIPPED" : " fits"));
            if (titleClipped) clipped.Add("title");
            Put("dialog.clipped", clipped.Count == 0 ? "none" : string.Join("; ", clipped.ToArray()));
            // Cancel button: must request cancellation
            Button button = dialog.Controls.OfType<Button>().Single();
            Put("dialog.cancel_before", cancel.IsCancellationRequested);
            button.PerformClick();
            Application.DoEvents();
            Put("dialog.cancel_after", cancel.IsCancellationRequested);
            Put("dialog.cancel_button_disabled", !button.Enabled);
            dialog.Close();
        }
    }

    [STAThread] public static int Main(string[] args)
    {
        int code = 0;
        try
        {
            string mode = args[0];
            int pid = int.Parse(args[1]);
            outDir = Path.GetFullPath(mode == "dialog" ? args[2] : args[3]);
            Directory.CreateDirectory(outDir);
            if (pid != 0) { app = Bind(pid); math = (IMathUtility)app.GetMathUtility(); }
            URDFPackage.MessageBox = new RecordingMessages();
            Put("mode", mode);
            if (pid != 0) Put("language", app.GetCurrentLanguage());
            switch (mode)
            {
                case "assembly": Assembly(Path.GetFullPath(args[2]), args[4], true); break;
                case "part": Part(Path.GetFullPath(args[2]), args[4]); break;
                case "nested": Nested(Path.GetFullPath(args[2]), args[4]); break;
                case "texture": Texture(Path.GetFullPath(args[2]), args[4]); break;
                case "dialog": Dialog(pid); break;
                default: throw new Exception("unknown mode " + mode);
            }
            Put("messages", string.Join(" | ", messages.ToArray()));
            Put("status", "ok");
        }
        catch (Exception e) { Put("status", "error"); Put("error", e.ToString()); Console.Error.WriteLine(e); code = 1; }
        finally { if (outDir != null) File.WriteAllLines(Path.Combine(outDir, "probe.tsv"), rows.ToArray(), new UTF8Encoding(false)); }
        return code;
    }
}
