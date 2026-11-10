// Native CLI test: exact PID ROT binding, disposable models only, no COM registration.
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

public static class ObjExportCli
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
    sealed class ConsoleMessages : IMessageBox
    {
        public System.Windows.MessageBoxResult Show(string message) { Console.WriteLine(message); return System.Windows.MessageBoxResult.OK; }
        public System.Windows.MessageBoxResult Show(string message,string caption,System.Windows.MessageBoxButton buttons) { return Show(message); }
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            int pid=int.Parse(args[0]); string models=Path.GetFullPath(args[1]), output=Path.GetFullPath(args[2]);
            Directory.CreateDirectory(output); var app=Bind(pid); app.Visible=true; app.UserControl=true;
            Console.WriteLine("PID="+app.GetProcessID()+"; language="+app.GetCurrentLanguage());
            foreach(ModelDoc2 open in (object[])(app.GetDocuments() ?? new object[0]))
            {
                if(!open.GetPathName().StartsWith(models+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Non-test document in owned session");
                app.CloseDoc(open.GetTitle());
            }
            URDFPackage.MessageBox=new ConsoleMessages();
            foreach(bool part in new[]{true,false}) foreach(MeshExportFormat format in new[]{MeshExportFormat.STL,MeshExportFormat.OBJ})
            {
                string test=(part?"part":"assembly")+"-"+format;
                string file=Path.Combine(models,part?"3_DOF_ARM_BASE.SLDPRT":"3_DOF_ARM.SLDASM");
                if(!part)
                {
                    var dependencies=(string[])app.GetDocumentDependencies2(file,false,false,false);
                    if(dependencies!=null) for(int i=1;i<dependencies.Length;i+=2)
                    {
                        string local=Path.Combine(models,Path.GetFileName(dependencies[i]));
                        if(!File.Exists(local))throw new Exception("Missing copied dependency "+local);
                        if(!String.Equals(dependencies[i],local,StringComparison.OrdinalIgnoreCase) && !app.ReplaceReferencedDocument(file,dependencies[i],local))
                            throw new Exception("Could not localize "+dependencies[i]);
                    }
                }
                Console.WriteLine("OPEN "+test+" "+file);
                int err=0,warn=0; ModelDoc2 model=app.OpenDoc6(file,part?1:2,1,"",ref err,ref warn);
                if(model==null||err!=0)throw new Exception("Open failed "+err+"/"+warn);
                string title=model.GetTitle(); int activateError=0; app.ActivateDoc3(title,false,0,ref activateError);
                var paths=new List<string>(); paths.Add(model.GetPathName());
                if(!part) foreach(Component2 component in (object[])((AssemblyDoc)model).GetComponents(false))
                {
                    string path=component.GetPathName();paths.Add(path);
                    if(!Path.GetFullPath(path).StartsWith(models+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        throw new Exception("External live dependency: "+path);
                    if(component.GetModelDoc2()==null)throw new Exception("Unresolved component: "+path);
                }
                File.WriteAllLines(Path.Combine(output,test+"-references.txt"),paths);
                try
                {
                    var helper=new ExportHelper(app);helper.SavePath=Path.Combine(output,test);helper.PackageName=part?"part_test":"assembly_test";
                    if(part)
                    {
                        helper.CreateRobotFromActiveModel();helper.URDFRobot.Name="part_test";helper.URDFRobot.BaseLink.Name="part_test";
                        helper.ExportLink(true,format);
                    }
                    else
                    {
                        bool loadError;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(model,out loadError);
                        if(loadError||tree==null)throw new Exception("No saved URDF tree");
                        var missing=new List<string>();CommonSwOperations.LoadSWComponents(model,tree,missing);
                        if(missing.Count>0)throw new Exception("Missing saved references");
                        if(!helper.CreateRobotFromTreeView(tree))throw new Exception("Robot build failed");
                        helper.URDFRobot.Name="assembly_test";helper.ExportRobot(true,format);
                    }
                    string urdf=Path.Combine(helper.SavePath,helper.PackageName,"urdf",helper.URDFRobot.Name+".urdf");
                    if(!File.Exists(urdf))throw new Exception("Missing URDF: "+urdf);
                    Console.WriteLine("EXPORTED "+urdf);
                }
                finally { app.CloseDoc(title); }
            }
            Console.WriteLine("PASS: native part and assembly STL/OBJ exports; all dependencies local");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
