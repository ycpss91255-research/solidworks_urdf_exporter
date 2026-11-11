using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Judges whether a mesh export produced a file, and marks a package whose export did not finish.
    /// SolidWorks' SaveAs reports problems through its return value and error flags only, so the file
    /// itself is checked as well.
    /// </summary>
    public static class MeshExportCheck
    {
        public const string FailureMarkerFileName = "EXPORT_FAILED.txt";

        public static bool IsUsableMesh(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                FileInfo info = new FileInfo(path);
                return info.Exists && info.Length > 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static string JoinLinkNames(IEnumerable<string> links)
        {
            return string.Join(", ", links);
        }

        public static void WriteFailureMarker(string packageDirectory, IEnumerable<string> failedLinks)
        {
            string text = "The mesh export of this package did not finish. The URDF was not written." +
                Environment.NewLine + "Links without a mesh: " + JoinLinkNames(failedLinks) + Environment.NewLine;
            File.WriteAllText(Path.Combine(packageDirectory, FailureMarkerFileName), text, new UTF8Encoding(false));
        }

        public static void RemoveFailureMarker(string packageDirectory)
        {
            string path = Path.Combine(packageDirectory, FailureMarkerFileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
