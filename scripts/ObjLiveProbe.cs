// Development UI host: exact PID ROT binding, disposable models only, no COM registration.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SW2URDF.UI;
using SW2URDF.URDFExport;

public static class ObjLiveProbe
{
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx context);
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
            var app = (SldWorks)value;
            if (app.GetProcessID() != pid) throw new Exception("PID mismatch");
            return app;
        }
        throw new Exception("Owned PID absent from ROT");
    }
    static void SetText(Form form, string field, string value)
    {
        ((Control)form.GetType().GetField(field, BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form)).Text = value;
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            int pid = int.Parse(args[0]); string mode = args[1];
            string models = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
            Directory.CreateDirectory(output);
            var app = Bind(pid); app.Visible = true; app.UserControl = true;
            if (app.GetDocuments() != null) throw new Exception("Probe requires an empty owned session");
            string file = Path.Combine(models, mode == "part" ? "3_DOF_ARM_BASE.SLDPRT" : "3_DOF_ARM.SLDASM");
            if (mode != "part")
            {
                var dependencies = (string[])app.GetDocumentDependencies2(file, false, false, false);
                if (dependencies != null) for (int i=1; i<dependencies.Length; i+=2)
                {
                    string local = Path.Combine(models, Path.GetFileName(dependencies[i]));
                    if (!File.Exists(local)) throw new Exception("Missing copied dependency " + local);
                    if (!String.Equals(dependencies[i], local, StringComparison.OrdinalIgnoreCase) &&
                        !app.ReplaceReferencedDocument(file, dependencies[i], local))
                        throw new Exception("Could not localize reference " + dependencies[i]);
                }
            }
            int error=0, warning=0;
            var model = app.OpenDoc6(file, mode == "part" ? 1 : 2, 1, "", ref error, ref warning);
            if (model == null || error != 0) throw new Exception("Open failed " + error + "/" + warning);
            string title=model.GetTitle(); int activateError=0;
            app.ActivateDoc3(title,false,0,ref activateError);
            var paths=new List<string>(); paths.Add(model.GetPathName());
            if (mode != "part") foreach (Component2 component in (object[])((AssemblyDoc)model).GetComponents(false))
            {
                string path=component.GetPathName(); paths.Add(path);
                if (!Path.GetFullPath(path).StartsWith(models+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new Exception("External live dependency: " + path);
                if(component.GetModelDoc2()==null) throw new Exception("Unresolved component: " + path);
            }
            File.WriteAllLines(Path.Combine(output,mode+"-references.txt"),paths);
            model.ViewZoomtofit2();
            Application.EnableVisualStyles();
            if(mode == "part")
            {
                using(var form=new PartExportForm(app))
                {
                    form.Shown += delegate { SetText(form,"textBox_save_as",Path.Combine(output,"part_obj")); };
                    Application.Run(form);
                }
            }
            else
            {
                bool loadError; var tree=ConfigurationSerialization.LoadBaseNodeFromModel(model,out loadError);
                if(loadError||tree==null)throw new Exception("No saved URDF tree");
                var missing=new List<string>(); CommonSwOperations.LoadSWComponents(model,tree,missing);
                if(missing.Count>0)throw new Exception("Missing saved references");
                var helper=new ExportHelper(app); helper.SavePath=output;
                 helper.CreateRobotFromTreeView(tree);
                using(var form=new AssemblyExportForm(app,tree,helper)) Application.Run(form);
            }
            app.CloseDoc(title); // disposable models only; no save
            File.WriteAllText(Path.Combine(output,mode+"-ui-closed.txt"),"PID="+pid+"; closed copied model without saving");
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
