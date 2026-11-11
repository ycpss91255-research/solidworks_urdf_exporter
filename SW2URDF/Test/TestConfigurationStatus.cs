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
            Assert.Equal("Configuration: Wide. Settings saved for this configuration.",
                ConfigurationStatus.Describe(ui, "Wide", SettingsSource.Configuration));
            Assert.Equal("Configuration: Wide. Settings loaded from the shared (legacy) data; they are saved to this configuration on the next save.",
                ConfigurationStatus.Describe(ui, "Wide", SettingsSource.Shared));
            Assert.Equal("Configuration: Wide. New settings: nothing is saved for this configuration yet.",
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
                Assert.DoesNotContain("Settings", text);
                Assert.DoesNotContain("Configuration", text);
            }
        }

        [Fact]
        public void MissingConfigurationNameStillProducesText()
        {
            Assert.Equal("Configuration: (none). New settings: nothing is saved for this configuration yet.",
                ConfigurationStatus.Describe(new UiText(null), null, SettingsSource.New));
            Assert.DoesNotContain("(none)",
                ConfigurationStatus.Describe(new UiText("chinese"), "", SettingsSource.New));
        }
    }
}
