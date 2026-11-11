using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
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
    /// <summary>No SOLIDWORKS needed. A mesh file name from the CSV survives the export (issue #85).</summary>
    public class TestCsvMeshFilename
    {
        private static Link Merge(string csvText, bool useCsvVisual, string modelMesh)
        {
            Link merged = null;
            TestCsvPerLink.RunSta(() =>
            {
                Link model = new Link { Name = "base" };
                model.Visual.Geometry.Mesh.Filename = modelMesh;
                model.Collision.Geometry.Mesh.Filename = modelMesh;

                Link csv;
                using (MemoryStream stream = new MemoryStream(new UTF8Encoding(false).GetBytes(csvText)))
                {
                    csv = ImportExport.LoadURDFRobotFromCSV(stream).Single();
                }

                URDFTreeView view = new URDFTreeView();
                view.Items.Add(new TreeViewItem { Name = "base", Tag = model });
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { csv }, out _, out _);
                merged = new TreeMerger(true, useCsvVisual, true, true).Merge(view, correspondance);
            });
            return merged;
        }

        [Fact]
        public void MeshNamesFromTheCsvAreMarkedAsKept()
        {
            Link merged = Merge("Link Name,Mesh Filename,Collision Mesh Filename\r\nbase,package://p/meshes/a.STL,package://p/meshes/b.STL\r\n", true, "x");
            Assert.True(merged.UsesCsvVisualMesh);
            Assert.True(merged.UsesCsvCollisionMesh);
            Assert.Equal("package://p/meshes/a.STL", merged.Visual.Geometry.Mesh.Filename);
            Assert.Equal("package://p/meshes/b.STL", merged.Collision.Geometry.Mesh.Filename);
        }

        [Fact]
        public void BlankMeshNameOrModelChoiceIsNotKept()
        {
            Link blank = Merge("Link Name,Mesh Filename\r\nbase,\r\n", true, "x");
            Assert.False(blank.UsesCsvVisualMesh);
            Assert.False(blank.UsesCsvCollisionMesh);

            Link model = Merge("Link Name,Mesh Filename\r\nbase,package://p/meshes/a.STL\r\n", false, "x");
            Assert.False(model.UsesCsvVisualMesh);
            Assert.Equal("x", model.Visual.Geometry.Mesh.Filename);
        }

        [Fact]
        public void ExportKeepsTheCsvNameAndOtherwiseUsesTheNewOne()
        {
            Assert.Equal("old", ExportHelper.MeshFilenameForExport("old", true, "new"));
            Assert.Equal("new", ExportHelper.MeshFilenameForExport("old", false, "new"));
            Assert.Equal("new", ExportHelper.MeshFilenameForExport("", true, "new"));
        }

        [Fact]
        public void MeshIsNotExportedOnlyWhenBothNamesComeFromTheCsv()
        {
            Link link = new Link { UsesCsvVisualMesh = true };
            Assert.True(ExportHelper.ShouldExportMesh(true, link));
            link.UsesCsvCollisionMesh = true;
            Assert.False(ExportHelper.ShouldExportMesh(true, link));
            Assert.False(ExportHelper.ShouldExportMesh(false, new Link()));
            Assert.True(ExportHelper.ShouldExportMesh(true, new Link()));
        }
    }
}
