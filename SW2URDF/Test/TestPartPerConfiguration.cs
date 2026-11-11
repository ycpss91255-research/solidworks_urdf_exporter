using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// The part exporter keeps its settings per SolidWorks configuration, like the assembly
    /// exporter. Works on a temporary copy of BOX_BLUE (configurations Short and Tall).
    /// </summary>
    [Collection("Requires SW Test Collection")]
    public class TestPartPerConfiguration : SW2URDFTest
    {
        public TestPartPerConfiguration(SWTestFixture fixture) : base(fixture)
        {
        }

        private ModelDoc2 OpenCopy(out string directory)
        {
            Assert.True(SwApp.CloseAllDocuments(true));
            directory = CreateRandomTempDirectory();
            string path = Path.Combine(directory, TwoBoxesModel.PartBlue + ".SLDPRT");
            File.Copy(Path.Combine(GetModelDirectory(TwoBoxesModel.Name), TwoBoxesModel.PartBlue + ".SLDPRT"), path);
            return OpenPart(path);
        }

        private ModelDoc2 OpenPart(string path)
        {
            int errors = 0;
            int warnings = 0;
            ModelDoc2 doc = SwApp.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            Assert.NotNull(doc);
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

        /// <summary>Builds the link for the active configuration, then loads saved settings.</summary>
        private ExportHelper Open(out bool loaded)
        {
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            loaded = helper.LoadPartSettings();
            return helper;
        }

        private double RedOfActiveConfiguration()
        {
            return Open(out bool _).URDFRobot.BaseLink.Visual.Material.Color.Red;
        }

        private void SaveWithRed(double red)
        {
            ExportHelper helper = Open(out bool _);
            helper.URDFRobot.BaseLink.Visual.Material.Color.Red = red;
            helper.SavePartSettings();
        }

        [Fact]
        public void ConfigurationsKeepTheirOwnPartSettings()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.BlueShort);
                SaveWithRed(0.25);
                Show(doc, TwoBoxesModel.BlueTall);
                ExportHelper tall = Open(out bool tallLoaded);
                Assert.False(tallLoaded);
                Assert.NotEqual(0.25, tall.URDFRobot.BaseLink.Visual.Material.Color.Red);
                SaveWithRed(0.75);

                Show(doc, TwoBoxesModel.BlueShort);
                ExportHelper shortHelper = Open(out bool shortLoaded);
                Assert.True(shortLoaded);
                Assert.Equal(0.25, shortHelper.URDFRobot.BaseLink.Visual.Material.Color.Red);
                Show(doc, TwoBoxesModel.BlueTall);
                Assert.Equal(0.75, RedOfActiveConfiguration());
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void ConfigurationWithoutOwnSettingsFallsBackToTheSharedAttribute()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.BlueShort);
                ExportHelper helper = Open(out bool _);
                helper.URDFRobot.BaseLink.Visual.Material.Color.Red = 0.5;
                WriteSharedAttribute(doc, new LinkNode(helper.URDFRobot.BaseLink));

                Show(doc, TwoBoxesModel.BlueTall);
                ExportHelper tall = Open(out bool loaded);
                Assert.True(loaded);
                Assert.Equal(0.5, tall.URDFRobot.BaseLink.Visual.Material.Color.Red);
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void PartSettingsSurviveSaveCloseAndReopen()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.BlueShort);
                SaveWithRed(0.25);
                Show(doc, TwoBoxesModel.BlueTall);
                SaveWithRed(0.75);
                string path = doc.GetPathName();
                int errors = 0;
                int warnings = 0;
                Assert.True(doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings));
                Assert.True(SwApp.CloseAllDocuments(true));

                doc = OpenPart(path);
                Show(doc, TwoBoxesModel.BlueShort);
                Assert.Equal(0.25, RedOfActiveConfiguration());
                Show(doc, TwoBoxesModel.BlueTall);
                Assert.Equal(0.75, RedOfActiveConfiguration());
            }
            finally
            {
                Cleanup(directory);
            }
        }

        [Fact]
        public void FormShowsTheSettingsOfTheActiveConfigurationAndFollowsSwitches()
        {
            ModelDoc2 doc = OpenCopy(out string directory);
            try
            {
                Show(doc, TwoBoxesModel.BlueShort);
                SaveWithRed(0.25);
                Show(doc, TwoBoxesModel.BlueTall);
                SaveWithRed(0.75);
                Show(doc, TwoBoxesModel.BlueShort);

                using (PartExportForm form = new PartExportForm(SwApp))
                {
                    BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    typeof(PartExportForm).GetMethod("PartExportFormLoad", flags)
                        .Invoke(form, new object[] { null, EventArgs.Empty });
                    DomainUpDown red = (DomainUpDown)typeof(PartExportForm)
                        .GetField("domainUpDown_red", flags).GetValue(form);
                    MethodInfo activated = typeof(PartExportForm).GetMethod("OnActivated", flags);

                    Assert.Equal(0.25, double.Parse(red.Text, CultureInfo.CurrentCulture));
                    Show(doc, TwoBoxesModel.BlueTall);
                    activated.Invoke(form, new object[] { EventArgs.Empty });
                    Assert.Equal(0.75, double.Parse(red.Text, CultureInfo.CurrentCulture));
                    Show(doc, TwoBoxesModel.BlueShort);
                    activated.Invoke(form, new object[] { EventArgs.Empty });
                    Assert.Equal(0.25, double.Parse(red.Text, CultureInfo.CurrentCulture));
                }
            }
            finally
            {
                Cleanup(directory);
            }
        }

        private void WriteSharedAttribute(ModelDoc2 doc, LinkNode node)
        {
            Type type = typeof(ConfigurationSerialization);
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            string data = (string)type.GetMethod("SerializeToString", flags).Invoke(null, new object[] { node });
            var attribute = (SolidWorks.Interop.sldworks.Attribute)type.GetMethod("CreateSWSaveAttribute", flags)
                .Invoke(null, new object[] { SwApp, doc, ConfigurationSerialization.UrdfConfigurationSwAttributeName });
            int all = (int)swInConfigurationOpts_e.swAllConfiguration;
            attribute.GetParameter("data").SetStringValue2(data, all, "");
            attribute.GetParameter("name").SetStringValue2("config1", all, "");
            attribute.GetParameter("date").SetStringValue2(DateTime.Now.ToString(), all, "");
            attribute.GetParameter("exporterVersion").SetDoubleValue2(1.4, all, "");
        }
    }
}
