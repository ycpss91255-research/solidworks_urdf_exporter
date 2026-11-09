using SW2URDF.URDFExport;
using System.Collections.Generic;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks, so it has no test fixture.
    /// </summary>
    public class TestMissingComponentPolicy
    {
        [Fact]
        public void LinkWithFewerLoadedComponentsIsIncomplete()
        {
            Assert.True(MissingComponentPolicy.IsLinkIncomplete(2, 1));
            Assert.True(MissingComponentPolicy.IsLinkIncomplete(1, 0));
        }

        [Fact]
        public void LinkWithAllComponentsIsComplete()
        {
            Assert.False(MissingComponentPolicy.IsLinkIncomplete(2, 2));
            Assert.False(MissingComponentPolicy.IsLinkIncomplete(0, 0));
        }

        [Fact]
        public void ExportMustBeConfirmedOnlyWhenLinksAreMissingComponents()
        {
            Assert.False(MissingComponentPolicy.MustConfirmExport(null));
            Assert.False(MissingComponentPolicy.MustConfirmExport(new List<string>()));
            Assert.True(MissingComponentPolicy.MustConfirmExport(new List<string> { "link_a" }));
        }

        [Fact]
        public void NoMessageWhenNothingIsMissing()
        {
            Assert.Null(MissingComponentPolicy.BuildMessage(new List<string>(), "Config A", false));
            Assert.Null(MissingComponentPolicy.BuildMessage(null, "Config A", true));
        }

        [Fact]
        public void MessageNamesLinksAndConfiguration()
        {
            string message = MissingComponentPolicy.BuildMessage(
                new List<string> { "link_a", "link_b" }, "Config B", false);
            Assert.NotNull(message);
            Assert.Contains("link_a", message);
            Assert.Contains("link_b", message);
            Assert.Contains("Config B", message);
        }

        [Fact]
        public void ExportMessageWarnsThatTheResultWillBeIncomplete()
        {
            string load = MissingComponentPolicy.BuildMessage(new List<string> { "a" }, "C", false);
            string export = MissingComponentPolicy.BuildMessage(new List<string> { "a" }, "C", true);
            Assert.Contains("incomplete", export.ToLowerInvariant());
            Assert.NotEqual(load, export);
        }

        [Fact]
        public void MessageWithoutConfigurationNameStillListsLinks()
        {
            string message = MissingComponentPolicy.BuildMessage(new List<string> { "link_a" }, null, true);
            Assert.Contains("link_a", message);
        }
    }
}
