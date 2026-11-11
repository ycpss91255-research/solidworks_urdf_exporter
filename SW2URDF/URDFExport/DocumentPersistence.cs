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

        /// <summary>What to put into the attribute of a configuration when the exporter opens.</summary>
        public static AttributeInit DecideAttribute(bool hasOwnAttribute, bool hasSharedData)
        {
            if (hasOwnAttribute) return AttributeInit.None;
            return hasSharedData ? AttributeInit.CopyShared : AttributeInit.Blank;
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
