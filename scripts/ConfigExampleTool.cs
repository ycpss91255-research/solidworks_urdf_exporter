// Helper for scripts/make_config_example.ps1: stores one URDF link tree per SolidWorks configuration in the
// TWO_BOXES example and exports each configuration to a URDF package, using the exporter's own code.
//
//   ConfigExampleTool.exe save   --pid <pid> --assembly <TWO_BOXES.SLDASM>
//   ConfigExampleTool.exe export --pid <pid> --assembly <TWO_BOXES.SLDASM> --config <name> --out <folder>
//
// It binds to the one SolidWorks process named by --pid (Running Object Table), never starts SolidWorks and
// closes only the document it opened. "save" modifies and saves the given assembly, so always pass a copy.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SolidWorks.Interop.sldworks;
using SW2URDF.Test;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;

public sealed class SilentMessages : IMessageBox
{
    public MessageBoxResult Show(string message)
    {
        return MessageBoxResult.OK;
    }

    public MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
    {
        return MessageBoxResult.OK;
    }
}

public static class ConfigExampleTool
{
    // What each configuration stores. Keep in sync with examples/TWO_BOXES/README.md.
    private static readonly Dictionary<string, string[]> LinkContents = new Dictionary<string, string[]>
    {
        { "CFG_BOTH", new[] { "BOX_RED", "BOX_BLUE" } },
        { "CFG_RED_ONLY", new[] { "BOX_RED" } },
    };

    public static int Main(string[] args)
    {
        try
        {
            Dictionary<string, string> options = Parse(args);
            string mode = args.Length > 0 ? args[0] : "";
            SldWorks app = SolidWorksBinding.Connect(int.Parse(options["pid"]));
            URDFPackage.MessageBox = new SilentMessages();
            if (mode == "save")
            {
                Save(app, options["assembly"]);
            }
            else if (mode == "export")
            {
                Export(app, options["assembly"], options["config"], options["out"]);
            }
            else
            {
                throw new ArgumentException("First argument must be save or export");
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        Dictionary<string, string> options = new Dictionary<string, string>();
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            options[args[i].TrimStart('-')] = args[i + 1];
        }
        return options;
    }

    private static ModelDoc2 Open(SldWorks app, string path)
    {
        int error = 0;
        int warning = 0;
        ModelDoc2 model = app.OpenDoc6(path, 2, 1, "", ref error, ref warning);
        if (model == null || error != 0)
        {
            throw new InvalidOperationException("Could not open " + path + " (error " + error + ")");
        }
        int activateError = 0;
        app.ActivateDoc3(model.GetTitle(), false, 0, ref activateError);
        return model;
    }

    private static void Save(SldWorks app, string path)
    {
        ModelDoc2 model = Open(app, path);
        string title = model.GetTitle();
        try
        {
            AssemblyDoc assembly = (AssemblyDoc)model;
            foreach (KeyValuePair<string, string[]> entry in LinkContents)
            {
                model.ShowConfiguration2(entry.Key);
                // Fetched per configuration: component references go stale when the configuration changes.
                Component2[] components = ((object[])assembly.GetComponents(true)).Cast<Component2>().ToArray();
                Link link = new Link();
                link.Name = "base_link";
                link.Joint.CoordinateSystemName = "Automatically Generate";
                // Link.SWComponents is a generic list of an embedded interop type, which the compiler refuses to
                // use across assemblies, hence the reflection.
                IList componentList = (IList)typeof(Link).GetField("SWComponents").GetValue(link);
                foreach (string part in entry.Value)
                {
                    Component2 component = components.First(c => c.Name2.StartsWith(part + "-"));
                    componentList.Add(component);
                }
                link.SWMainComponent = (Component2)componentList[0];
                CommonSwOperations.SaveSWComponents(model, link);
                LinkNode node = new LinkNode(link);
                node.IsBaseNode = true;
                CommonSwOperations.RetrieveSWComponentPIDs(model, node);
                ConfigurationSerialization.SaveConfigTreeXML(app, model, node, false);
                Console.WriteLine("Saved link tree for " + entry.Key + " (" + entry.Value.Length + " component(s))");
            }
            model.ShowConfiguration2("CFG_BOTH");
            int errors = 0;
            int warnings = 0;
            if (!model.Save3(1, ref errors, ref warnings))
            {
                throw new InvalidOperationException("Save3 failed, errors=" + errors);
            }
        }
        finally
        {
            app.CloseDoc(title);
        }
    }

    private static void Export(SldWorks app, string path, string config, string outputFolder)
    {
        ModelDoc2 model = Open(app, path);
        string title = model.GetTitle();
        try
        {
            model.ShowConfiguration2(config);
            bool error;
            LinkNode tree = ConfigurationSerialization.LoadBaseNodeFromModel(model, out error);
            if (error || tree == null)
            {
                throw new InvalidOperationException("No saved link tree for configuration " + config);
            }
            List<string> problems = new List<string>();
            CommonSwOperations.LoadSWComponents(model, tree, problems);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException("Missing components for: " + string.Join(", ", problems));
            }
            ExportHelper helper = new ExportHelper(app);
            helper.SavePath = outputFolder.TrimEnd('\\') + "\\";
            helper.PackageName = Path.GetFileNameWithoutExtension(path) + "_description_" + config;
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            if (!helper.CreateRobotFromTreeView(tree))
            {
                throw new InvalidOperationException("Building the robot failed for " + config);
            }
            helper.ExportRobot(true, MeshExportFormat.STL);
            Console.WriteLine("Exported " + config + " -> " + helper.PackageName +
                " (" + CommonSwOperations.GetCount(helper.URDFRobot.BaseLink) + " link(s))");
        }
        finally
        {
            app.CloseDoc(title);
        }
    }
}
