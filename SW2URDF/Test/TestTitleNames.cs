using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    // Pure string logic, no SolidWorks needed.
    public class TestTitleNames
    {
        [Theory]
        [InlineData("D4-1111-CX.SLDPRT", "D4-1111-CX")]
        [InlineData("D4-1111-CX.sldprt", "D4-1111-CX")]
        [InlineData("3_DOF_ARM.SLDASM", "3_DOF_ARM")]
        [InlineData("3_DOF_ARM", "3_DOF_ARM")]
        [InlineData("part.v2.SLDPRT", "part.v2")]
        [InlineData("  spaced.SLDASM ", "spaced")]
        [InlineData("Pallet 1200x800x150.SLDPRT", "Pallet 1200x800x150")]
        [InlineData("model.STEP", "model.STEP")]
        [InlineData("", "")]
        [InlineData(null, null)]
        public void TestTitleWithoutExtension(string title, string expected)
        {
            Assert.Equal(expected, CommonSwOperations.TitleWithoutExtension(title));
        }
    }
}
