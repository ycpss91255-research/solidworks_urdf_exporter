// Live checks of the per-configuration URDF export settings, driven through the real
// ConfigurationSerialization / ExportHelper of SW2URDF.dll. Started by test_configuration_live.py, which
// builds the scratch models and compiles this file. Binds to one process through its ROT entry
// (SolidWorks_PID_<pid>) and only opens and closes the scratch copies it makes itself.
// Usage: ConfigurationLiveProbe.exe <pid> <models folder> <work folder>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;

public static class ConfigurationLiveProbe
{
    const string AsmName = "TWO_BOXES", Red = "BOX_RED", Blue = "BOX_BLUE";
    const string Both = "CFG_BOTH", RedOnly = "CFG_RED_ONLY", Short = "Short", Tall = "Tall";
    // Chinese, Latin with diacritics, and a Greek letter, written as escapes to keep this file ASCII
    const string NonAscii = "連桿_底座_üé_Ω";
    const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    static SldWorks app;
    static int failed, passed;

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
            var found = (SldWorks)value;
            if (found.GetProcessID() != pid) throw new Exception("process id mismatch");
            return found;
        }
        throw new Exception("SolidWorks_PID_" + pid + " is not in the Running Object Table");
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) passed++; else failed++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (ok || detail.Length == 0 ? "" : "  (" + detail + ")"));
    }

    static void Eq(string name, object expected, object actual)
    {
        Check(name, Equals(expected, actual), "expected '" + expected + "', got '" + actual + "'");
    }

    static string CopyModels(string models, string work, string tag, params string[] files)
    {
        string dir = Path.Combine(work, tag);
        Directory.CreateDirectory(dir);
        foreach (string file in files) File.Copy(Path.Combine(models, file), Path.Combine(dir, file), true);
        return dir;
    }

    static ModelDoc2 Open(string path, swDocumentTypes_e type)
    {
        int errors = 0, warnings = 0;
        ModelDoc2 doc = app.OpenDoc6(path, (int)type, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "",
            ref errors, ref warnings);
        if (doc == null) throw new Exception("could not open " + path + " (error " + errors + ")");
        return doc;
    }

    static void Close(ModelDoc2 doc) { app.CloseDoc(doc.GetTitle()); }

    static bool Show(ModelDoc2 doc, string configuration)
    {
        doc.ShowConfiguration2(configuration);
        return doc.ConfigurationManager.ActiveConfiguration.Name == configuration;
    }

    static LinkNode BuildTree(ModelDoc2 doc, string linkName, params string[] partNames)
    {
        Link link = new Link { Name = linkName };
        link.Joint.CoordinateSystemName = "Automatically Generate";
        // List<Component2> crosses assemblies that both embed the interop types, so set it by reflection
        List<Component2> components = ((object[])((AssemblyDoc)doc).GetComponents(true)).Cast<Component2>()
            .Where(c => partNames.Any(p => c.Name2.Contains(p))).ToList();
        System.Collections.IList target = (System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(link);
        target.Clear();
        foreach (Component2 component in components) target.Add(component);
        LinkNode node = new LinkNode(link);
        CommonSwOperations.RetrieveSWComponentPIDs(doc, node);
        return node;
    }

    static string LoadName(ModelDoc2 doc)
    {
        bool error;
        LinkNode node = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out error);
        return error || node == null ? "<none>" : node.Link.Name;
    }

    static void Save(ModelDoc2 doc, LinkNode node) { ConfigurationSerialization.SaveConfigTreeXML(app, doc, node, false); }

    static void WriteSharedAttribute(ModelDoc2 doc, LinkNode node)
    {
        Type type = typeof(ConfigurationSerialization);
        string data = (string)type.GetMethod("SerializeToString", NonPublicStatic).Invoke(null, new object[] { node });
        var attribute = (SolidWorks.Interop.sldworks.Attribute)type.GetMethod("CreateSWSaveAttribute", NonPublicStatic)
            .Invoke(null, new object[] { app, doc, ConfigurationSerialization.UrdfConfigurationSwAttributeName });
        int all = (int)swInConfigurationOpts_e.swAllConfiguration;
        attribute.GetParameter("data").SetStringValue2(data, all, "");
        attribute.GetParameter("name").SetStringValue2("config1", all, "");
        attribute.GetParameter("date").SetStringValue2(DateTime.Now.ToString(), all, "");
        attribute.GetParameter("exporterVersion").SetDoubleValue2(1.4, all, "");
    }

    static List<string> ConfigurationAttributeNames(ModelDoc2 doc)
    {
        List<string> names = new List<string>();
        Feature feature = doc.FirstFeature() as Feature;
        while (feature != null)
        {
            if (feature.GetTypeName2() == "Attribute")
            {
                var attribute = (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                if (attribute.GetParameter("data") != null && attribute.GetParameter("exporterVersion") != null &&
                    attribute.GetName() != ConfigurationSerialization.UrdfConfigurationSwAttributeName)
                    names.Add(attribute.GetName());
            }
            feature = feature.GetNextFeature() as Feature;
        }
        return names;
    }

    static bool HasFolder(ModelDoc2 doc)
    {
        Feature feature = doc.FirstFeature() as Feature;
        while (feature != null)
        {
            if (feature.Name == ConfigurationSerialization.ConfigurationAttributeFolderName &&
                feature.GetTypeName2().Contains("Folder")) return true;
            feature = feature.GetNextFeature() as Feature;
        }
        return false;
    }

    static void Assembly_Independence(string models, string work)
    {
        Console.WriteLine("-- assembly: each SolidWorks configuration keeps its own settings");
        string dir = CopyModels(models, work, "asm_own", Red + ".SLDPRT", Blue + ".SLDPRT", AsmName + ".SLDASM");
        string path = Path.Combine(dir, AsmName + ".SLDASM");
        ModelDoc2 doc = Open(path, swDocumentTypes_e.swDocASSEMBLY);
        try
        {
            Check("activate " + Both, Show(doc, Both));
            Eq("new configuration reports 'New' settings", SettingsSource.New, ConfigurationSerialization.GetSettingsSource(doc));
            Save(doc, BuildTree(doc, "link_both", Red, Blue));
            Check("activate " + RedOnly, Show(doc, RedOnly));
            Eq(RedOnly + " does not see " + Both + "'s settings", SettingsSource.New, ConfigurationSerialization.GetSettingsSource(doc));
            Save(doc, BuildTree(doc, "link_red", Red));

            Show(doc, Both);
            Eq(Both + " loads its own link name", "link_both", LoadName(doc));
            Eq(Both + " reports its own settings", SettingsSource.Configuration, ConfigurationSerialization.GetSettingsSource(doc));
            Show(doc, RedOnly);
            Eq(RedOnly + " loads its own link name", "link_red", LoadName(doc));
            Show(doc, Both);
            Eq(Both + " is unchanged after switching back", "link_both", LoadName(doc));

            List<string> names = ConfigurationAttributeNames(doc);
            Check("one attribute per configuration, named after it",
                names.OrderBy(n => n).SequenceEqual(new[] { Both, RedOnly }.OrderBy(n => n)), string.Join(",", names));
            Check("feature tree folder '" + ConfigurationSerialization.ConfigurationAttributeFolderName + "' exists", HasFolder(doc));

            int errors = 0, warnings = 0;
            Check("save the model", doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings));
            Close(doc);
            doc = Open(path, swDocumentTypes_e.swDocASSEMBLY);
            Show(doc, Both);
            Eq(Both + " survives save, close and reopen", "link_both", LoadName(doc));
            Show(doc, RedOnly);
            Eq(RedOnly + " survives save, close and reopen", "link_red", LoadName(doc));
        }
        finally { Close(doc); }
    }

    static void Assembly_LegacyFallback(string models, string work)
    {
        Console.WriteLine("-- assembly: legacy shared attribute is the fallback");
        string dir = CopyModels(models, work, "asm_legacy", Red + ".SLDPRT", Blue + ".SLDPRT", AsmName + ".SLDASM");
        ModelDoc2 doc = Open(Path.Combine(dir, AsmName + ".SLDASM"), swDocumentTypes_e.swDocASSEMBLY);
        try
        {
            Show(doc, Both);
            WriteSharedAttribute(doc, BuildTree(doc, "link_shared", Red));
            Eq("only the shared attribute exists: no per-configuration attribute", 0, ConfigurationAttributeNames(doc).Count);
            Eq(Both + " reports the shared settings", SettingsSource.Shared, ConfigurationSerialization.GetSettingsSource(doc));
            Eq(Both + " loads the shared link name", "link_shared", LoadName(doc));
            Show(doc, RedOnly);
            Eq(RedOnly + " loads the shared link name", "link_shared", LoadName(doc));

            Show(doc, Both);
            Save(doc, BuildTree(doc, "link_own", Red));
            Eq(Both + " now prefers its own settings", "link_own", LoadName(doc));
            Eq(Both + " reports its own settings", SettingsSource.Configuration, ConfigurationSerialization.GetSettingsSource(doc));
            Show(doc, RedOnly);
            Eq(RedOnly + " still falls back to the shared settings", "link_shared", LoadName(doc));
            Eq("saving wrote only the active configuration's attribute", 1, ConfigurationAttributeNames(doc).Count);
        }
        finally { Close(doc); }
    }

    static void Assembly_NonAsciiAndRename(string models, string work)
    {
        Console.WriteLine("-- assembly: non-ASCII names and renaming a configuration");
        string dir = CopyModels(models, work, "asm_names", Red + ".SLDPRT", Blue + ".SLDPRT", AsmName + ".SLDASM");
        string path = Path.Combine(dir, AsmName + ".SLDASM");
        ModelDoc2 doc = Open(path, swDocumentTypes_e.swDocASSEMBLY);
        try
        {
            Show(doc, Both);
            Save(doc, BuildTree(doc, NonAscii, Red));
            Eq("non-ASCII link name round trip", NonAscii, LoadName(doc));
            int errors = 0, warnings = 0;
            doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            Close(doc);
            doc = Open(path, swDocumentTypes_e.swDocASSEMBLY);
            Show(doc, Both);
            Eq("non-ASCII link name survives save, close and reopen", NonAscii, LoadName(doc));

            // a configuration with a non-ASCII name gets its own attribute
            string configName = "CFG_" + NonAscii;
            bool added = doc.AddConfiguration3(configName, "", "", 0) != null;
            Check("add configuration with a non-ASCII name", added);
            if (added)
            {
                Show(doc, configName);
                Eq("non-ASCII configuration falls back to nothing yet", SettingsSource.New, ConfigurationSerialization.GetSettingsSource(doc));
                Save(doc, BuildTree(doc, "link_unicode_cfg", Red));
                Show(doc, Both);
                Show(doc, configName);
                Eq("non-ASCII configuration keeps its own link name", "link_unicode_cfg", LoadName(doc));
                Check("its attribute is named after the configuration", ConfigurationAttributeNames(doc).Contains(configName));
            }

            // rename: the old attribute no longer matches any configuration (logged as orphan), the settings are not lost
            const string renamed = "CFG_RENAMED";
            doc.GetConfigurationByName(Both).Name = renamed;
            string[] configurations = (string[])doc.GetConfigurationNames();
            List<string> attributes = ConfigurationAttributeNames(doc);
            Check("renamed configuration exists", configurations.Contains(renamed));
            Check("old attribute is detected as orphan",
                OrphanAttributes.FindOrphans(attributes, configurations).Contains(Both));
            Check("renamed configuration is reported as having no attribute",
                OrphanAttributes.FindConfigurationsWithoutAttribute(attributes, configurations).Contains(renamed));
            Show(doc, renamed);
            Eq("renamed configuration no longer shows its old settings", SettingsSource.New, ConfigurationSerialization.GetSettingsSource(doc));
            bool error;
            ConfigurationSerialization.LoadBaseNodeFromModel(doc, out error);
            Check("loading after a rename is not blocked", !error);
        }
        finally { Close(doc); }
    }

    static ExportHelper OpenPartHelper(out bool loaded)
    {
        ExportHelper helper = new ExportHelper(app);
        helper.CreateRobotFromActiveModel();
        loaded = helper.LoadPartSettings();
        return helper;
    }

    static void SavePartRed(double red)
    {
        bool loaded;
        ExportHelper helper = OpenPartHelper(out loaded);
        helper.URDFRobot.BaseLink.Visual.Material.Color.Red = red;
        helper.SavePartSettings();
    }

    static double PartRed()
    {
        bool loaded;
        return OpenPartHelper(out loaded).URDFRobot.BaseLink.Visual.Material.Color.Red;
    }

    static void Part(string models, string work)
    {
        Console.WriteLine("-- part: per-configuration settings, fallback and non-ASCII");
        string dir = CopyModels(models, work, "part", Blue + ".SLDPRT");
        string path = Path.Combine(dir, Blue + ".SLDPRT");
        ModelDoc2 doc = Open(path, swDocumentTypes_e.swDocPART);
        try
        {
            Show(doc, Short);
            SavePartRed(0.25);
            Show(doc, Tall);
            bool loaded;
            ExportHelper tall = OpenPartHelper(out loaded);
            Check(Tall + " has no settings of its own yet", !loaded);
            SavePartRed(0.75);
            Show(doc, Short);
            Eq(Short + " keeps its own value", 0.25, PartRed());
            Show(doc, Tall);
            Eq(Tall + " keeps its own value", 0.75, PartRed());

            // non-ASCII text in the part settings (the part exporter keeps the visual material name, not the link name)
            Show(doc, Short);
            ExportHelper unicode = OpenPartHelper(out loaded);
            unicode.URDFRobot.BaseLink.Visual.Material.Name = NonAscii;
            unicode.SavePartSettings();
            ExportHelper back = OpenPartHelper(out loaded);
            Eq("part: non-ASCII material name round trip", NonAscii, back.URDFRobot.BaseLink.Visual.Material.Name);
            Close(doc);
            doc = null;

            // legacy shared attribute in a second copy (a document of that name can only be open once)
            string legacyDir = CopyModels(models, work, "part_legacy", Blue + ".SLDPRT");
            ModelDoc2 legacy = Open(Path.Combine(legacyDir, Blue + ".SLDPRT"), swDocumentTypes_e.swDocPART);
            try
            {
                Show(legacy, Short);
                ExportHelper helper = OpenPartHelper(out loaded);
                helper.URDFRobot.BaseLink.Visual.Material.Color.Red = 0.5;
                WriteSharedAttribute(legacy, new LinkNode(helper.URDFRobot.BaseLink));
                Show(legacy, Tall);
                ExportHelper fallback = OpenPartHelper(out loaded);
                Check(Tall + " loads the legacy shared settings", loaded);
                Eq(Tall + " legacy value", 0.5, fallback.URDFRobot.BaseLink.Visual.Material.Color.Red);
            }
            finally { Close(legacy); }
        }
        finally { if (doc != null) Close(doc); }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            int pid = int.Parse(args[0]);
            string models = Path.GetFullPath(args[1]), work = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(work);
            app = Bind(pid);
            Console.WriteLine("Bound to SolidWorks process " + pid);
            if (app.GetDocuments() != null) throw new Exception("the instance must start with no open documents");
            Assembly_Independence(models, work);
            Assembly_LegacyFallback(models, work);
            Assembly_NonAsciiAndRename(models, work);
            Part(models, work);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL  unexpected error: " + e.GetType().Name + ": " + e.Message);
            Console.WriteLine(e.StackTrace);
        }
        Console.WriteLine();
        Console.WriteLine("Result: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
