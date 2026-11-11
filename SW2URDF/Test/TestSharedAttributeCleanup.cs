using System.Collections.Generic;
using System.Linq;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Removal of the old shared attributes once every configuration has its own entry. Uses a
    /// fake document, so it does not need SolidWorks.
    /// </summary>
    public class TestSharedAttributeCleanup
    {
        private sealed class FakeDocument : ISharedAttributeDocument
        {
            public string[] Configurations = { "Default" };
            public Dictionary<string, AttributeData> Entries = new Dictionary<string, AttributeData>();
            public AttributeData Shared;
            public bool DeleteWorks = true;
            public int Deletions;

            public string[] ConfigurationNames() { return Configurations; }
            public bool HasEntry(string configuration) { return Entries.ContainsKey(configuration); }
            public AttributeData GetEntry(string configuration)
            {
                return Entries.TryGetValue(configuration, out AttributeData data) ? data : null;
            }
            public AttributeData GetSharedFallback() { return Shared; }
            public bool DeleteSharedAttributes()
            {
                Deletions++;
                if (DeleteWorks) Shared = null;
                return DeleteWorks;
            }
        }

        // The fake "tree" is the link names; whitespace and the format version do not matter,
        // "BAD" cannot be read
        private static string Canonical(string data, double version)
        {
            return data == "BAD" ? null : string.Join("|", data.Split(new[] { ' ', ',', '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        private static SharedCleanupResult Run(FakeDocument document, List<string> log = null)
        {
            return SharedAttributeCleanup.Run(document, Canonical, m => (log ?? new List<string>()).Add(m));
        }

        [Fact]
        public void OneConfigurationWithItsOwnCopyRemovesTheSharedAttributes()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base, arm", 1.4) };
            document.Entries["Default"] = new AttributeData("base, arm", 1.4);
            Assert.Equal(SharedCleanupResult.Removed, Run(document));
            Assert.Null(document.Shared);
        }

        [Fact]
        public void ComparisonIsOnTheTreesNotTheStrings()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base,arm", 1.0) };
            document.Entries["Default"] = new AttributeData("base  arm\n", 1.4);
            Assert.Equal(SharedCleanupResult.Removed, Run(document));
        }

        [Fact]
        public void ConfigurationWithoutEntryKeepsEverything()
        {
            FakeDocument document = new FakeDocument { Configurations = new[] { "A", "B" }, Shared = new AttributeData("base", 1.4) };
            document.Entries["A"] = new AttributeData("base", 1.4);
            List<string> log = new List<string>();
            Assert.Equal(SharedCleanupResult.KeptMissingEntry, Run(document, log));
            Assert.NotNull(document.Shared);
            Assert.Equal(0, document.Deletions);
            Assert.Contains("\"B\"", log.Single());
        }

        [Fact]
        public void EntryWithoutDataCountsAsMissing()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base", 1.4) };
            document.Entries["Default"] = new AttributeData("", 1.4);
            Assert.Equal(SharedCleanupResult.KeptMissingEntry, Run(document));
            Assert.NotNull(document.Shared);
        }

        [Fact]
        public void TwoConfigurationsWithTheirOwnCopiesRemoveTheSharedAttributes()
        {
            FakeDocument document = new FakeDocument { Configurations = new[] { "A", "B" }, Shared = new AttributeData("base", 1.4) };
            document.Entries["A"] = new AttributeData("base", 1.4);
            document.Entries["B"] = new AttributeData("base", 1.4);
            Assert.Equal(SharedCleanupResult.Removed, Run(document));
        }

        [Fact]
        public void DifferingContentKeepsEverything()
        {
            FakeDocument document = new FakeDocument { Configurations = new[] { "A", "B" }, Shared = new AttributeData("base", 1.4) };
            document.Entries["A"] = new AttributeData("base", 1.4);
            document.Entries["B"] = new AttributeData("base, arm", 1.4);
            List<string> log = new List<string>();
            Assert.Equal(SharedCleanupResult.KeptDiffers, Run(document, log));
            Assert.NotNull(document.Shared);
            Assert.Contains("\"B\"", log.Single());
        }

        [Fact]
        public void UnparsableSharedAttributeKeepsEverything()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("BAD", 1.4) };
            document.Entries["Default"] = new AttributeData("base", 1.4);
            Assert.Equal(SharedCleanupResult.KeptUnparsable, Run(document));
            Assert.NotNull(document.Shared);
        }

        [Fact]
        public void UnparsableEntryKeepsEverything()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base", 1.4) };
            document.Entries["Default"] = new AttributeData("BAD", 1.4);
            Assert.Equal(SharedCleanupResult.KeptUnparsable, Run(document));
            Assert.NotNull(document.Shared);
        }

        [Fact]
        public void FreshModelWithoutSharedAttributesChangesNothing()
        {
            FakeDocument document = new FakeDocument();
            document.Entries["Default"] = new AttributeData("base", 1.4);
            Assert.Equal(SharedCleanupResult.NothingToRemove, Run(document));
            Assert.Equal(0, document.Deletions);
        }

        [Fact]
        public void SecondRunDoesNothing()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base", 1.4) };
            document.Entries["Default"] = new AttributeData("base", 1.4);
            Run(document);
            Assert.Equal(SharedCleanupResult.NothingToRemove, Run(document));
            Assert.Equal(1, document.Deletions);
        }

        [Fact]
        public void FailedDeletionIsReportedAndNothingIsClaimedRemoved()
        {
            FakeDocument document = new FakeDocument { Shared = new AttributeData("base", 1.4), DeleteWorks = false };
            document.Entries["Default"] = new AttributeData("base", 1.4);
            Assert.Equal(SharedCleanupResult.KeptDeleteFailed, Run(document));
        }
    }
}
