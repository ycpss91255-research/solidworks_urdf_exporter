using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Characterization tests for storing the URDF export configuration once per SolidWorks
    /// configuration. They work on a temporary copy of the TWO_BOXES example (see TESTING.md):
    /// configuration CFG_BOTH has both boxes, CFG_RED_ONLY suppresses the blue one.
    /// </summary>
    [Collection("Requires SW Test Collection")]
    public class TestPerConfiguration : SW2URDFTest
    {
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

        public TestPerConfiguration(SWTestFixture fixture) : base(fixture)
        {
        }

        #region Helpers

        /// <summary>
        /// Copies the TWO_BOXES example to a temporary directory and opens the copy.
        /// </summary>
        private ModelDoc2 OpenCopy(out string directory)
        {
            Assert.True(SwApp.CloseAllDocuments(true));
            directory = CreateRandomTempDirectory();
            foreach (string file in Directory.GetFiles(GetModelDirectory(TwoBoxesModel.Name)))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }
            return OpenAssembly(directory);
        }

        private ModelDoc2 OpenAssembly(string directory)
        {
            string path = Path.Combine(directory, TwoBoxesModel.Name + ".SLDASM");
            int errors = 0;
            int warnings = 0;
            ModelDoc2 doc = SwApp.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            Assert.NotNull(doc);
            Assert.Equal(0, errors);
            return doc;
        }

        private void Cleanup(string directory)
        {
            SwApp.CloseAllDocuments(true);
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }

        private static void Show(ModelDoc2 doc, string configuration)
        {
            doc.ShowConfiguration2(configuration);
            Assert.Equal(configuration, doc.ConfigurationManager.ActiveConfiguration.Name);
        }

        /// <summary>
        /// A single link holding the components whose name contains one of the given parts.
        /// </summary>
        private static LinkNode BuildTree(ModelDoc2 doc, string linkName, params string[] partNames)
        {
            Link link = new Link { Name = linkName };
            link.Joint.CoordinateSystemName = "Automatically Generate";
            link.SWComponents = ((object[])((AssemblyDoc)doc).GetComponents(true)).Cast<Component2>()
                .Where(c => partNames.Any(p => c.Name2.Contains(p))).ToList();
            Assert.Equal(partNames.Length, link.SWComponents.Count);
            LinkNode node = new LinkNode(link);
            CommonSwOperations.RetrieveSWComponentPIDs(doc, node);
            return node;
        }

        private LinkNode Load(ModelDoc2 doc)
        {
            LinkNode node = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            return node;
        }

        private void Save(ModelDoc2 doc, LinkNode node)
        {
            ConfigurationSerialization.SaveConfigTreeXML(SwApp, doc, node, false);
        }

        /// <summary>
        /// Writes the shared (all configurations) attribute the way earlier exporters did.
        /// </summary>
        private void WriteSharedAttribute(ModelDoc2 doc, LinkNode node)
        {
            Type type = typeof(ConfigurationSerialization);
            string data = (string)type.GetMethod("SerializeToString", NonPublicStatic)
                .Invoke(null, new object[] { node });
            Assert.NotEmpty(data);
            var attribute = (SolidWorks.Interop.sldworks.Attribute)type
                .GetMethod("CreateSWSaveAttribute", NonPublicStatic)
                .Invoke(null, new object[] { SwApp, doc, ConfigurationSerialization.UrdfConfigurationSwAttributeName });
            int all = (int)swInConfigurationOpts_e.swAllConfiguration;
            attribute.GetParameter("data").SetStringValue2(data, all, "");
            attribute.GetParameter("name").SetStringValue2("config1", all, "");
            attribute.GetParameter("date").SetStringValue2(DateTime.Now.ToString(), all, "");
            attribute.GetParameter("exporterVersion").SetDoubleValue2(1.4, all, "");
        }

        /// <summary>
        /// Names of the per-configuration attributes (those with data and exporterVersion
        /// parameters, other than the shared attribute), walking the whole feature chain.
        /// </summary>
        private static List<string> ConfigurationAttributeNames(ModelDoc2 doc)
        {
            List<string> names = new List<string>();
            Feature feature = doc.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "Attribute")
                {
                    var attribute = (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (attribute.GetParameter("data") != null &&
                        attribute.GetParameter("exporterVersion") != null &&
                        attribute.GetName() != ConfigurationSerialization.UrdfConfigurationSwAttributeName)
                    {
                        names.Add(attribute.GetName());
                    }
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return names;
        }

        private static List<string> ProblemLinks(ModelDoc2 doc)
        {
            LinkNode node = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            List<string> problems = new List<string>();
            CommonSwOperations.LoadSWComponents(doc, node, problems);
            return problems;
        }

        #endregion Helpers

        [Fact]
        public void ConfigurationsStoreAndReadBackTheirOwnTree()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                Save(doc, BuildTree(doc, "link_both", TwoBoxesModel.PartRed, TwoBoxesModel.PartBlue));
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Save(doc, BuildTree(doc, "link_red", TwoBoxesModel.PartRed));

                Show(doc, TwoBoxesModel.ConfigBoth);
                Assert.Equal("link_both", Load(doc).Link.Name);
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Assert.Equal("link_red", Load(doc).Link.Name);
                Show(doc, TwoBoxesModel.ConfigBoth);
                Assert.Equal("link_both", Load(doc).Link.Name);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void ConfigurationWithoutOwnAttributeFallsBackToSharedAttribute()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                WriteSharedAttribute(doc, BuildTree(doc, "link_shared", TwoBoxesModel.PartRed));
                Save(doc, BuildTree(doc, "link_own", TwoBoxesModel.PartRed));

                Show(doc, TwoBoxesModel.ConfigBoth);
                Assert.Equal("link_own", Load(doc).Link.Name);
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Assert.Equal("link_shared", Load(doc).Link.Name);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void ModelWithOnlyTheSharedAttributeBehavesAsBefore()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                WriteSharedAttribute(doc, BuildTree(doc, "link_shared", TwoBoxesModel.PartRed));
                Assert.Empty(ConfigurationAttributeNames(doc));

                Show(doc, TwoBoxesModel.ConfigBoth);
                Assert.Equal("link_shared", Load(doc).Link.Name);
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Assert.Equal("link_shared", Load(doc).Link.Name);
                Assert.Empty(ConfigurationAttributeNames(doc));
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void SavingWritesOnlyTheActiveConfigurationAndKeepsTheSharedAttribute()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                WriteSharedAttribute(doc, BuildTree(doc, "link_shared", TwoBoxesModel.PartRed));
                Save(doc, BuildTree(doc, "link_own", TwoBoxesModel.PartRed));

                Assert.Equal(new[] { TwoBoxesModel.ConfigBoth }, ConfigurationAttributeNames(doc));
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Assert.Equal("link_shared", Load(doc).Link.Name);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void PerConfigurationTreesSurviveSaveCloseAndReopen()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                Save(doc, BuildTree(doc, "link_both", TwoBoxesModel.PartRed, TwoBoxesModel.PartBlue));
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Save(doc, BuildTree(doc, "link_red", TwoBoxesModel.PartRed));
                Show(doc, TwoBoxesModel.ConfigBoth);

                int errors = 0;
                int warnings = 0;
                Assert.True(doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings));
                Assert.True(SwApp.CloseAllDocuments(true));

                doc = OpenAssembly(directory);
                Show(doc, TwoBoxesModel.ConfigBoth);
                Assert.Equal("link_both", Load(doc).Link.Name);
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Assert.Equal("link_red", Load(doc).Link.Name);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void FeatureTreeFolderHoldsAttributesNamedAfterTheConfigurations()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                Save(doc, BuildTree(doc, "link_both", TwoBoxesModel.PartRed, TwoBoxesModel.PartBlue));
                Show(doc, TwoBoxesModel.ConfigRedOnly);
                Save(doc, BuildTree(doc, "link_red", TwoBoxesModel.PartRed));

                Feature folder = null;
                Feature feature = doc.FirstFeature() as Feature;
                while (feature != null)
                {
                    if (feature.Name == ConfigurationSerialization.ConfigurationAttributeFolderName &&
                        feature.GetTypeName2().Contains("Folder"))
                    {
                        folder = feature;
                        break;
                    }
                    feature = feature.GetNextFeature() as Feature;
                }
                Assert.NotNull(folder);

                List<string> inFolder = new List<string>();
                FeatureFolder featureFolder = (FeatureFolder)folder.GetSpecificFeature2();
                Assert.NotNull(featureFolder);
                object[] contents = (object[])featureFolder.GetFeatures();
                Assert.NotNull(contents);
                foreach (object child in contents)
                {
                    inFolder.Add(((Feature)child).Name);
                }
                string[] configurations = (string[])doc.GetConfigurationNames();
                Assert.Equal(configurations.OrderBy(n => n), inFolder.OrderBy(n => n));
                Assert.Equal(configurations.OrderBy(n => n), ConfigurationAttributeNames(doc).OrderBy(n => n));
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void SuppressedPartIsReportedAsMissingOnlyInTheConfigurationThatSuppressesIt()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                WriteSharedAttribute(doc, BuildTree(doc, "link_all", TwoBoxesModel.PartRed, TwoBoxesModel.PartBlue));

                Show(doc, TwoBoxesModel.ConfigBoth);
                List<string> both = ProblemLinks(doc);
                Assert.Empty(both);
                Assert.False(MissingComponentPolicy.MustConfirmExport(both));

                Show(doc, TwoBoxesModel.ConfigRedOnly);
                List<string> redOnly = ProblemLinks(doc);
                Assert.Equal(new[] { "link_all" }, redOnly);
                Assert.True(MissingComponentPolicy.MustConfirmExport(redOnly));
                string message = MissingComponentPolicy.BuildMessage(redOnly, TwoBoxesModel.ConfigRedOnly, true);
                Assert.Contains("link_all", message);
                Assert.Contains(TwoBoxesModel.ConfigRedOnly, message);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void RenamingAConfigurationLeavesItsAttributeDetectedAsOrphan()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.ConfigBoth);
                Save(doc, BuildTree(doc, "link_both", TwoBoxesModel.PartRed, TwoBoxesModel.PartBlue));
                const string renamed = "CFG_RENAMED";
                doc.GetConfigurationByName(TwoBoxesModel.ConfigBoth).Name = renamed;

                string[] configurations = (string[])doc.GetConfigurationNames();
                Assert.Contains(renamed, configurations);
                List<string> attributes = ConfigurationAttributeNames(doc);
                List<string> orphans = OrphanAttributes.FindOrphans(attributes, configurations);
                Assert.Equal(new[] { TwoBoxesModel.ConfigBoth }, orphans);
                Assert.Contains(renamed,
                    OrphanAttributes.FindConfigurationsWithoutAttribute(attributes, configurations));

                // Detection only: loading must not be blocked and must not throw
                Show(doc, renamed);
                ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
                Assert.False(error);
            }
            finally
            {
                Cleanup(directory);
            }
        }
    }
}
