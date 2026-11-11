using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDFExport;
using Xunit;
using PrivateObject = Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject;

namespace SW2URDF.Test
{
    /// <summary>
    /// Which SolidWorks setting carries the coordinate system a mesh is saved relative to. Does not
    /// need SolidWorks.
    /// </summary>
    public class TestStlOutputCoordinateSystem
    {
        private const int NewSetting = (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem;
        private const int OldSetting = (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem;

        private static void SetFor(bool newSettingAccepted, bool oldSettingAccepted,
            out Mock<ModelDocExtension> extension)
        {
            extension = new Mock<ModelDocExtension>();
            extension.Setup(e => e.SetUserPreferenceString(NewSetting, It.IsAny<int>(), It.IsAny<string>()))
                .Returns(newSettingAccepted);
            extension.Setup(e => e.SetUserPreferenceString(OldSetting, It.IsAny<int>(), It.IsAny<string>()))
                .Returns(oldSettingAccepted);
            var document = new Mock<ModelDoc2>();
            document.Setup(d => d.Extension).Returns(extension.Object);
            var helper = (ExportHelper)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ExportHelper));
            new PrivateObject(helper).Invoke("SetLinkSpecificSTLPreferences", "Origin_arm", document.Object);
        }

        [Fact]
        public void NewSettingIsUsedWhenSolidWorksAcceptsIt()
        {
            SetFor(true, true, out Mock<ModelDocExtension> extension);
            extension.Verify(e => e.SetUserPreferenceString(NewSetting, It.IsAny<int>(), "Origin_arm"), Times.Once);
            extension.Verify(e => e.SetUserPreferenceString(OldSetting, It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void OlderSettingIsUsedWhenTheNewOneIsRejected()
        {
            SetFor(false, true, out Mock<ModelDocExtension> extension);
            extension.Verify(e => e.SetUserPreferenceString(NewSetting, It.IsAny<int>(), "Origin_arm"), Times.Once);
            extension.Verify(e => e.SetUserPreferenceString(OldSetting, It.IsAny<int>(), "Origin_arm"), Times.Once);
        }

        [Fact]
        public void RejectionByBothSettingsDoesNotThrow()
        {
            SetFor(false, false, out Mock<ModelDocExtension> extension);
            extension.Verify(e => e.SetUserPreferenceString(OldSetting, It.IsAny<int>(), "Origin_arm"), Times.Once);
        }
    }
}
