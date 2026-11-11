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
        /// axes). Together with the configuration entries they all live in the one URDF folder.
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

        public static string WarningText(UiText ui)
        {
            return ui.Get("The exporter settings were stored in the open document but could not be saved with it (it has never been saved, is read-only, or saving failed). Save the document yourself, otherwise the settings are lost when it is closed.");
        }
    }
}
