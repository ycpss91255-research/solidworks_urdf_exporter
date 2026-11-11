using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using PrivateObject = Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject;

namespace SW2URDF.Test
{
    /// <summary>
    /// What a run of the wizard generated in the model, which names it gave out, and what a cancel
    /// may remove again. Does not need SolidWorks.
    /// </summary>
    public class TestGeneratedFeatureLog
    {
        private static bool Never(string name)
        {
            return false;
        }

        [Fact]
        public void FreeNameIsUsedAsIs()
        {
            string name = GeneratedFeatureLog.ChooseName("Origin_arm", n => false, Never, out bool reuse);
            Assert.Equal("Origin_arm", name);
            Assert.False(reuse);
        }

        [Fact]
        public void ExistingFrameWithTheSameGeometryIsReused()
        {
            string name = GeneratedFeatureLog.ChooseName("Origin_arm", n => n == "Origin_arm", n => true, out bool reuse);
            Assert.Equal("Origin_arm", name);
            Assert.True(reuse);
        }

        [Fact]
        public void ExistingFrameWithOtherGeometryIsNotTouchedAndTheNextFreeNameIsUsed()
        {
            var existing = new HashSet<string> { "Origin_arm", "Origin_arm2" };
            string name = GeneratedFeatureLog.ChooseName("Origin_arm", existing.Contains, Never, out bool reuse);
            Assert.Equal("Origin_arm3", name);
            Assert.False(reuse);
        }

        [Fact]
        public void LeftOverNumberedFrameWithTheSameGeometryIsReused()
        {
            // Residue of earlier previews: Origin_arm (other geometry), Origin_arm2 (same geometry)
            var existing = new HashSet<string> { "Origin_arm", "Origin_arm2", "Origin_arm3" };
            string name = GeneratedFeatureLog.ChooseName(
                "Origin_arm", existing.Contains, n => n == "Origin_arm2", out bool reuse);
            Assert.Equal("Origin_arm2", name);
            Assert.True(reuse);
        }

        // Saved settings name the frame a joint uses. A frame the exporter generated earlier is only
        // right for the geometry it was generated in (another SolidWorks configuration, or the same
        // one after an edit); a frame the user made is theirs and is never replaced.
        [Fact]
        public void SavedFrameMadeByTheUserIsKeptWhereverItIs()
        {
            string name = GeneratedFeatureLog.RefreshName(
                "My_frame", "Origin_arm", n => true, n => false, Never, out bool reuse, out bool changed);
            Assert.Equal("My_frame", name);
            Assert.False(changed);
        }

        [Fact]
        public void SavedGeneratedFrameAtTheWantedPlaceIsKept()
        {
            string name = GeneratedFeatureLog.RefreshName(
                "Origin_arm", "Origin_arm", n => true, n => true, n => true, out bool reuse, out bool changed);
            Assert.Equal("Origin_arm", name);
            Assert.False(changed);
        }

        [Fact]
        public void SavedGeneratedFrameElsewhereIsLeftAloneAndANewOneIsNamed()
        {
            var existing = new HashSet<string> { "Origin_arm" };
            string name = GeneratedFeatureLog.RefreshName(
                "Origin_arm", "Origin_arm", existing.Contains, n => true, Never, out bool reuse, out bool changed);
            Assert.Equal("Origin_arm2", name);
            Assert.False(reuse);
            Assert.True(changed);
        }

        [Fact]
        public void SavedGeneratedFrameElsewhereGivesWayToTheFrameAlreadyAtTheWantedPlace()
        {
            // Origin_arm was generated in one configuration, Origin_arm2 in the other, which is active
            var existing = new HashSet<string> { "Origin_arm", "Origin_arm2" };
            string name = GeneratedFeatureLog.RefreshName(
                "Origin_arm", "Origin_arm", existing.Contains, n => true, n => n == "Origin_arm2",
                out bool reuse, out bool changed);
            Assert.Equal("Origin_arm2", name);
            Assert.True(reuse);
            Assert.True(changed);
        }

        [Fact]
        public void SwitchingBackAndForthBetweenConfigurationsCreatesNothingAfterTheFirstRound()
        {
            // Frame positions by name: Origin_arm in configuration A, Origin_arm2 in B
            var place = new Dictionary<string, string> { { "Origin_arm", "A" } };
            string saved = "Origin_arm";
            int created = 0;
            foreach (string active in new[] { "B", "A", "B", "A" })
            {
                string chosen = GeneratedFeatureLog.RefreshName(
                    saved, "Origin_arm", place.ContainsKey, n => true,
                    n => place.ContainsKey(n) && place[n] == active, out bool reuse, out bool changed);
                if (!reuse && changed)
                {
                    place[chosen] = active;
                    created++;
                }
                saved = chosen;
            }
            Assert.Equal(1, created);
            Assert.Equal(2, place.Count);
        }

