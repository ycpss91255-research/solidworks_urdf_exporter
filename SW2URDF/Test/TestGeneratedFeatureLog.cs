using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System.Collections.Generic;
using System.Linq;
using Xunit;

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
}
