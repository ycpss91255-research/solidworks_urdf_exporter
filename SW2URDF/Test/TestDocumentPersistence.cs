using SW2URDF.UI;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// What to do with the document once the exporter settings were written into it (they live in
    /// the open document and are lost when it is closed unsaved). Does not need SolidWorks.
    /// </summary>
    public class TestDocumentPersistence
    {
        [Fact]
        public void NothingWrittenNothingToSave()
        {
            Assert.Equal(PersistAction.None, DocumentPersistence.Decide(false, true, false));
            Assert.Equal(PersistAction.None, DocumentPersistence.Decide(false, false, true));
        }

        [Fact]
        public void WrittenSettingsOfASavedDocumentAreSaved()
        {
            Assert.Equal(PersistAction.Save, DocumentPersistence.Decide(true, true, false));
        }

        [Fact]
        public void NeverSavedDocumentCannotBeSavedSilently()
        {
            Assert.Equal(PersistAction.WarnNotSaved, DocumentPersistence.Decide(true, false, false));
        }

        [Fact]
        public void ReadOnlyDocumentCannotBeSaved()
        {
            Assert.Equal(PersistAction.WarnNotSaved, DocumentPersistence.Decide(true, true, true));
        }

        [Fact]
        public void WarningsAreLocalizedAndTellTheUserToSave()
        {
            Assert.Equal(
                "The exporter settings were stored in the open document but could not be saved with it (it has never been saved, is read-only, or saving failed). Save the document yourself, otherwise the settings are lost when it is closed.",
                DocumentPersistence.WarningText(new UiText(null)));
            string zh = DocumentPersistence.WarningText(new UiText("chinese"));
            Assert.Contains("儲存", zh);
            Assert.DoesNotContain("exporter", zh);
            Assert.NotEqual("Settings not saved", DocumentPersistence.WarningTitle(new UiText("chinese")));
        }
    }
}
