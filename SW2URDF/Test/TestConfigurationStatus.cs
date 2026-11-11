using SW2URDF.UI;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Which storage the exporter settings came from, and the text that tells the user.
    /// Does not need SolidWorks.
    /// </summary>
    public class TestConfigurationStatus
    {
        [Fact]
        public void OwnDataWinsOverSharedData()
        {
            Assert.Equal(SettingsSource.Configuration, ConfigurationStatus.Decide(true, true));
        }

        [Fact]
        public void SharedDataIsUsedWhenTheConfigurationHasNone()
        {
            Assert.Equal(SettingsSource.Shared, ConfigurationStatus.Decide(false, true));
        }

        [Fact]
        public void NothingStoredMeansNew()
        {
            Assert.Equal(SettingsSource.New, ConfigurationStatus.Decide(false, false));
        }

        [Fact]
        public void EnglishTextNamesTheConfigurationAndTheSource()
        {
            UiText ui = new UiText(null);
            Assert.Equal("Active configuration: Wide",
                ConfigurationStatus.Headline(ui, "Wide"));
            Assert.Equal("These settings are stored for this configuration.",
                ConfigurationStatus.Detail(ui, SettingsSource.Configuration));
            Assert.Equal("Using the shared (legacy) settings; they are stored for this configuration on the next save.",
                ConfigurationStatus.Detail(ui, SettingsSource.Shared));
            Assert.Equal("New: nothing is stored for this configuration yet.",
                ConfigurationStatus.Detail(ui, SettingsSource.New));
            Assert.Equal("Active configuration: Wide\r\nNew: nothing is stored for this configuration yet.",
                ConfigurationStatus.Describe(ui, "Wide", SettingsSource.New));
        }

        [Fact]
        public void ChineseTextIsLocalizedAndKeepsTheConfigurationName()
        {
            UiText ui = new UiText("chinese");
            foreach (SettingsSource source in new[] { SettingsSource.Configuration, SettingsSource.Shared, SettingsSource.New })
            {
                string text = ConfigurationStatus.Describe(ui, "寬版", source);
                Assert.Contains("寬版", text);
                Assert.DoesNotContain("settings", text);
                Assert.DoesNotContain("configuration", text);
                Assert.DoesNotContain("Active", text);
            }
        }

        [Fact]
        public void MissingConfigurationNameStillProducesText()
        {
            Assert.Equal("Active configuration: (none)",
                ConfigurationStatus.Headline(new UiText(null), null));
            Assert.DoesNotContain("(none)",
                ConfigurationStatus.Headline(new UiText("chinese"), ""));
        }
    }
}
