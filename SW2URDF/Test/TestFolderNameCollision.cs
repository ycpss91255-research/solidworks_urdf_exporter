using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// The URDF folder is named like the oldest shared attribute ("URDF Export Configuration"), and
    /// SolidWorks feature names are unique, so on a migrated model the folder kept the default
    /// name "Folder1" (and a new empty one was added on every run). These tests describe the
    /// feature chain with plain arrays, like a fake document. They do not need SolidWorks.
    /// </summary>
    public class TestFolderNameCollision
    {
        private const string Folder = ConfigurationSerialization.ConfigurationAttributeFolderName;

        private static bool IsOurs(string type, string name)
        {
            return type == "Attribute" || DocumentPersistence.IsGeneratedUrdfFeature(type, name);
        }

        [Fact]
        public void OldSharedAttributeWithTheFolderNameIsRenamed()
        {
            string[] types = { "Reference", "Attribute", "3DProfileFeature", "Attribute" };
            string[] names = { "Part-1", Folder, "URDF Reference", Folder + " (v1.4)" };
            Assert.Equal(FolderNameCollision.RenameLegacyAttribute,
                DocumentPersistence.DecideFolderNameCollision(types, names, Folder));
        }

        [Fact]
        public void UserFeatureWithTheFolderNameBlocksTheFolderAndIsNeverTouched()
        {
            string[] types = { "ProfileFeature" };
            string[] names = { Folder };
            Assert.Equal(FolderNameCollision.Blocked,
                DocumentPersistence.DecideFolderNameCollision(types, names, Folder));
        }

        [Fact]
        public void AFolderWithTheNameIsNotACollision()
        {
            string[] types = { "FtrFolder", "Attribute", "FtrFolder" };
            string[] names = { Folder, "Default", Folder + "___EndTag___" };
            Assert.Equal(FolderNameCollision.None,
                DocumentPersistence.DecideFolderNameCollision(types, names, Folder));
        }

        [Fact]
        public void FreshModelHasNoCollision()
        {
            string[] types = { "Reference", "FtrFolder", "Attribute", "FtrFolder" };
            string[] names = { "Part-1", Folder, "CFG_BOTH", Folder + "___EndTag___" };
            Assert.Equal(FolderNameCollision.None,
                DocumentPersistence.DecideFolderNameCollision(types, names, Folder));
        }

        [Fact]
        public void RenamedLegacyAttributeIsNotACollisionTheSecondTime()
        {
            string[] types = { "Attribute", "Attribute" };
            string[] names = { DocumentPersistence.LegacyAttributeRenamedName, Folder + " (v1.4)" };
            Assert.Equal(FolderNameCollision.None,
                DocumentPersistence.DecideFolderNameCollision(types, names, Folder));
        }

        [Fact]
        public void RenamedLegacyAttributeStaysReadableAsAnOldSharedAttribute()
        {
            Assert.Contains(DocumentPersistence.LegacyAttributeRenamedName,
                ConfigurationFormat.PreviousSharedAttributeNames);
            Assert.NotEqual(Folder, DocumentPersistence.LegacyAttributeRenamedName);
        }

        [Fact]
        public void RenameTargetNeverClashesWithAnExistingFeature()
        {
            string wanted = DocumentPersistence.LegacyAttributeRenamedName;
            Assert.Equal(wanted, DocumentPersistence.FreeName(wanted, new[] { "a" }));
            Assert.Equal(wanted + " 2", DocumentPersistence.FreeName(wanted, new[] { wanted }));
            Assert.Equal(wanted + " 3", DocumentPersistence.FreeName(wanted, new[] { wanted, wanted + " 2" }));
        }

        [Fact]
        public void DefaultNamedFoldersHoldingOnlyExporterFeaturesAreStrays()
        {
            // What the old code left behind: one default-named folder around the next, all around our items
            string[] types = { "Reference", "FtrFolder", "FtrFolder", "Attribute", "CoordSys", "FtrFolder", "FtrFolder" };
            string[] names = { "Part-1", "Folder1", "Folder2", "Default", "Origin_global",
                "Folder2___EndTag___", "Folder1___EndTag___" };
            Assert.Equal(new[] { "Folder1", "Folder2" }, DocumentPersistence.FindStrayFolders(types, names, IsOurs));
        }

        [Fact]
        public void UserFolderWithDefaultNameIsNeverAStray()
        {
            string[] types = { "FtrFolder", "ProfileFeature", "Attribute", "FtrFolder" };
            string[] names = { "Folder1", "Sketch1", "Default", "Folder1___EndTag___" };
            Assert.Empty(DocumentPersistence.FindStrayFolders(types, names, IsOurs));
        }

        [Fact]
        public void EmptyDefaultNamedFolderIsLeftAlone()
        {
            string[] types = { "FtrFolder", "FtrFolder" };
            string[] names = { "Folder1", "Folder1___EndTag___" };
            Assert.Empty(DocumentPersistence.FindStrayFolders(types, names, IsOurs));
        }

        [Fact]
        public void CorrectlyNamedFolderIsNotAStray()
        {
            string[] types = { "FtrFolder", "Attribute", "FtrFolder" };
            string[] names = { Folder, "Default", Folder + "___EndTag___" };
            Assert.Empty(DocumentPersistence.FindStrayFolders(types, names, IsOurs));
        }
    }
}
