using System.Collections.Generic;

namespace SW2URDF.URDFExport
{
    public enum ConfigurationFormatKind
    {
        None,
        LegacyXml,
        DataContract,
        TooNew
    }

    /// <summary>
    /// Version rules of the URDF export configuration data format stored in SolidWorks
    /// attributes. See CONFIGURATION_FORMAT.md. Free of SolidWorks types so it can be unit tested.
    /// </summary>
    public static class ConfigurationFormat
    {
        /// <summary>
        /// Version written to the "exporterVersion" parameter of every saved configuration. It
        /// versions the data format only and is independent of the exporter release number.
        /// </summary>
        public const double CurrentVersion = 1.4;

        /// <summary>
        /// Data written before this version used the older XmlSerializer scheme.
        /// </summary>
        public const double MinDataContractVersion = 1.3;

        /// <summary>
        /// Name of the shared (all configurations) attribute. It carries a version for historical
        /// reasons and must not be renamed: older models store their data under this name.
        /// </summary>
        public const string SharedAttributeName = "URDF Export Configuration (v1.4)";

        /// <summary>
        /// Shared attribute names used by earlier exporters, read as a fallback.
        /// </summary>
        public static readonly IList<string> PreviousSharedAttributeNames = new List<string>
        {
            "URDF Export Configuration (v1.3)",
            "URDF Export Configuration"
        };

        public static ConfigurationFormatKind Classify(double version)
        {
            if (version <= 0.0)
            {
                return ConfigurationFormatKind.None;
            }
            if (version > CurrentVersion)
            {
                return ConfigurationFormatKind.TooNew;
            }
            return version >= MinDataContractVersion ?
                ConfigurationFormatKind.DataContract : ConfigurationFormatKind.LegacyXml;
        }

        public static bool NeedsUpgrade(double version)
        {
            return version > 0.0 && version < CurrentVersion;
        }
    }
}
