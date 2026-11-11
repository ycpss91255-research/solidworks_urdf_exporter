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
        public void BeforeOpeningTheExporterAnUnsavedDocumentIsSaved()
        {
            Assert.Equal(SaveBeforeExport.SaveAs, DocumentPersistence.DecideBeforeExport(false, true, true));
            Assert.Equal(SaveBeforeExport.SaveAs, DocumentPersistence.DecideBeforeExport(false, false, false));
            Assert.Equal(SaveBeforeExport.Save, DocumentPersistence.DecideBeforeExport(true, true, false));
            Assert.Equal(SaveBeforeExport.Save, DocumentPersistence.DecideBeforeExport(true, false, true));
            Assert.Equal(SaveBeforeExport.None, DocumentPersistence.DecideBeforeExport(true, false, false));
        }

        [Fact]
        public void MissingConfigurationAttributeIsCreatedBlankOrCopiedFromTheSharedOne()
        {
            Assert.Equal(AttributeInit.Blank, DocumentPersistence.DecideAttribute(false, false));
            Assert.Equal(AttributeInit.CopyShared, DocumentPersistence.DecideAttribute(false, true));
        }

        [Fact]
        public void ExistingConfigurationAttributeIsNeverTouched()
        {
            Assert.Equal(AttributeInit.None, DocumentPersistence.DecideAttribute(true, false));
            Assert.Equal(AttributeInit.None, DocumentPersistence.DecideAttribute(true, true));
        }

        [Fact]
        public void NewModelGetsAnEntryOnlyForTheActiveConfiguration()
        {
            string[] names = { "CFG_BOTH", "CFG_RED_ONLY" };
            Assert.Equal(new[] { "CFG_BOTH" },
                DocumentPersistence.NamesToCreate(names, "CFG_BOTH", new string[0]));
        }

        [Fact]
        public void SwitchingConfigurationAddsOnlyTheNewActiveOne()
        {
            string[] names = { "CFG_BOTH", "CFG_RED_ONLY" };
            Assert.Equal(new[] { "CFG_RED_ONLY" },
                DocumentPersistence.NamesToCreate(names, "CFG_RED_ONLY", new[] { "CFG_BOTH" }));
        }

        [Fact]
        public void ActiveConfigurationThatHasAnEntryCreatesNothing()
        {
            string[] names = { "CFG_BOTH", "CFG_RED_ONLY" };
            Assert.Empty(DocumentPersistence.NamesToCreate(names, "CFG_BOTH", new[] { "CFG_BOTH" }));
            // a deleted folder is rebuilt around the existing entries only: nothing new is created
            Assert.Empty(DocumentPersistence.NamesToCreate(names, "CFG_RED_ONLY", new[] { "CFG_BOTH", "CFG_RED_ONLY" }));
        }

        [Fact]
        public void DeletedFolderShowsOnlyTheActiveConfigurationAgain()
        {
            string[] existing = { "CFG_BOTH", "CFG_RED_ONLY" };
            string[] shown = DocumentPersistence.NamesToGroup(new string[0], "CFG_BOTH", existing);
            Assert.Equal(new[] { "CFG_BOTH" }, shown);
            Assert.True(DocumentPersistence.NeedsRegroup(new string[0], shown, false));
        }

        [Fact]
        public void SwitchingConfigurationShowsTheSecondEntryAndKeepsTheFirst()
        {
            string[] existing = { "CFG_BOTH", "CFG_RED_ONLY" };
            string[] shown = DocumentPersistence.NamesToGroup(new[] { "CFG_BOTH" }, "CFG_RED_ONLY", existing);
            Assert.Equal(new[] { "CFG_BOTH", "CFG_RED_ONLY" }, shown);
            Assert.True(DocumentPersistence.NeedsRegroup(new[] { "CFG_BOTH" }, shown, true));
        }

        [Fact]
        public void NothingToRegroupWhenTheFolderAlreadyShowsTheActiveConfiguration()
        {
            string[] existing = { "CFG_BOTH", "CFG_RED_ONLY" };
            string[] shown = DocumentPersistence.NamesToGroup(new[] { "CFG_BOTH" }, "CFG_BOTH", existing);
            Assert.False(DocumentPersistence.NeedsRegroup(new[] { "CFG_BOTH" }, shown, true));
        }

        [Fact]
        public void HiddenEntriesOfOtherConfigurationsAreNeverGrouped()
        {
            string[] existing = { "CFG_BOTH", "CFG_RED_ONLY" };
            Assert.Empty(DocumentPersistence.NamesToGroup(new string[0], "Other", existing));
        }

        [Theory]
        [InlineData("CoordSys", "Origin_global", true)]
        [InlineData("CoordSys", "Origin_joint1", true)]
        [InlineData("CoordSys", "My coordinate system", false)]
        [InlineData("RefAxis", "Axis_joint1", true)]
        [InlineData("RefAxis", "Axis1", false)]
        [InlineData("3DProfileFeature", "URDF Reference", true)]
        [InlineData("ProfileFeature", "URDF Reference", true)]
        [InlineData("3DProfileFeature", "3DSketch1", false)]
        [InlineData("Attribute", "Origin_x", false)]
        public void OnlyFeaturesTheExporterGeneratesAreRecognised(string type, string name, bool expected)
        {
            Assert.Equal(expected, DocumentPersistence.IsGeneratedUrdfFeature(type, name));
        }

        [Fact]
        public void AllUrdfFeaturesEndUpInOneFolderListWithoutDuplicates()
        {
            string[] all = DocumentPersistence.WithExtras(
                new[] { "CFG_BOTH" }, new[] { "URDF Reference", "Origin_global", "CFG_BOTH", "Axis_j" });
            Assert.Equal(new[] { "CFG_BOTH", "URDF Reference", "Origin_global", "Axis_j" }, all);
        }

        [Fact]
        public void UnknownOrMissingActiveConfigurationCreatesNothing()
        {
            string[] names = { "CFG_BOTH", "CFG_RED_ONLY" };
            Assert.Empty(DocumentPersistence.NamesToCreate(names, null, new string[0]));
            Assert.Empty(DocumentPersistence.NamesToCreate(names, "Other", new string[0]));
            Assert.Empty(DocumentPersistence.NamesToCreate(null, "CFG_BOTH", new string[0]));
        }

        [Fact]
        public void TheFolderIsRebuiltWhenItIsMissingEvenIfNothingWasCreated()
        {
            // The user deleted the feature tree folder: the attributes survive (outside any
            // folder, where they are not shown) and must be grouped into a new folder.
            Assert.True(DocumentPersistence.NeedsGrouping(false, false));
            Assert.True(DocumentPersistence.NeedsGrouping(true, true));
            Assert.True(DocumentPersistence.NeedsGrouping(true, false));
            Assert.False(DocumentPersistence.NeedsGrouping(false, true));
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

        [Fact]
        public void ASaveWithAFailureErrorOrWarningIsAProblem()
        {
            Assert.False(DocumentPersistence.SaveHadProblem(true, 0, 0));
            Assert.True(DocumentPersistence.SaveHadProblem(false, 0, 0));
            Assert.True(DocumentPersistence.SaveHadProblem(true, 1, 0));
            Assert.True(DocumentPersistence.SaveHadProblem(true, 0, 2));
            Assert.True(DocumentPersistence.SaveHadProblem(false, 4, 0));
        }

        [Fact]
        public void SaveProblemTextIsLocalizedAndGivesTheCodes()
        {
            string en = DocumentPersistence.SaveProblemText(new UiText(null), 8, 2);
            Assert.Contains("8", en);
            Assert.Contains("2", en);
            Assert.Contains("Save the document yourself", en);
            string zh = DocumentPersistence.SaveProblemText(new UiText("chinese"), 8, 2);
            Assert.Contains("儲存", zh);
            Assert.Contains("8", zh);
            Assert.DoesNotContain("Save the document", zh);
        }
    }
}
