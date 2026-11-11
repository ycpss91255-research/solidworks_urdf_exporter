using SW2URDF.UI;

namespace SW2URDF.URDFExport
{
    public enum PersistAction
    {
        /// <summary>No settings were written: leave the document alone.</summary>
        None,
        /// <summary>Save the document so the settings survive closing it.</summary>
        Save,
        /// <summary>The document cannot be saved silently: tell the user to save it.</summary>
        WarnNotSaved
    }

    public enum SaveBeforeExport { None, Save, SaveAs }

    public enum AttributeInit { None, Blank, CopyShared }

    public enum FolderNameCollision { None, RenameLegacyAttribute, Blocked }

    /// <summary>
    /// The exporter settings are stored as attributes inside the open SolidWorks document, so they
    /// are lost when the document is closed without saving. Free of SolidWorks types so the
    /// decision and the texts can be unit tested.
    /// </summary>
    public static class DocumentPersistence
    {
        public static PersistAction Decide(bool settingsWritten, bool hasFilePath, bool readOnly)
        {
            if (!settingsWritten) return PersistAction.None;
            return hasFilePath && !readOnly ? PersistAction.Save : PersistAction.WarnNotSaved;
        }

        /// <summary>
        /// Before the exporter opens the document is saved, so what the exporter writes builds on a
        /// saved document: unsaved changes or a pending rebuild are saved, a never saved document
        /// goes through Save As.
        /// </summary>
        public static SaveBeforeExport DecideBeforeExport(bool hasFilePath, bool dirty, bool needsRebuild)
        {
            if (!hasFilePath) return SaveBeforeExport.SaveAs;
            return dirty || needsRebuild ? SaveBeforeExport.Save : SaveBeforeExport.None;
        }

        /// <summary>
        /// Entries are created only for the active configuration, when the user works with it, not
        /// for all configurations at once. Returns the names that need a new entry.
        /// </summary>
        public static string[] NamesToCreate(string[] allConfigurations, string active, string[] existing)
        {
            if (allConfigurations == null || string.IsNullOrEmpty(active) ||
                System.Array.IndexOf(allConfigurations, active) < 0 ||
                existing != null && System.Array.IndexOf(existing, active) >= 0)
            {
                return new string[0];
            }
            return new[] { active };
        }

        /// <summary>
        /// Entries to show in the folder: those already in it, plus the active configuration's entry
        /// when it exists. Entries of other configurations stay hidden until the user works with
        /// that configuration. Ordered like <paramref name="existing"/>.
        /// </summary>
        public static string[] NamesToGroup(string[] inFolder, string active, string[] existing)
        {
            System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>();
            foreach (string name in existing ?? new string[0])
            {
                if (name == active || (inFolder != null && System.Array.IndexOf(inFolder, name) >= 0))
                {
                    result.Add(name);
                }
            }
            return result.ToArray();
        }

