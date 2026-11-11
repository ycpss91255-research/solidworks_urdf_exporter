using System.Collections.Generic;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>Does not need SolidWorks. File and package names derived from model names (issue 62).</summary>
    public class TestFileNames
    {
        [Fact]
        public void ValidNamesStayUnchanged()
        {
            Assert.Equal("link_1", FileNames.SanitizeFileName("link_1"));
            Assert.Equal("手臂_üé_Ω", FileNames.SanitizeFileName("手臂_üé_Ω"));
            Assert.Equal("a.b", FileNames.SanitizeFileName("a.b"));
        }

        [Fact]
        public void InvalidCharactersAreReplaced()
        {
            Assert.Equal("a_b_c_d_e_f_g_h_i_j", FileNames.SanitizeFileName("a/b\\c:d*e?f\"g<h>i|j"));
            Assert.Equal("arm_upper_1", FileNames.SanitizeFileName("arm\\upper:1"));
        }

        [Fact]
        public void PathTraversalCannotEscape()
        {
            string name = FileNames.SanitizeFileName("..\\x");
            Assert.DoesNotContain("\\", name);
            Assert.DoesNotContain("/", name);
            Assert.NotEqual("..", FileNames.SanitizeFileName(".."));
            Assert.NotEqual(".", FileNames.SanitizeFileName("."));
        }

        [Fact]
        public void ControlCharactersAreReplaced()
        {
            Assert.Equal("a_b", FileNames.SanitizeFileName("a\tb"));
            Assert.Equal("a_b", FileNames.SanitizeFileName("a\nb"));
        }

        [Fact]
        public void TrailingDotsAndSpacesAreRemoved()
        {
            Assert.Equal("a", FileNames.SanitizeFileName("a. . "));
            Assert.Equal("a b", FileNames.SanitizeFileName("  a b  "));
        }

        [Fact]
        public void ReservedDeviceNamesAreChanged()
        {
            foreach (string reserved in new[] { "CON", "nul", "Aux", "COM1", "lpt9", "con.txt" })
            {
                CheckReserved(reserved);
            }
        }

        private static void CheckReserved(string reserved)
        {
            string name = FileNames.SanitizeFileName(reserved);
            Assert.NotEqual(reserved, name);
            Assert.False(FileNames.IsReservedDeviceName(name), name);
        }

        [Fact]
        public void EmptyNameGetsAFallback()
        {
            Assert.Equal("link", FileNames.SanitizeFileName(""));
            Assert.Equal("link", FileNames.SanitizeFileName(null));
            Assert.Equal("link", FileNames.SanitizeFileName(" . "));
            Assert.Equal("x", FileNames.SanitizeFileName("", "x"));
        }

        [Fact]
        public void LongNamesAreShortened()
        {
            string name = FileNames.SanitizeFileName(new string('a', 400));
            Assert.True(name.Length <= FileNames.MaxFileNameLength);
            Assert.True(name.Length > 0);
        }

        [Fact]
        public void UniqueNamesAvoidCollisionsIgnoringCase()
        {
            HashSet<string> used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            Assert.Equal("a_b", FileNames.UniqueFileName("a/b", used));
            Assert.Equal("a_b_2", FileNames.UniqueFileName("a:b", used));
            Assert.Equal("A_B_3", FileNames.UniqueFileName("A_B", used));
            Assert.Equal("a_b", FileNames.UniqueFileName("a/b", new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)));
        }

        [Fact]
        public void PackageNameDropsDocumentExtensionAndSpaces()
        {
            Assert.Equal("My_Robot", FileNames.SanitizePackageName("My Robot"));
            Assert.Equal("arm", FileNames.SanitizePackageName("arm.SLDASM"));
            Assert.Equal("arm", FileNames.SanitizePackageName("arm.sldprt"));
            Assert.Equal("3_DOF_ARM", FileNames.SanitizePackageName("3_DOF_ARM"));
            Assert.Equal("機器人", FileNames.SanitizePackageName("機器人"));
        }

        [Fact]
        public void PackageNameIsSafeInUris()
        {
            string name = FileNames.SanitizePackageName("a#b%c?d e/f");
            Assert.Equal("a_b_c_d_e_f", name);
            Assert.Equal("package", FileNames.SanitizePackageName(""));
        }

        [Fact]
        public void TextureFileNamesAreUniquePerSource()
        {
            HashSet<string> used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            Assert.Equal("wood.png", FileNames.UniqueFileNameWithExtension("wood.png", used));
            Assert.Equal("wood_2.png", FileNames.UniqueFileNameWithExtension("wood.png", used));
        }
    }
}
