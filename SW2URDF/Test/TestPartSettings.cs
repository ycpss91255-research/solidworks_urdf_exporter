using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks: how settings saved for a part are applied to a freshly built link.
    /// </summary>
    public class TestPartSettings
    {
        [Fact]
        public void SavedInertialVisualAndCollisionReplaceTheComputedValues()
        {
            Link computed = new Link { Name = "computed" };
            computed.Inertial.Mass.Value = 1.0;
            computed.Visual.Material.Color.Red = 0.1;
            Link saved = new Link { Name = "saved" };
            saved.Inertial.Mass.Value = 2.5;
            saved.Visual.Material.Color.Red = 0.9;
            saved.Collision.Origin.SetXYZ(new double[] { 0.1, 0.2, 0.3 });

            ExportHelper.ApplySavedSettings(computed, saved);

            Assert.Equal(2.5, computed.Inertial.Mass.Value);
            Assert.Equal(0.9, computed.Visual.Material.Color.Red);
            Assert.Equal(0.2, computed.Collision.Origin.GetXYZ()[1]);
        }

        [Fact]
        public void ApplyingKeepsTheLinkNameAndDoesNotShareObjectsWithTheSavedLink()
        {
            Link computed = new Link { Name = "computed" };
            Link saved = new Link { Name = "saved" };
            saved.Collision.Origin.SetXYZ(new double[] { 1, 2, 3 });

            ExportHelper.ApplySavedSettings(computed, saved);
            saved.Collision.Origin.SetXYZ(new double[] { 9, 9, 9 });

            Assert.Equal("computed", computed.Name);
            Assert.Equal(1.0, computed.Collision.Origin.GetXYZ()[0]);
        }

        [Fact]
        public void NothingSavedLeavesTheComputedLinkUnchanged()
        {
            Link computed = new Link { Name = "computed" };
            computed.Inertial.Mass.Value = 1.0;

            ExportHelper.ApplySavedSettings(computed, null);

            Assert.Equal(1.0, computed.Inertial.Mass.Value);
        }
    }
}
