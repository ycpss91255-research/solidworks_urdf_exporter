using SW2URDF.UI;
using SW2URDF.URDF;
using System.Collections.Generic;
using System.Windows.Controls;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>No SOLIDWORKS needed. The texts of the merge window come from the language catalog.</summary>
    public class TestTreeMergeWindowText
    {
        private static TreeMergeWPF Create(string language)
        {
            Link model = new Link { Name = "base" };
            return new TreeMergeWPF(model, new List<Link> { new Link { Name = "base" } }, "robot.csv", "CFG_A",
                new UiText(language));
        }

        [Fact]
        public void EnglishWindowKeepsTheEnglishTexts()
        {
            TestCsvPerLink.RunSta(() =>
            {
                TreeMergeWPF window = Create(null);
                Assert.Equal("URDF Tree Merge", window.Title);
                Assert.Equal("Merge", window.MergeButton.Content);
                Assert.Equal("Unmatched Links", window.UnmatchedListLabel.Content);
                Assert.Equal("Mass and Inertia", window.MassInertiaGroup.Header);
            });
        }

        [Fact]
        public void TraditionalChineseWindowHasNoEnglishLeftOver()
        {
            TestCsvPerLink.RunSta(() =>
            {
                TreeMergeWPF window = Create("chinese");
                Assert.NotEqual("URDF Tree Merge", window.Title);
                Assert.NotEqual("Merge", window.MergeButton.Content);
                Assert.NotEqual("Cancel", window.CancelButton.Content);
                Assert.NotEqual("Reset", window.ResetSelectedLinkButton.Content);
                Assert.NotEqual("Change Name", window.UpdateSelectedLinkButton.Content);
                Assert.NotEqual("Preview of Updated Configuration", window.ExistingTreeLabel.Content);
                Assert.NotEqual("Unmatched Links", window.UnmatchedListLabel.Content);
                Assert.NotEqual("Mass and Inertia", window.MassInertiaGroup.Header);
                Assert.NotEqual("Visual Properties", window.VisualGroup.Header);
                Assert.NotEqual("Joint Kinematics", window.JointKinematicsGroup.Header);
                Assert.NotEqual("Other Joint Values", window.OtherJointGroup.Header);
                Assert.NotEqual("Use:", window.UseLabel1.Content);
                Assert.NotEqual("Link: ", window.SelectedLinkNameLabel.Content);
                Assert.DoesNotContain("Match loaded CSV", window.MergeDescription.Text);
                Assert.Contains("CFG_A", ((TextBlock)window.MassInertiaExistingButton.ToolTip).Text);
                Assert.DoesNotContain("Use Mass", ((TextBlock)window.MassInertiaLoadedButton.ToolTip).Text);
            });
        }
    }
}
