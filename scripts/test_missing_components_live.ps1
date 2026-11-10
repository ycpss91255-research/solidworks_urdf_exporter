<#
.SYNOPSIS
    Live check (real SolidWorks) of missing-component handling and orphan attribute detection.
.DESCRIPTION
    Binds to a SolidWorks PID that you own (never the "active object"). In a new temporary
    folder it creates one empty part and an assembly with two instances of it and two
    configurations, where configuration B suppresses the second instance. It then checks that
      1. a model that only has the shared (older) attribute reports the link as missing a
         component in configuration B, MissingComponentPolicy asks for confirmation and its
         message names the link and the configuration; in configuration A nothing is reported;
      2. after a configuration with its own attribute is renamed, the attribute is detected as
         an orphan and the configurations without data are listed, and loading is not blocked.
    Only files in -WorkDirectory are created or changed. Exits non-zero when a check fails.
    Requires a built and Authenticode-signed SW2URDF.dll (the signature must be Valid).
.PARAMETER TargetPid
    SolidWorks process owned by this session.
.PARAMETER AssemblyDirectory
    Folder with the signed SW2URDF.dll and its dependencies.
.PARAMETER WorkDirectory
    Empty or new folder for the temporary part and assembly.
#>
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [Parameter(Mandatory=$true)][string]$AssemblyDirectory,
    [Parameter(Mandatory=$true)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$d = (Resolve-Path -LiteralPath $AssemblyDirectory).Path
$dll = Join-Path $d 'SW2URDF.dll'
$signature = Get-AuthenticodeSignature -LiteralPath $dll
if ($signature.Status -ne 'Valid') { throw "Refusing to load an unsigned or invalid SW2URDF.dll: $($signature.Status)" }
foreach ($n in 'log4net','MathNet.Numerics','SolidWorks.Interop.sldworks','SolidWorks.Interop.swconst') {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $d "$n.dll"))
}
[void][Reflection.Assembly]::LoadFrom($dll)
$work = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Force $work | Out-Null

$src = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;

