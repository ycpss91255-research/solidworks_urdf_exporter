using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Turns names that come from the model (link names, document titles, texture files) into names that are
    /// safe as Windows file and directory names. The names inside the URDF stay as the user wrote them; only
    /// the names of files and folders go through here.
    /// </summary>
    public static class FileNames
    {
        public const int MaxFileNameLength = 100;
        private const string DefaultFallback = "link";

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static bool IsReservedDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ');
            return Array.Exists(ReservedNames, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));
        }

        public static string SanitizeFileName(string name, string fallback = DefaultFallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(c < ' ' || Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' ? '_' : c);
            }
            string result = Trim(builder.ToString());
            if (result.Length > MaxFileNameLength) result = Trim(result.Substring(0, MaxFileNameLength));
            if (result.Length == 0) return fallback;
            if (IsReservedDeviceName(result)) result = "_" + result;
            return result;
        }

        private static string Trim(string value)
        {
            return value.TrimStart(' ').TrimEnd(' ', '.');
        }

        /// <summary>Sanitized name that is not yet in <paramref name="used"/>; the result is added to it.
        /// Use a case-insensitive set: Windows file names are.</summary>
        public static string UniqueFileName(string name, ISet<string> used, string fallback = DefaultFallback)
        {
            string baseName = SanitizeFileName(name, fallback);
            string candidate = baseName;
            for (int i = 2; !used.Add(candidate); i++)
            {
                candidate = baseName + "_" + i;
            }
            return candidate;
        }

        /// <summary>Like <see cref="UniqueFileName"/> for a name with an extension such as a texture file.</summary>
        public static string UniqueFileNameWithExtension(string fileName, ISet<string> used)
        {
            string clean = SanitizeFileName(fileName, "file");
            string extension = Path.GetExtension(clean);
            string stem = clean.Substring(0, clean.Length - extension.Length);
            if (stem.Length == 0) { stem = "file"; }
            string candidate = stem + extension;
            for (int i = 2; !used.Add(candidate); i++)
            {
                candidate = stem + "_" + i + extension;
            }
            return candidate;
        }

        /// <summary>
        /// Package name from a document title: without the shown .SLDASM/.SLDPRT extension and with every
        /// character that is awkward in a package:// URI, a CMake project name or a file name replaced.
        /// </summary>
        public static string SanitizePackageName(string title)
        {
            if (string.IsNullOrEmpty(title)) return "package";
            foreach (string extension in new[] { ".SLDASM", ".SLDPRT" })
            {
                if (title.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    title = title.Substring(0, title.Length - extension.Length);
                    break;
                }
            }
            StringBuilder builder = new StringBuilder(title.Length);
            foreach (char c in title)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            }
            return SanitizeFileName(builder.ToString(), "package");
        }
    }
}
