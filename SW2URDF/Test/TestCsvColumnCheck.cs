using SW2URDF.URDFExport.CSV;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>No SOLIDWORKS needed.</summary>
    public class TestCsvColumnCheck
    {
        [Fact]
        public void ChildLinkIsNotReportedAsMissingColumn()
        {
            // A joint's child is the link itself (its name is the "Link Name" column), so the
            // child element has no column of its own and must not be reported as unwritten.
            var keys = new[] { "Link.name", "Link.Joint.ChildLink.link", "Link.Joint.ParentLink.link" };
            Assert.Empty(ImportExport.UnwrittenColumns(keys));
        }

        [Fact]
        public void UnknownKeysAreStillReported()
        {
            var keys = new[] { "Link.name", "Link.Joint.ChildLink.link", "Link.Something.new" };
            Assert.Equal(new[] { "Link.Something.new" }, ImportExport.UnwrittenColumns(keys).ToArray());
        }
    }
}