        public static bool NeedsRegroup(string[] inFolder, string[] toShow, bool folderExists)
        {
            if (!folderExists) return toShow.Length > 0;
            int shown = inFolder == null ? 0 : inFolder.Length;
            if (shown != toShow.Length) return true;
            foreach (string name in toShow)
            {
                if (inFolder == null || System.Array.IndexOf(inFolder, name) < 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Features the exporter generates (reference sketch, generated coordinate systems and
        /// axes). They stay loose at the feature tree root, never in the URDF folder; found by name wherever they sit.
        /// </summary>
        public static bool IsGeneratedUrdfFeature(string typeName, string name)
        {
            if (name == null) return false;
            switch (typeName)
            {
                case "CoordSys": return name.StartsWith("Origin_");
                case "RefAxis": return name.StartsWith("Axis_");
                case "3DProfileFeature":
                case "ProfileFeature": return name == "URDF Reference";
                default: return false;
            }
        }

        /// <summary>
        /// What the oldest exporter attribute ("URDF Export Configuration") is renamed to. The
        /// feature tree folder takes that name and SolidWorks feature names are unique. Keep it
        /// listed in <see cref="ConfigurationFormat.PreviousSharedAttributeNames"/>.
        /// </summary>
        public const string LegacyAttributeRenamedName = "URDF Export Configuration (legacy)";

        /// <summary>
        /// Another feature already uses the name the URDF folder needs (SolidWorks keeps the
        /// default "Folder1" then). An old exporter attribute is renamed, a user's feature never.
        /// </summary>
        public static FolderNameCollision DecideFolderNameCollision(string[] types, string[] names, string folderName)
        {
            for (int i = 0; names != null && i < names.Length; i++)
            {
                if (names[i] != folderName || types[i] == "FtrFolder") continue;
                return types[i] == "Attribute" ? FolderNameCollision.RenameLegacyAttribute : FolderNameCollision.Blocked;
            }
            return FolderNameCollision.None;
        }

        /// <summary>The wanted name, or the wanted name followed by the first free number from 2.</summary>
        public static string FreeName(string wanted, string[] taken)
        {
            System.Collections.Generic.List<string> used = new System.Collections.Generic.List<string>(taken ?? new string[0]);
            string name = wanted;
            for (int n = 2; used.Contains(name); n++) name = wanted + " " + n;
            return name;
        }

        private static bool IsDefaultFolderName(string name)
        {
            return name != null && System.Text.RegularExpressions.Regex.IsMatch(name, @"^Folder\d+$");
        }

        /// <summary>
        /// Folders named "FolderN" that earlier exporter versions left behind when the rename to the
        /// URDF folder name failed: only the ones holding nothing but exporter features (and other such
        /// folders). A user's folder, or an empty one, is never listed. Deleting a folder keeps its contents.
        /// </summary>
        public static string[] FindStrayFolders(string[] types, string[] names,
            System.Func<string, string, bool> isExporterFeature)
        {
            System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>();
            for (int start = 0; names != null && start < names.Length; start++)
            {
                if (types[start] != "FtrFolder" || !IsDefaultFolderName(names[start])) continue;
                int end = System.Array.IndexOf(names, names[start] + "___EndTag___", start + 1);
                if (end < 0) continue;
                bool ours = false, foreign = false;
                for (int i = start + 1; i < end; i++)
                {
                    if (types[i] == "FtrFolder")
                    {
                        string folder = names[i].EndsWith("___EndTag___") ? names[i].Substring(0, names[i].Length - 12) : names[i];
                        if (!IsDefaultFolderName(folder)) foreign = true;
                    }
                    else if (isExporterFeature(types[i], names[i])) ours = true;
                    else foreign = true;
                }
                if (ours && !foreign) result.Add(names[start]);
            }
            return result.ToArray();
        }

        public static string[] WithExtras(string[] names, string[] extras)
        {
            System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>(names ?? new string[0]);
            foreach (string extra in extras ?? new string[0])
            {
                if (!result.Contains(extra)) result.Add(extra);
            }
            return result.ToArray();
        }

        /// <summary>What to put into the attribute of a configuration when the exporter opens.</summary>
        public static AttributeInit DecideAttribute(bool hasOwnAttribute, bool hasSharedData)
        {
            if (hasOwnAttribute) return AttributeInit.None;
            return hasSharedData ? AttributeInit.CopyShared : AttributeInit.Blank;
        }

        /// <summary>
        /// The attributes are only shown inside the feature tree folder. Group them when new ones
        /// were created, or when the folder is missing (for example deleted by the user).
        /// </summary>
        public static bool NeedsGrouping(bool attributesCreated, bool folderExists)
        {
            return attributesCreated || !folderExists;
        }

        public static string SaveFirstText(UiText ui)
        {
            return ui.Get("The document has to be saved before the exporter can be opened.");
        }

        public static string WarningTitle(UiText ui)
        {
            return ui.Get("Settings not saved");
        }

        /// <summary>
        /// A save that returned false, or reported an error or a warning code, did not go as planned.
        /// The exporter carries on, but tells the user.
        /// </summary>
        public static bool SaveHadProblem(bool saved, int errors, int warnings)
        {
            return !saved || errors != 0 || warnings != 0;
        }

        public static string SaveProblemText(UiText ui, int errors, int warnings)
        {
            return string.Format(ui.Get("Saving the document did not finish cleanly (SolidWorks error code {0}, warning code {1}). The exporter continues, but its settings may not be stored in the file. Save the document yourself, otherwise they can be lost when it is closed."), errors, warnings);
        }

        /// <summary>Shows the warning when a save had a problem; returns whether it did.</summary>
        public static bool ReportSaveProblem(UiText ui, bool saved, int errors, int warnings)
        {
            if (!SaveHadProblem(saved, errors, warnings)) return false;
            URDFPackage.MessageBox.Show(SaveProblemText(ui, errors, warnings), WarningTitle(ui), System.Windows.MessageBoxButton.OK);
            return true;
        }

        public static string WarningText(UiText ui)
        {
            return ui.Get("The exporter settings were stored in the open document but could not be saved with it (it has never been saved, is read-only, or saving failed). Save the document yourself, otherwise the settings are lost when it is closed.");
        }
    }
}