        [Fact]
        public void SameValuesUsesTheTolerance()
        {
            Assert.True(GeneratedFeatureLog.SameValues(new[] { 1.0, 2.0 }, new[] { 1.0 + 1e-9, 2.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameValues(new[] { 1.0, 2.0 }, new[] { 1.0 + 1e-3, 2.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameValues(new[] { 1.0 }, new[] { 1.0, 2.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameValues(null, new[] { 1.0 }, 1e-6));
        }

        [Fact]
        public void SameDirectionIgnoresLengthAndSign()
        {
            Assert.True(GeneratedFeatureLog.SameDirection(new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, 3.0 }, 1e-6));
            Assert.True(GeneratedFeatureLog.SameDirection(new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, -1.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameDirection(new[] { 0.0, 0.0, 1.0 }, new[] { 0.0, 1.0, 0.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameDirection(new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 1.0 }, 1e-6));
            Assert.False(GeneratedFeatureLog.SameDirection(null, new[] { 0.0, 0.0, 1.0 }, 1e-6));
        }

        [Fact]
        public void FramesAreRemovedNewestFirstAndTheSketchLast()
        {
            var log = new GeneratedFeatureLog();
            log.AddCreated(GeneratedKind.Sketch, "URDF Reference");
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_global");
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_arm");
            log.AddCreated(GeneratedKind.Axis, "Axis_arm");

            string[] order = log.RemovalOrder().Select(f => f.Name).ToArray();

            Assert.Equal(new[] { "Axis_arm", "Origin_arm", "Origin_global", "URDF Reference" }, order);
        }

        [Fact]
        public void ReusedFramesAreAssignedButNeverRemoved()
        {
            var log = new GeneratedFeatureLog();
            log.AddReused(GeneratedKind.CoordinateSystem, "Origin_arm");

            Assert.True(log.WasAssigned(GeneratedKind.CoordinateSystem, "Origin_arm"));
            Assert.False(log.WasCreated(GeneratedKind.CoordinateSystem, "Origin_arm"));
            Assert.Empty(log.RemovalOrder());
        }

        [Fact]
        public void NothingIsRecordedForFeaturesThatWereThereBefore()
        {
            var log = new GeneratedFeatureLog();
            Assert.True(log.IsEmpty);
            Assert.Empty(log.RemovalOrder());
        }

        [Fact]
        public void ClearForgetsEverything()
        {
            var log = new GeneratedFeatureLog();
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_arm");
            log.AddSketchEntity(new object());
            log.Clear();
            Assert.True(log.IsEmpty);
            Assert.False(log.WasAssigned(GeneratedKind.CoordinateSystem, "Origin_arm"));
        }

        private static LinkNode Tree(string baseFrame, string childFrame, string childAxis)
        {
            Link root = new Link { Name = "base_link" };
            root.Joint.CoordinateSystemName = baseFrame;
            Link child = new Link { Name = "arm_link" };
            child.Joint.CoordinateSystemName = childFrame;
            child.Joint.AxisName = childAxis;
            root.Children.Add(child);
            child.Parent = root;
            return new LinkNode(root);
        }

        [Fact]
        public void RestoreGivesTheJointsTheirEarlierNames()
        {
            LinkNode tree = Tree("Automatically Generate", "Automatically Generate", "Automatically Generate");
            JointNameSnapshot snapshot = JointNameSnapshot.Take(tree);
            var log = new GeneratedFeatureLog();
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_global");
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_arm_joint");
            log.AddCreated(GeneratedKind.Axis, "Axis_arm_joint");
            tree.Link.Joint.CoordinateSystemName = "Origin_global";
            LinkNode child = (LinkNode)tree.Nodes[0];
            child.Link.Joint.CoordinateSystemName = "Origin_arm_joint";
            child.Link.Joint.AxisName = "Axis_arm_joint";

            snapshot.Restore(log);

            Assert.Equal("Automatically Generate", tree.Link.Joint.CoordinateSystemName);
            Assert.Equal("Automatically Generate", child.Link.Joint.CoordinateSystemName);
            Assert.Equal("Automatically Generate", child.Link.Joint.AxisName);
        }

        [Fact]
        public void RestoreAlsoUndoesTheNameOfAReusedFrame()
        {
            LinkNode tree = Tree("Automatically Generate", "Automatically Generate", "None");
            JointNameSnapshot snapshot = JointNameSnapshot.Take(tree);
            var log = new GeneratedFeatureLog();
            log.AddReused(GeneratedKind.CoordinateSystem, "Origin_arm_joint");
            LinkNode child = (LinkNode)tree.Nodes[0];
            child.Link.Joint.CoordinateSystemName = "Origin_arm_joint";

            snapshot.Restore(log);

            Assert.Equal("Automatically Generate", child.Link.Joint.CoordinateSystemName);
        }

        [Fact]
        public void RestoreLeavesANameTheUserChoseAlone()
        {
            LinkNode tree = Tree("Origin_mine", "Automatically Generate", "None");
            JointNameSnapshot snapshot = JointNameSnapshot.Take(tree);
            var log = new GeneratedFeatureLog();
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_arm_joint");
            LinkNode child = (LinkNode)tree.Nodes[0];
            child.Link.Joint.CoordinateSystemName = "Coordinate System1";

            snapshot.Restore(log);

            Assert.Equal("Origin_mine", tree.Link.Joint.CoordinateSystemName);
            Assert.Equal("Coordinate System1", child.Link.Joint.CoordinateSystemName);
        }
    }

    /// <summary>
    /// What cancelling the preview does to the model, with a fake SolidWorks document. The live
    /// check against SolidWorks itself is described in the pull request.
    /// </summary>
    public class TestDiscardGeneratedFeatures
    {
        private class FakeModel
        {
            public ExportHelper Helper;
            public Moq.Mock<ModelDocExtension> Extension = new Moq.Mock<ModelDocExtension>();
            public List<string> Selected = new List<string>();
            public List<string> Deleted = new List<string>();
            private string current;

            public FakeModel()
            {
                Helper = (ExportHelper)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(ExportHelper));
                var document = new Moq.Mock<ModelDoc2>();
                var sketchManager = new Moq.Mock<SketchManager>();
                document.SetupGet(d => d.Extension).Returns(Extension.Object);
                document.SetupGet(d => d.SketchManager).Returns(sketchManager.Object);
                Extension.Setup(e => e.DeleteSelection2(0)).Returns(true).Callback(() => Deleted.Add(current));
                Helper.ActiveSWModel = document.Object;
            }

            public Feature Feature(string name, string currentName = null, object[] children = null)
            {
                var feature = new Moq.Mock<Feature>();
                feature.SetupProperty(f => f.Name, currentName ?? name);
                feature.Setup(f => f.GetChildren()).Returns(children);
                feature.Setup(f => f.Select2(false, 0)).Returns(true).Callback(() => current = name);
                return feature.Object;
            }
        }

        [Fact]
        public void OnlyTheFeaturesOfTheRunAreDeleted()
        {
            var model = new FakeModel();
            GeneratedFeatureLog log = model.Helper.GeneratedFeatures;
            log.AddCreated(GeneratedKind.CoordinateSystem, "Origin_arm", model.Feature("Origin_arm"));
            log.AddCreated(GeneratedKind.Sketch, "URDF Reference", model.Feature("URDF Reference"));
            model.Feature("Origin_from_before");   // exists in the model but is not in the log

            model.Helper.DiscardGeneratedFeatures();

            Assert.Equal(new[] { "Origin_arm", "URDF Reference" }, model.Deleted.ToArray());
            Assert.True(log.IsEmpty);
        }

        [Fact]
        public void ReusedFramesAreNotDeleted()
        {
            var model = new FakeModel();
            model.Helper.GeneratedFeatures.AddReused(GeneratedKind.CoordinateSystem, "Origin_arm");

            model.Helper.DiscardGeneratedFeatures();

            Assert.Empty(model.Deleted);
        }

        [Fact]
        public void ARenamedOrAlreadyDeletedFeatureIsSkipped()
        {
            var model = new FakeModel();
            // the object still answers to its name, but the user renamed it since
            model.Helper.GeneratedFeatures.AddCreated(
                GeneratedKind.CoordinateSystem, "Origin_arm", model.Feature("Origin_arm", "My own name"));

            model.Helper.DiscardGeneratedFeatures();

            Assert.Empty(model.Deleted);
        }

        [Fact]
        public void AFeatureUsedByOthersIsKept()
        {
            var model = new FakeModel();
            model.Helper.GeneratedFeatures.AddCreated(GeneratedKind.Sketch, "URDF Reference",
                model.Feature("URDF Reference", null, new object[] { new object() }));

            model.Helper.DiscardGeneratedFeatures();

            Assert.Empty(model.Deleted);
        }

        [Fact]
        public void TheJointNamesComeBackAndASecondDiscardDoesNothing()
        {
            var model = new FakeModel();
            Link root = new Link { Name = "base_link" };
            root.Joint.CoordinateSystemName = "Automatically Generate";
            LinkNode tree = new LinkNode(root);
            new PrivateObject(model.Helper).SetField("jointNames", JointNameSnapshot.Take(tree));
            model.Helper.GeneratedFeatures.AddCreated(
                GeneratedKind.CoordinateSystem, "Origin_global", model.Feature("Origin_global"));
            root.Joint.CoordinateSystemName = "Origin_global";

            model.Helper.DiscardGeneratedFeatures();
            model.Helper.DiscardGeneratedFeatures();

            Assert.Equal("Automatically Generate", root.Joint.CoordinateSystemName);
            Assert.Single(model.Deleted);
        }
    }
}
