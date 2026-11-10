using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Finds per-configuration URDF attributes that match no SolidWorks configuration, for
    /// example after a configuration was renamed. Free of SolidWorks types so it can be unit
    /// tested. Names are compared exactly (ordinal, case sensitive), like the loader does.
    /// </summary>
    public static class OrphanAttributes
    {
        /// <summary>
        /// Attribute names that match no configuration, each reported once, in input order.
        /// </summary>
        public static List<string> FindOrphans(
            IEnumerable<string> attributeNames, IEnumerable<string> configurationNames)
        {
            HashSet<string> configurations = new HashSet<string>(
                configurationNames ?? Enumerable.Empty<string>(), System.StringComparer.Ordinal);
            return (attributeNames ?? Enumerable.Empty<string>())
                .Where(name => !configurations.Contains(name))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Configurations that have no attribute of their own, in input order.
        /// </summary>
        public static List<string> FindConfigurationsWithoutAttribute(
            IEnumerable<string> attributeNames, IEnumerable<string> configurationNames)
        {
            HashSet<string> attributes = new HashSet<string>(
                attributeNames ?? Enumerable.Empty<string>(), System.StringComparer.Ordinal);
            return (configurationNames ?? Enumerable.Empty<string>())
                .Where(name => !attributes.Contains(name))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Log text, or null when there are no orphans.
        /// </summary>
        public static string BuildLogMessage(
            IList<string> orphans, IList<string> configurationsWithoutAttribute)
        {
            if (orphans == null || orphans.Count == 0)
            {
                return null;
            }
            string message = "URDF configuration attributes that match no SolidWorks configuration " +
                "(a configuration may have been renamed): " + string.Join(", ", orphans);
            if (configurationsWithoutAttribute != null && configurationsWithoutAttribute.Count > 0)
            {
                message += ". Configurations without their own URDF attribute: " +
                    string.Join(", ", configurationsWithoutAttribute) +
                    ". Rename the attribute feature to the configuration name to reattach it.";
            }
            return message;
        }
    }
}
