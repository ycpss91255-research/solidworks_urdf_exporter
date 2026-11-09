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
        public const double CurrentVersion = 0;
        public const double MinDataContractVersion = 0;
        public const string SharedAttributeName = "";
        public static readonly IList<string> PreviousSharedAttributeNames = new List<string>();

        public static ConfigurationFormatKind Classify(double version)
        {
            return ConfigurationFormatKind.None;
        }

        public static bool NeedsUpgrade(double version)
        {
            return false;
        }
    }
}
