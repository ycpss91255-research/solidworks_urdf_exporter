using SW2URDF.Versioning;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Reads only the version of the built assembly, so it does not need SolidWorks.
    /// A build from a working tree with uncommitted changes is dirty; that is normal during
    /// development and must not fail the test. Release builds should come from a clean commit
    /// (see TESTING.md).
    /// </summary>
    public class TestVersioning
    {
        [Fact]
        public void TestGetCommitVersion()
        {
            string commitVersion = Version.GetCommitVersion();
            Assert.NotNull(commitVersion);
            Assert.NotEmpty(commitVersion);
            string clean = Version.StripDirtySuffix(commitVersion);
            Assert.NotEmpty(clean);
            Assert.DoesNotContain("dirty", clean);
        }

        [Fact]
        public void TestGetBuildVersion()
        {
            string buildVersion = Version.GetBuildVersion();
            Assert.NotNull(buildVersion);
            Assert.NotEmpty(buildVersion);
        }

        [Fact]
        public void DirtyMarkerIsDetected()
        {
            Assert.True(Version.IsDirty("7c99541-dirty"));
            Assert.True(Version.IsDirty("v1.6.0-3-g7c99541-dirty"));
            Assert.False(Version.IsDirty("7c99541"));
            Assert.False(Version.IsDirty("v1.6.0-3-g7c99541"));
            Assert.False(Version.IsDirty(null));
            Assert.False(Version.IsDirty(""));
        }

        [Fact]
        public void DirtyMarkerIsStrippedOnlyAtTheEnd()
        {
            Assert.Equal("7c99541", Version.StripDirtySuffix("7c99541-dirty"));
            Assert.Equal("v1.6.0-3-g7c99541", Version.StripDirtySuffix("v1.6.0-3-g7c99541-dirty"));
            Assert.Equal("7c99541", Version.StripDirtySuffix("7c99541"));
            Assert.Equal("dirty-feature-1", Version.StripDirtySuffix("dirty-feature-1"));
            Assert.Null(Version.StripDirtySuffix(null));
        }
    }
}