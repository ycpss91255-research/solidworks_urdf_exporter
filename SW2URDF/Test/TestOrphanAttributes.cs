using SW2URDF.URDFExport;
using System.Collections.Generic;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks, so it has no test fixture.
    /// </summary>
    public class TestOrphanAttributes
    {
        private static readonly string[] Configs = { "Default", "Wide" };

        [Fact]
        public void NoOrphansWhenEveryAttributeMatchesAConfiguration()
        {
            Assert.Empty(OrphanAttributes.FindOrphans(new[] { "Default", "Wide" }, Configs));
        }

        [Fact]
        public void RenamedConfigurationLeavesItsAttributeOrphaned()
        {
            List<string> orphans = OrphanAttributes.FindOrphans(new[] { "Default", "Narrow" }, Configs);
            Assert.Equal(new[] { "Narrow" }, orphans);
            List<string> without = OrphanAttributes.FindConfigurationsWithoutAttribute(
                new[] { "Default", "Narrow" }, Configs);
            Assert.Equal(new[] { "Wide" }, without);
        }

        [Fact]
        public void MatchingIsExactAndCaseSensitive()
        {
            Assert.Equal(new[] { "default" }, OrphanAttributes.FindOrphans(new[] { "default" }, Configs));
            Assert.Equal(new[] { "Default", "Wide" },
                OrphanAttributes.FindConfigurationsWithoutAttribute(new[] { "default" }, Configs));
        }

        [Fact]
        public void NullInputsAreTreatedAsEmpty()
        {
            Assert.Empty(OrphanAttributes.FindOrphans(null, Configs));
            Assert.Empty(OrphanAttributes.FindOrphans(new[] { "A" }, null).FindAll(n => n != "A"));
            Assert.Equal(new[] { "A" }, OrphanAttributes.FindOrphans(new[] { "A" }, null));
            Assert.Empty(OrphanAttributes.FindConfigurationsWithoutAttribute(null, null));
        }

        [Fact]
        public void DuplicateNamesAreReportedOnce()
        {
            Assert.Equal(new[] { "Old" }, OrphanAttributes.FindOrphans(new[] { "Old", "Old" }, Configs));
        }

        [Fact]
        public void NoMessageWhenThereAreNoOrphans()
        {
            Assert.Null(OrphanAttributes.BuildLogMessage(new List<string>(), new List<string> { "Wide" }));
            Assert.Null(OrphanAttributes.BuildLogMessage(null, null));
        }

        [Fact]
        public void MessageNamesOrphansAndConfigurationsWithoutData()
        {
            string message = OrphanAttributes.BuildLogMessage(
                new List<string> { "Narrow" }, new List<string> { "Wide" });
            Assert.NotNull(message);
            Assert.Contains("Narrow", message);
            Assert.Contains("Wide", message);
        }
    }
}
