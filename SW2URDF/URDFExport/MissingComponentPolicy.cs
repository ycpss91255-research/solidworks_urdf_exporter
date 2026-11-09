using System.Collections.Generic;
using System.Text;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Pure decisions about links whose SolidWorks components could not all be loaded, for
    /// example because the active configuration suppresses a part. Kept free of SolidWorks
    /// types so it can be unit tested.
    /// </summary>
    public static class MissingComponentPolicy
    {
        /// <summary>
        /// A link is incomplete when fewer components were found than were saved for it.
        /// </summary>
        public static bool IsLinkIncomplete(int expectedCount, int loadedCount)
        {
            return loadedCount < expectedCount;
        }

        /// <summary>
        /// Exporting with links that lost components yields a partial robot, so the user has
        /// to confirm it explicitly.
        /// </summary>
        public static bool MustConfirmExport(IList<string> problemLinks)
        {
            return problemLinks != null && problemLinks.Count > 0;
        }

        /// <summary>
        /// Text shown to the user, or null when no link is missing components.
        /// </summary>
        /// <param name="problemLinks">Names of the links that are missing components</param>
        /// <param name="configurationName">Active SolidWorks configuration, may be null</param>
        /// <param name="exporting">True when asking to continue an export, false when
        /// just reporting the problem while loading</param>
        public static string BuildMessage(
            IList<string> problemLinks, string configurationName, bool exporting)
        {
            if (!MustConfirmExport(problemLinks))
            {
                return null;
            }

            StringBuilder message = new StringBuilder();
            message.Append("The following links are missing SolidWorks components");
            if (!string.IsNullOrEmpty(configurationName))
            {
                message.Append(" in the active configuration \"" + configurationName + "\"");
            }
            message.Append(" (for example because the configuration suppresses a part):\r\n\r\n");
            message.Append(string.Join(", ", problemLinks));
            message.Append("\r\n\r\n");
            if (exporting)
            {
                message.Append("The exported robot will be incomplete (missing geometry, mass and " +
                    "inertia). Export anyway?");
            }
            else
            {
                message.Append("Please inspect these links before exporting, or switch to the " +
                    "configuration this URDF configuration was saved from.");
            }
            return message.ToString();
        }
    }
}
