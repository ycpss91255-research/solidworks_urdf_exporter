using SW2URDF.Utilities;
using System.Diagnostics;

namespace SW2URDF.Versioning
{
    internal class Version
    {
        private const string DirtySuffix = "-dirty";

        public static string GetCommitVersion()
        {
            // Getting commit version which is attached to the latest git commit
            return FileVersionInfo.GetVersionInfo(typeof(Logger).Assembly.Location).ProductVersion;
        }

        /// <summary>
        /// True when the commit version was built from a working tree with uncommitted changes
        /// (git describe appends "-dirty").
        /// </summary>
        public static bool IsDirty(string commitVersion)
        {
            return !string.IsNullOrEmpty(commitVersion) &&
                commitVersion.EndsWith(DirtySuffix, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The commit version without the "-dirty" marker.
        /// </summary>
        public static string StripDirtySuffix(string commitVersion)
        {
            return IsDirty(commitVersion) ?
                commitVersion.Substring(0, commitVersion.Length - DirtySuffix.Length) : commitVersion;
        }

        public static string GetBuildVersion()
        {
            // Getting AssemblyVersion which is auto incremented for each build. See the AssemblyInfo.cs
            return typeof(Logger).Assembly.GetName().Version.ToString();
        }
    }
}