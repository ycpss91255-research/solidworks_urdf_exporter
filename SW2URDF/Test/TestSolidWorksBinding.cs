using System;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks, so it has no test fixture. Covers issue #16.
    /// </summary>
    public class TestSolidWorksBinding
    {
        [Fact]
        public void BlankValueMeansNoBinding()
        {
            Assert.Null(SolidWorksBinding.ParseTargetPid(null));
            Assert.Null(SolidWorksBinding.ParseTargetPid(""));
            Assert.Null(SolidWorksBinding.ParseTargetPid("   "));
        }

        [Fact]
        public void ValidPidIsParsed()
        {
            Assert.Equal(1234, SolidWorksBinding.ParseTargetPid("1234"));
            Assert.Equal(42, SolidWorksBinding.ParseTargetPid(" 42 "));
        }

        [Fact]
        public void InvalidPidFailsClearlyInsteadOfFallingBack()
        {
            foreach (string value in new[] { "abc", "0", "-3", "12x", "1.5" })
            {
                string v = value;
                ArgumentException e = Assert.Throws<ArgumentException>(() => SolidWorksBinding.ParseTargetPid(v));
                Assert.Contains(SolidWorksBinding.PidEnvironmentVariable, e.Message);
                Assert.Contains(v, e.Message);
            }
        }

        [Fact]
        public void RotNameIsTheSolidWorksPidMoniker()
        {
            Assert.Equal("SolidWorks_PID_4321", SolidWorksBinding.RotName(4321));
        }

        [Fact]
        public void ProcessIdMismatchIsReportedWithBothIds()
        {
            SolidWorksBinding.VerifyProcessId(7, 7);
            InvalidOperationException e =
                Assert.Throws<InvalidOperationException>(() => SolidWorksBinding.VerifyProcessId(7, 9));
            Assert.Contains("7", e.Message);
            Assert.Contains("9", e.Message);
        }

        [Fact]
        public void ConnectingToAPidThatIsNotRunningFailsClearlyAndStartsNothing()
        {
            InvalidOperationException e =
                Assert.Throws<InvalidOperationException>(() => SolidWorksBinding.Connect(int.MaxValue));
            Assert.Contains(SolidWorksBinding.RotName(int.MaxValue), e.Message);
        }
    }
}
