using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    // A part without a texture used to be written with the bare textures directory as its texture.
    public class TestPartTexture
    {
        [Fact]
        public void PartWithoutTextureWritesNoTextureReference()
        {
            Assert.Null(ExportHelper.TextureUrdfFilename("", "package://pkg/textures/"));
            Assert.Null(ExportHelper.TextureUrdfFilename(null, "package://pkg/textures/"));
            Assert.Null(ExportHelper.TextureUrdfFilename("  ", "package://pkg/textures/"));
        }

        [Fact]
        public void PartWithTextureKeepsItsFileNameInTheTexturesDirectory()
        {
            Assert.Equal("package://pkg/textures/wood.png",
                ExportHelper.TextureUrdfFilename(@"C:\data\images\wood.png", "package://pkg/textures/"));
        }
    }
}
