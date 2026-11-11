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

        public static string Headline(UiText ui, string configurationName)
        {
            string name = string.IsNullOrEmpty(configurationName) ? ui.Get("(none)") : configurationName;
            return string.Format(ui.Get("Active configuration: {0}"), name);
        }

        public static string Detail(UiText ui, SettingsSource source)
        {
            switch (source)
            {
                case SettingsSource.Configuration:
                    return ui.Get("These settings are stored for this configuration.");
                case SettingsSource.Shared:
                    return ui.Get("Using the shared (legacy) settings; they are stored for this configuration on the next save.");
                default:
                    return ui.Get("New: nothing is stored for this configuration yet.");
            }
        }

        /// <summary>Headline and detail on two lines.</summary>
        public static string Describe(UiText ui, string configurationName, SettingsSource source)
        {
            return Headline(ui, configurationName) + "\r\n" + Detail(ui, source);
        }
    }
}
