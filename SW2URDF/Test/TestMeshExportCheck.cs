using System;
using System.IO;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>Does not need SolidWorks. Checks how a finished mesh export is judged and marked (issue 61).</summary>
    public class TestMeshExportCheck : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "sw2urdf_meshcheck_" + Guid.NewGuid().ToString("N"));

        public TestMeshExportCheck() { Directory.CreateDirectory(dir); }

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Fact]
        public void MissingFileIsNotUsable()
        {
            Assert.False(MeshExportCheck.IsUsableMesh(Path.Combine(dir, "none.STL")));
        }

        [Fact]
        public void EmptyFileIsNotUsable()
        {
            string path = Path.Combine(dir, "empty.STL");
            File.WriteAllBytes(path, new byte[0]);
            Assert.False(MeshExportCheck.IsUsableMesh(path));
        }

        [Fact]
        public void NonEmptyFileIsUsable()
        {
            string path = Path.Combine(dir, "ok.STL");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.True(MeshExportCheck.IsUsableMesh(path));
        }

        [Fact]
        public void NullOrBlankPathIsNotUsable()
        {
            Assert.False(MeshExportCheck.IsUsableMesh(null));
            Assert.False(MeshExportCheck.IsUsableMesh(""));
        }

        [Fact]
        public void FailureMarkerNamesTheLinks()
        {
            MeshExportCheck.WriteFailureMarker(dir, new[] { "arm_1", "手臂" });
            string marker = Path.Combine(dir, MeshExportCheck.FailureMarkerFileName);
            Assert.True(File.Exists(marker));
            string text = File.ReadAllText(marker);
            Assert.Contains("arm_1", text);
            Assert.Contains("手臂", text);
        }

        [Fact]
        public void FailureMarkerIsRemovedAfterSuccess()
        {
            MeshExportCheck.WriteFailureMarker(dir, new[] { "a" });
            MeshExportCheck.RemoveFailureMarker(dir);
            Assert.False(File.Exists(Path.Combine(dir, MeshExportCheck.FailureMarkerFileName)));
            MeshExportCheck.RemoveFailureMarker(dir); // no marker: no error
        }

        [Fact]
        public void LinkListIsJoinedForTheMessage()
        {
            Assert.Equal("a, b", MeshExportCheck.JoinLinkNames(new[] { "a", "b" }));
        }
    }
}
