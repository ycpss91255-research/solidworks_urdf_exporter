using System;
using System.IO;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks. Checks that the generated TWO_BOXES example is present and small.
    /// </summary>
    public class TestTwoBoxesFixture
    {
        [Fact]
        public void ModelFilesExist()
        {
            string dir = SW2URDFTest.GetModelDirectory(TwoBoxesModel.Name);
            Assert.True(File.Exists(Path.Combine(dir, TwoBoxesModel.Name + ".SLDASM")), "missing assembly in " + dir);
            Assert.True(File.Exists(Path.Combine(dir, TwoBoxesModel.PartRed + ".SLDPRT")), "missing red part");
            Assert.True(File.Exists(Path.Combine(dir, TwoBoxesModel.PartBlue + ".SLDPRT")), "missing blue part");
        }

        [Fact]
        public void ModelStaysSmall()
        {
            string dir = SW2URDFTest.GetModelDirectory(TwoBoxesModel.Name);
            long total = 0;
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }
            Assert.InRange(total, 1, TwoBoxesModel.MaxTotalBytes);
        }

        [Fact]
        public void ExamplesDirectoryCanBeOverriddenByEnvironment()
        {
            string previous = Environment.GetEnvironmentVariable(SW2URDFTest.ExamplesDirectoryEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(SW2URDFTest.ExamplesDirectoryEnvironmentVariable, @"C:\some\examples");
                Assert.Equal(@"C:\some\examples", SW2URDFTest.GetExamplesDirectory());
                Assert.Equal(@"C:\some\examples\" + TwoBoxesModel.Name, SW2URDFTest.GetModelDirectory(TwoBoxesModel.Name));
            }
            finally
            {
                Environment.SetEnvironmentVariable(SW2URDFTest.ExamplesDirectoryEnvironmentVariable, previous);
            }
        }
    }
}
