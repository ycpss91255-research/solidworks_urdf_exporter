using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.URDFExport.URDFMerge;
using System;
using System.Threading;
using System.Windows.Controls;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>No SOLIDWORKS needed. Values taken from the CSV apply per link (issue #83).</summary>
    public class TestCsvPerLink
    {
        internal static TreeViewItem Item(string name, params TreeViewItem[] children)
        {
            TreeViewItem item = new TreeViewItem { Name = name, Tag = new Link { Name = name } };
            foreach (TreeViewItem child in children)
            {
                item.Items.Add(child);
            }
            return item;
        }

        internal static void RunSta(Action action)
        {
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try { action(); } catch (Exception e) { error = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new InvalidOperationException(error.Message, error);
            }
        }

        [Fact]
        public void OnlyMatchedLinksAreFlaggedAsCsvValues()
        {
            RunSta(() =>
            {
                URDFTreeView view = new URDFTreeView();
                view.Items.Add(Item("root", Item("child")));

                Link csvRoot = new Link { Name = "root" };
                csvRoot.Inertial.Mass.Value = 0.5;
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new System.Collections.Generic.List<Link> { csvRoot },
                    out _, out _);
                TreeView mergeTree = view;

                TreeMerger merger = new TreeMerger(true, true, false, true);
                Link merged = merger.Merge(mergeTree, correspondance);

                Assert.True(merged.UsesCsvInertial);
                Assert.True(merged.UsesCsvVisualCollision);
                Assert.False(merged.UsesCsvJointKinematics);
                Assert.True(merged.UsesCsvJointLimits);
                Assert.Single(merged.Children);
                Link unmatched = merged.Children[0];
                Assert.False(unmatched.UsesCsvInertial);
                Assert.False(unmatched.UsesCsvVisualCollision);
                Assert.False(unmatched.UsesCsvJointLimits);
            });
        }

        [Fact]
        public void CloneKeepsTheCsvFlags()
        {
            Link link = new Link { Name = "a", UsesCsvInertial = true, UsesCsvJointLimits = true };
            Link clone = link.Clone();
            Assert.True(clone.UsesCsvInertial);
            Assert.True(clone.UsesCsvJointLimits);
            Assert.False(clone.UsesCsvVisualCollision);
        }

        [Fact]
        public void ShouldComputeSkipsCsvLinksOnly()
        {
            Assert.True(ExportHelper.ShouldCompute(true, false));
            Assert.False(ExportHelper.ShouldCompute(true, true));
            Assert.False(ExportHelper.ShouldCompute(false, false));
            Assert.False(ExportHelper.ShouldCompute(false, true));
        }
    }
}
