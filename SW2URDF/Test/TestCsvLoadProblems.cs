using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.URDFExport.URDFMerge;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// No SOLIDWORKS needed. A CSV that cannot be read completely is reported, never silently turned
    /// into zeros, and blank cells keep the value of the model (issue #84).
    /// </summary>
    public class TestCsvLoadProblems
    {
        private static List<Link> Load(string csv, CsvLoadReport report)
        {
            using (MemoryStream stream = new MemoryStream(new UTF8Encoding(false).GetBytes(csv)))
            {
                return ImportExport.LoadURDFRobotFromCSV(stream, report);
            }
        }

        [Fact]
        public void SemicolonSeparatedFileIsRead()
        {
            CsvLoadReport report = new CsvLoadReport();
            Link link = Load("Link Name;Mass\r\nbase;0.5\r\n", report).Single();
            Assert.Equal("base", link.Name);
            Assert.Equal(0.5, link.Inertial.Mass.Value);
            Assert.Equal(';', report.Delimiter);
            Assert.Empty(report.Problems);
        }

        [Fact]
        public void TabSeparatedFileIsRead()
        {
            CsvLoadReport report = new CsvLoadReport();
            Link link = Load("Link Name\tMass\r\nbase\t0.25\r\n", report).Single();
            Assert.Equal("base", link.Name);
            Assert.Equal(0.25, link.Inertial.Mass.Value);
            Assert.Equal('\t', report.Delimiter);
        }

        [Fact]
        public void DecimalCommaIsReportedAndNotLoadedAsZero()
        {
            CsvLoadReport report = new CsvLoadReport();
            Link link = Load("Link Name;Mass;Moment Ixx\r\nbase;\"0,5\";2.5\r\n", report).Single();
            CsvProblem problem = Assert.Single(report.Problems);
            Assert.Equal(CsvProblemKind.NotANumber, problem.Kind);
            Assert.Equal(2, problem.Row);
            Assert.Equal("Mass", problem.Column);
            Assert.Equal("0,5", problem.Value);
            Assert.Equal(2.5, link.Inertial.Inertia.Ixx);
            Assert.False(link.CsvData.ContainsKey("Link.Inertial.Mass.value"));
        }

        [Fact]
        public void TextInNumberCellIsReported()
        {
            CsvLoadReport report = new CsvLoadReport();
            Load("Link Name,Mass,Color Red\r\na,1,x\r\nb,abc,0.5\r\n", report);
            Assert.Equal(new[] { "Color Red", "Mass" }, report.Problems.Select(p => p.Column).ToArray());
            Assert.Equal(new[] { 2, 3 }, report.Problems.Select(p => p.Row).ToArray());
        }

        [Fact]
        public void TextColumnsAreNeverReported()
        {
            CsvLoadReport report = new CsvLoadReport();
            Load("Link Name,Mesh Filename,Joint Name,Coordinate System\r\nl,a b.STL,j,Origin_1\r\n", report);
            Assert.Empty(report.Problems);
        }

        [Fact]
        public void FileWithoutLinkNameColumnIsRejected()
        {
            Assert.Throws<CsvFormatException>(() => Load("Name,Mass\r\nbase,1\r\n", new CsvLoadReport()));
            Assert.Throws<CsvFormatException>(() => Load("", new CsvLoadReport()));
        }

        [Fact]
        public void RowWithoutLinkNameIsSkippedAndReported()
        {
            CsvLoadReport report = new CsvLoadReport();
            List<Link> links = Load("Link Name,Mass\r\na,1\r\n,2\r\n", report);
            Assert.Single(links);
            CsvProblem problem = Assert.Single(report.Problems);
            Assert.Equal(CsvProblemKind.EmptyLinkName, problem.Kind);
            Assert.Equal(3, problem.Row);
        }

        [Fact]
        public void BlankCellsKeepTheValuesOfTheModel()
        {
            TestCsvPerLink.RunSta(() =>
            {
                Link model = new Link { Name = "base" };
                model.Inertial.Mass.Value = 1.0;
                model.Inertial.Inertia.Ixx = 7.0;
                model.Inertial.Origin.SetXYZ(new double[] { 1, 2, 3 });
                model.Visual.Geometry.Mesh.Filename = "package://p/meshes/base.STL";
                model.Joint.Origin.SetXYZ(new double[] { 4, 5, 6 });
                model.Joint.CoordinateSystemName = "Origin_base";
                model.Joint.Axis.SetXYZ(new double[] { 0, 0, 1 });

                CsvLoadReport report = new CsvLoadReport();
                Link csv = Load("Link Name,Mass\r\nbase,0.5\r\n", report).Single();

                URDFTreeView view = new URDFTreeView();
                view.Items.Add(new TreeViewItem { Name = "base", Tag = model });
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { csv }, out _, out _);

                Link merged = new TreeMerger(true, true, true, true).Merge(view, correspondance);

                Assert.Equal(0.5, merged.Inertial.Mass.Value);
                Assert.Equal(7.0, merged.Inertial.Inertia.Ixx);
                Assert.Equal(new double[] { 1, 2, 3 }, merged.Inertial.Origin.GetXYZ());
                Assert.Equal("package://p/meshes/base.STL", merged.Visual.Geometry.Mesh.Filename);
                Assert.Equal(new double[] { 4, 5, 6 }, merged.Joint.Origin.GetXYZ());
                Assert.Equal("Origin_base", merged.Joint.CoordinateSystemName);
                Assert.Equal(new double[] { 0, 0, 1 }, merged.Joint.Axis.GetXYZ());
            });
        }

        [Fact]
        public void PresentCellsOverwriteAndPartialOriginKeepsTheOtherComponents()
        {
            TestCsvPerLink.RunSta(() =>
            {
                Link model = new Link { Name = "base" };
                model.Joint.Origin.SetXYZ(new double[] { 4, 5, 6 });

                Link csv = Load("Link Name,Joint Origin Y\r\nbase,9\r\n", new CsvLoadReport()).Single();
                URDFTreeView view = new URDFTreeView();
                view.Items.Add(new TreeViewItem { Name = "base", Tag = model });
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { csv }, out _, out _);

                Link merged = new TreeMerger(true, true, true, true).Merge(view, correspondance);
                Assert.Equal(new double[] { 4, 9, 6 }, merged.Joint.Origin.GetXYZ());
            });
        }
    }
}
