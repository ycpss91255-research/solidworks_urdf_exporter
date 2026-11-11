using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.URDFMerge;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>No SOLIDWORKS needed. Tests of TreeMerger and URDFTreeCorrespondance (they replace the TODO stub
    /// that TestURDFMerge used to be).</summary>
    public class TestTreeMerger
    {
        private static Link NewLink(string name, double mass = 0.0)
        {
            Link link = new Link { Name = name };
            link.Inertial.Mass.Value = mass;
            return link;
        }

        // base -> arm -> hand, plus base -> leg
        private static URDFTreeView ModelTree()
        {
            Link root = NewLink("base", 1.0);
            Link arm = NewLink("arm", 2.0);
            Link hand = NewLink("hand", 3.0);
            Link leg = NewLink("leg", 4.0);
            arm.Children.Add(hand);
            root.Children.Add(arm);
            root.Children.Add(leg);
            URDFTreeView view = new URDFTreeView();
            view.SetTree(root);
            return view;
        }

        private static Link CsvLink(string name, double mass, double? jointX = null)
        {
            Link link = NewLink(name, mass);
            if (jointX.HasValue)
            {
                link.Joint.Origin.SetXYZ(new double[] { jointX.Value, 0, 0 });
            }
            return link;
        }

        [Fact]
        public void LinksArePairedByNameAndTheRestIsUnmatched()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                List<Link> loaded = new List<Link> { CsvLink("arm", 9), CsvLink("wheel", 9), CsvLink("hand", 9) };
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, loaded, out List<Link> matched, out List<Link> unmatched);

                Assert.Equal(new[] { "arm", "hand" }, matched.Select(l => l.Name).OrderBy(n => n).ToArray());
                Assert.Equal(new[] { "wheel" }, unmatched.Select(l => l.Name).ToArray());
            });
        }

        [Fact]
        public void NamesAreCaseSensitive()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("ARM", 9) },
                    out List<Link> matched, out List<Link> unmatched);
                Assert.Empty(matched);
                Assert.Single(unmatched);
            });
        }

        [Fact]
        public void CorrespondingLinkIsFoundPerItemAndNullWithoutMatch()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                Link csvArm = CsvLink("arm", 9);
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { csvArm }, out _, out _);

                List<TreeViewItem> items = view.Flatten();
                TreeViewItem armItem = items.Single(i => ((Link)i.Tag).Name == "arm");
                TreeViewItem legItem = items.Single(i => ((Link)i.Tag).Name == "leg");
                Assert.Same(csvArm, correspondance.GetCorrespondingLink(armItem));
                Assert.Null(correspondance.GetCorrespondingLink(legItem));
            });
        }

        [Fact]
        public void BuildingAgainReplacesThePreviousCorrespondance()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("arm", 9) }, out _, out _);
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("leg", 9) }, out List<Link> matched, out _);
                TreeViewItem armItem = view.Flatten().Single(i => ((Link)i.Tag).Name == "arm");
                Assert.Null(correspondance.GetCorrespondingLink(armItem));
                Assert.Single(matched);
            });
        }

        [Fact]
        public void OnlyMatchedLinksTakeTheCsvValuesAndTheTreeComesFromTheModel()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("arm", 9.5) }, out _, out _);

                Link merged = new TreeMerger(true, true, true, true).Merge(view, correspondance);

                Assert.Equal("base", merged.Name);
                Assert.Equal(new[] { "arm", "leg" }, merged.Children.Select(l => l.Name).ToArray());
                Link arm = merged.Children[0];
                Assert.Equal(9.5, arm.Inertial.Mass.Value);
                Assert.Equal(new[] { "hand" }, arm.Children.Select(l => l.Name).ToArray());
                Assert.Equal(1.0, merged.Inertial.Mass.Value);
                Assert.Equal(3.0, arm.Children[0].Inertial.Mass.Value);
                Assert.Equal(4.0, merged.Children[1].Inertial.Mass.Value);
                Assert.True(arm.UsesCsvInertial);
                Assert.False(merged.UsesCsvInertial);
                Assert.False(merged.Children[1].UsesCsvInertial);
            });
        }

        [Fact]
        public void ModelChoiceLeavesTheValuesAndDoesNotFlagTheLink()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("arm", 9.5, 7) }, out _, out _);

                Link merged = new TreeMerger(false, true, false, true).Merge(view, correspondance);
                Link arm = merged.Children[0];
                Assert.Equal(2.0, arm.Inertial.Mass.Value);
                Assert.False(arm.UsesCsvInertial);
                Assert.False(arm.UsesCsvJointKinematics);
                Assert.True(arm.UsesCsvVisualCollision);
                Assert.True(arm.UsesCsvJointLimits);
                Assert.Equal(0.0, arm.Joint.Origin.X);
            });
        }

        [Fact]
        public void JointKinematicsFromTheCsvReplaceTheOrigin()
        {
            TestCsvPerLink.RunSta(() =>
            {
                URDFTreeView view = ModelTree();
                URDFTreeCorrespondance correspondance = new URDFTreeCorrespondance();
                correspondance.BuildCorrespondance(view, new List<Link> { CsvLink("arm", 9.5, 7) }, out _, out _);

                Link merged = new TreeMerger(false, false, true, false).Merge(view, correspondance);
                Assert.Equal(7.0, merged.Children[0].Joint.Origin.X);
                Assert.True(merged.Children[0].UsesCsvJointKinematics);
            });
        }

        [Fact]
        public void MergeNeedsExactlyOneRoot()
        {
            TestCsvPerLink.RunSta(() =>
            {
                TreeView empty = new TreeView();
                TreeMerger merger = new TreeMerger(true, true, true, true);
                Assert.Null(merger.Merge(empty, new URDFTreeCorrespondance()));

                TreeView two = new TreeView();
                two.Items.Add(TestCsvPerLink.Item("a"));
                two.Items.Add(TestCsvPerLink.Item("b"));
                Assert.Null(merger.Merge(two, new URDFTreeCorrespondance()));
            });
        }
    }
}
