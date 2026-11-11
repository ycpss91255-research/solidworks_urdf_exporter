using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Which exporter belongs to the active document, and the part form's fallback when no base
    /// link could be built (it used to throw a NullReferenceException when opened on an assembly).
    /// Does not need SolidWorks.
    /// </summary>
    public class TestExporterRouting
    {
        [Fact]
        public void PartsGoToThePartExporter()
        {
            Assert.Equal(ExporterKind.Part, ExporterRouting.Decide(1));
        }

        [Fact]
        public void AssembliesGoToTheAssemblyExporter()
        {
            Assert.Equal(ExporterKind.Assembly, ExporterRouting.Decide(2));
        }

        [Fact]
        public void DrawingsAndUnknownDocumentsHaveNoExporter()
        {
            Assert.Equal(ExporterKind.None, ExporterRouting.Decide(3));
            Assert.Equal(ExporterKind.None, ExporterRouting.Decide(0));
            Assert.Equal(ExporterKind.None, ExporterRouting.Decide(-1));
        }

        [Fact]
        public void MissingBaseLinkFallsBackToADefaultLink()
        {
            Robot robot = new Robot();
            robot.SetBaseLink(null);
            ExportHelper.EnsureBaseLink(robot, "model");
            Assert.NotNull(robot.BaseLink);
            Assert.Equal("model", robot.BaseLink.Name);
            Assert.NotNull(robot.BaseLink.Inertial);
            Assert.NotNull(robot.BaseLink.Visual);
        }

        [Fact]
        public void ExistingBaseLinkIsKept()
        {
            Robot robot = new Robot();
            Link link = new Link(null);
            link.Name = "kept";
            robot.SetBaseLink(link);
            ExportHelper.EnsureBaseLink(robot, "model");
            Assert.Same(link, robot.BaseLink);
        }
    }
}