public static class MissingComponentsProbe
{
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx context);
    static int failures = 0;

    static SldWorks Bind(int pid)
    {
        IRunningObjectTable rot; IBindCtx ctx;
        GetRunningObjectTable(0, out rot); CreateBindCtx(0, out ctx);
        IEnumMoniker it; rot.EnumRunning(out it);
        IMoniker[] next = new IMoniker[1];
        while (it.Next(1, next, IntPtr.Zero) == 0)
        {
            string name; next[0].GetDisplayName(ctx, null, out name);
            if (name != "SolidWorks_PID_" + pid) continue;
            object v; rot.GetObject(next[0], out v);
            SldWorks app = (SldWorks)v;
            if (app.GetProcessID() != pid) throw new Exception("PID mismatch");
            return app;
        }
        throw new Exception("Owned PID absent from the running object table");
    }

    static void Check(bool ok, string msg)
    {
        if (!ok) failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + msg);
    }

    static ModelDoc2 NewDoc(SldWorks app, int templatePref)
    {
        string template = app.GetUserPreferenceStringValue(templatePref);
        if (string.IsNullOrEmpty(template) || !File.Exists(template)) throw new Exception("No default template: " + template);
        ModelDoc2 m = (ModelDoc2)app.NewDocument(template, 0, 0, 0);
        if (m == null) throw new Exception("NewDocument failed");
        return m;
    }

    static void SaveAs(ModelDoc2 m, string path)
    {
        int e = 0, w = 0;
        if (!m.Extension.SaveAs(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w))
            throw new Exception("SaveAs failed " + e + " for " + path);
    }

    static LinkNode Tree(ModelDoc2 model, string linkName)
    {
        Link link = new Link();
        link.Name = linkName;
        link.Joint.CoordinateSystemName = "Automatically Generate";
        // SWComponents is typed with the interop types embedded in SW2URDF.dll, so add through IList
        System.Collections.IList list = (System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(link);
        foreach (object o in (object[])((AssemblyDoc)model).GetComponents(true)) list.Add(o);
        LinkNode node = new LinkNode(link);
        CommonSwOperations.RetrieveSWComponentPIDs(model, node);
        return node;
    }

    static List<string> Problems(ModelDoc2 model, out string linkName)
    {
        bool err;
        LinkNode tree = ConfigurationSerialization.LoadBaseNodeFromModel(model, out err);
        linkName = tree == null ? null : tree.Link.Name;
        List<string> problems = new List<string>();
        if (tree != null) CommonSwOperations.LoadSWComponents(model, tree, problems);
        return problems;
    }

    public static int Run(int pid, string work)
    {
        SldWorks app = Bind(pid);
        string partPath = Path.Combine(work, "part_x.SLDPRT");
        string asmPath = Path.Combine(work, "asm_y.SLDASM");

        ModelDoc2 part = NewDoc(app, (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        SaveAs(part, partPath);
        string partTitle = part.GetTitle(); // keep it open: AddComponent5 needs it in memory

        ModelDoc2 model = NewDoc(app, (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
        AssemblyDoc assy = (AssemblyDoc)model;
        Component2 c1 = assy.AddComponent5(partPath, 0, "", false, "", 0, 0, 0);
        Component2 c2 = assy.AddComponent5(partPath, 0, "", false, "", 0.1, 0, 0);
        if (c1 == null || c2 == null) throw new Exception("Could not add components");
        app.CloseDoc(partTitle);
        SaveAs(model, asmPath);

        string cfgA = ((string[])model.GetConfigurationNames())[0];
        const string cfgB = "Cfg_B";
        if (model.AddConfiguration3(cfgB, "", "", 0) == null) throw new Exception("Could not add configuration");
        model.ShowConfiguration2(cfgB);
        // suppress the instance that was added last, in configuration B only
        string suppressedName = c2.Name2;
        foreach (object o in (object[])assy.GetComponents(true))
        {
            Component2 c = (Component2)o;
            if (c.Name2 == suppressedName) c.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed);
        }
        model.ShowConfiguration2(cfgA);

        BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Static;
        Type ser = typeof(ConfigurationSerialization);
        // The shared (older) attribute only, written like earlier exporters did
        LinkNode node = Tree(model, "link_all");
        string data = (string)ser.GetMethod("SerializeToString", bf).Invoke(null, new object[] { node });
        SolidWorks.Interop.sldworks.Attribute shared = (SolidWorks.Interop.sldworks.Attribute)
            ser.GetMethod("CreateSWSaveAttribute", bf).Invoke(null, new object[] { app, model, ConfigurationSerialization.UrdfConfigurationSwAttributeName });
        int all = (int)swInConfigurationOpts_e.swAllConfiguration;
        ((Parameter)shared.GetParameter("data")).SetStringValue2(data, all, "");
        ((Parameter)shared.GetParameter("name")).SetStringValue2("config1", all, "");
        ((Parameter)shared.GetParameter("date")).SetStringValue2(DateTime.Now.ToString(), all, "");
        ((Parameter)shared.GetParameter("exporterVersion")).SetDoubleValue2(1.4, all, "");

        // ---- 1. missing components under the shared fallback ----
        string n;
        model.ShowConfiguration2(cfgA);
        List<string> problemsA = Problems(model, out n);
        Check(n == "link_all" && problemsA.Count == 0, "config A (all parts present): no link reported");
        Check(!MissingComponentPolicy.MustConfirmExport(problemsA) && MissingComponentPolicy.BuildMessage(problemsA, cfgA, true) == null, "config A: no confirmation required");
        model.ShowConfiguration2(cfgB);
        List<string> problemsB = Problems(model, out n);
        Check(problemsB.Count == 1 && problemsB[0] == "link_all", "config B (second part suppressed): link_all reported missing a component");
        Check(MissingComponentPolicy.MustConfirmExport(problemsB), "config B: export needs confirmation");
        string msg = MissingComponentPolicy.BuildMessage(problemsB, cfgB, true);
        Check(msg != null && msg.Contains("link_all") && msg.Contains(cfgB) && msg.ToLowerInvariant().Contains("incomplete"), "config B: message names the link and configuration and says the result is incomplete");

        // ---- 2. orphan detection after a rename ----
        model.ShowConfiguration2(cfgA);
        ConfigurationSerialization.SaveConfigTreeXML(app, model, Tree(model, "link_A_own"), false);
        ((Configuration)model.GetConfigurationByName(cfgA)).Name = "Renamed_A";
        string[] configs = (string[])model.GetConfigurationNames();
        System.Collections.IEnumerable features = (System.Collections.IEnumerable)ser.GetMethod("GetConfigurationAttributeFeatures", bf).Invoke(null, new object[] { model });
        List<string> attributeNames = new List<string>();
        foreach (Feature f in features)
        {
            string an = ((SolidWorks.Interop.sldworks.Attribute)f.GetSpecificFeature2()).GetName();
            if (an != ConfigurationSerialization.UrdfConfigurationSwAttributeName) attributeNames.Add(an);
        }
        Console.WriteLine("attributes=[" + string.Join(",", attributeNames.ToArray()) + "] configurations=[" + string.Join(",", configs) + "]");
        List<string> orphans = OrphanAttributes.FindOrphans(attributeNames, configs);
        List<string> without = OrphanAttributes.FindConfigurationsWithoutAttribute(attributeNames, configs);
        Check(orphans.Count == 1 && orphans[0] == cfgA, "after renaming the first configuration its attribute is detected as an orphan");
        Check(without.Contains("Renamed_A") && without.Contains(cfgB), "configurations without own data are listed");
        Check(OrphanAttributes.BuildLogMessage(orphans, without) != null, "a log message is produced");
        // the real loader path must not throw or block (detection only)
        bool err; ConfigurationSerialization.LoadBaseNodeFromModel(model, out err);
        Check(!err, "loading after the rename is not blocked");

        app.CloseDoc(model.GetTitle());
        return failures;
    }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies @('System.Windows.Forms.dll','System.Drawing.dll','System.Runtime.Serialization.dll',$dll,(Join-Path $d 'SolidWorks.Interop.sldworks.dll'),(Join-Path $d 'SolidWorks.Interop.swconst.dll'),'WindowsBase')
try { $failed = [MissingComponentsProbe]::Run($TargetPid, $work) }
catch {
    $e = $_.Exception
    if ($e.InnerException) { $e = $e.InnerException }
    Write-Host $e.ToString()
    exit 2
}
if ($failed -gt 0) { Write-Error "$failed check(s) failed"; exit 1 }
Write-Host 'All missing-component and orphan checks passed.'
