using SW2URDF.UI;

namespace SW2URDF.URDFExport
{
    /// <summary>Where the exporter settings shown for the active configuration were loaded from.</summary>
    public enum SettingsSource
    {
        /// <summary>The attribute stored for the active SolidWorks configuration.</summary>
        Configuration,
        /// <summary>The legacy attribute shared by all configurations.</summary>
        Shared,
        /// <summary>Nothing stored yet: an empty tree.</summary>
        New
    }

    /// <summary>
    /// Source decision and display text for the active configuration. Free of SolidWorks types so
    /// it can be unit tested.
    /// </summary>
    public static class ConfigurationStatus
    {
        /// <summary>Mirrors the loader: the configuration's own data first, then the shared data.</summary>
        public static SettingsSource Decide(bool hasOwnData, bool hasSharedData)
        {
            if (hasOwnData) return SettingsSource.Configuration;
            return hasSharedData ? SettingsSource.Shared : SettingsSource.New;
        }

        public static string Describe(UiText ui, string configurationName, SettingsSource source)
        {
            string name = string.IsNullOrEmpty(configurationName) ? ui.Get("(none)") : configurationName;
            string detail;
            switch (source)
            {
                case SettingsSource.Configuration:
                    detail = ui.Get("Settings saved for this configuration.");
                    break;
                case SettingsSource.Shared:
                    detail = ui.Get("Settings loaded from the shared (legacy) data; they are saved to this configuration on the next save.");
                    break;
                default:
                    detail = ui.Get("New settings: nothing is saved for this configuration yet.");
                    break;
            }
            return string.Format(ui.Get("Configuration: {0}. {1}"), name, detail);
        }
    }
}
