using System.Collections.Generic;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Finds per-configuration URDF attributes that match no SolidWorks configuration, for
    /// example after a configuration was renamed. Free of SolidWorks types so it can be unit
    /// tested.
    /// </summary>
    public static class OrphanAttributes
    {
        public static List<string> FindOrphans(
            IEnumerable<string> attributeNames, IEnumerable<string> configurationNames)
        {
            return new List<string>();
        }

        public static List<string> FindConfigurationsWithoutAttribute(
            IEnumerable<string> attributeNames, IEnumerable<string> configurationNames)
        {
            return new List<string>();
        }

        public static string BuildLogMessage(
            IList<string> orphans, IList<string> configurationsWithoutAttribute)
        {
            return null;
        }
    }
}
