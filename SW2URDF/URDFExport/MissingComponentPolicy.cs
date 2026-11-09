using System.Collections.Generic;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Pure decisions about links whose SolidWorks components could not all be loaded, for
    /// example because the active configuration suppresses a part. Kept free of SolidWorks
    /// types so it can be unit tested.
    /// </summary>
    public static class MissingComponentPolicy
    {
        public static bool IsLinkIncomplete(int expectedCount, int loadedCount)
        {
            return false;
        }

        public static bool MustConfirmExport(IList<string> problemLinks)
        {
            return false;
        }

        public static string BuildMessage(
            IList<string> problemLinks, string configurationName, bool exporting)
        {
            return null;
        }
    }
}
