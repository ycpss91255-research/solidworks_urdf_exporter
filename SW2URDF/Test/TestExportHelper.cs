using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestExportHelper : SW2URDFTest
    {
        public TestExportHelper(SWTestFixture fixture) : base(fixture)
        {
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4, MeshExportFormat.STL)]
        [InlineData("4_WHEELER", 5, MeshExportFormat.STL)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4, MeshExportFormat.STL)]
        [InlineData("3_DOF_ARM", 4, MeshExportFormat.THREEDXML)]
        [InlineData("4_WHEELER", 5, MeshExportFormat.THREEDXML)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4, MeshExportFormat.THREEDXML)]
        public void TestExportRobot(string modelName, int expNumLinks, MeshExportFormat meshExportFormat)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true, meshExportFormat);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        // Each wheel link in this model selects a sub-assembly nested two levels below the top
        // assembly. Before the sub-assembly visibility fix, ShowComponents revealed only the
        // selected node and not the leaf parts beneath that nested sub-assembly, so SaveAs wrote
        // a header-only STL with no triangles. Export to a temporary directory and assert that
        // every exported link mesh actually contains geometry.
        [Theory]
        [InlineData("4_WHEELER_NESTED")]
        public void TestExportRobotNestedSubAssemblyMeshesNotEmpty(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);

            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            string meshesDirectory = Path.Combine(helper.SavePath, helper.PackageName, "meshes");
            helper.ExportRobot(true, MeshExportFormat.STL);

            string[] meshFiles = Directory.GetFiles(meshesDirectory, "*.STL");
            Assert.NotEmpty(meshFiles);
            foreach (string meshFile in meshFiles)
            {
                Assert.True(GetBinaryStlTriangleCount(meshFile) > 0,
                    Path.GetFileName(meshFile) + " was exported with no triangles");
            }
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        // Triangle count from a binary STL: an 80-byte header followed by a UInt32 count.
        private static uint GetBinaryStlTriangleCount(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                stream.Seek(80, SeekOrigin.Begin);
                return reader.ReadUInt32();
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotNoSTL(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(false);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipInertial(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(false);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipVisual(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(false);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipKinematics(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(false);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipLimits(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(false);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 3)]
        [InlineData("4_WHEELER", 4)]
        [InlineData("ORIGINAL_3_DOF_ARM", 3)]
        public void TestGetJointNames(string modelName, int expNumJoints)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true);
            List<string> jointNames = helper.GetJointNames();
            Assert.NotNull(jointNames);
            Assert.Equal(jointNames.Count, expNumJoints);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestCreateRobotFromActiveModel(string modelName, string partName)
        {
            OpenSWPartDocument(modelName, partName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(partName, helper.URDFRobot.BaseLink.Name);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        // Part export with the default frame: the part origin rotated so +Y becomes +Z.
        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestExportLink(string modelName, string partName)
        {
            OpenSWPartDocument(modelName, partName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            helper.ExportLink(true);

            XElement visualOrigin = LoadExportedVisualOrigin(helper);
            AssertVector(new[] { 0.0, 0.0, 0.0 }, visualOrigin.Attribute("xyz").Value);
            AssertVector(new[] { Math.PI / 2, 0.0, 0.0 }, visualOrigin.Attribute("rpy").Value);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        // Part export with an explicit reference coordinate system as the link frame: the mesh
        // stays in the part frame and the inverse of the frame pose lands in the visual origin,
        // exactly like the assembly exporter does per link.
        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestExportLinkWithCoordinateSystem(string modelName, string partName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName, partName);
            // frame 0.1 m along X, rotated -90 deg about X (part +Y -> frame +Z)
            Feature frame = doc.FeatureManager.CreateCoordinateSystemUsingNumericalValues(
                true, 0.1, 0, 0, true, -Math.PI / 2, 0, 0);
            Assert.NotNull(frame);
            frame.Name = "TestFrame";

            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            helper.ExportLink(false, "TestFrame");

            XElement visualOrigin = LoadExportedVisualOrigin(helper);
            // inverse pose: R^T * (-t) = Rx(+90) * (-0.1, 0, 0) = (-0.1, 0, 0); rpy = (+90 deg, 0, 0)
            AssertVector(new[] { -0.1, 0.0, 0.0 }, visualOrigin.Attribute("xyz").Value);
            AssertVector(new[] { Math.PI / 2, 0.0, 0.0 }, visualOrigin.Attribute("rpy").Value);
            Assert.True(SwApp.CloseAllDocuments(true));   // discards the test frame
        }

        // A part with exactly one reference coordinate system exports relative to it by default.
        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestExportLinkSingleCoordinateSystemIsDefault(string modelName, string partName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName, partName);
            // BlockA ships with an "Origin_global" left by earlier exports; remove it so the part
            // has exactly one coordinate system (the document is discarded afterwards).
            foreach (string existing in new ExportHelper(SwApp).GetRefCoordinateSystems())
            {
                Assert.True(doc.Extension.SelectByID2(existing, "COORDSYS", 0, 0, 0, false, 0, null, 0));
                Assert.True(doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed));
            }
            Feature frame = doc.FeatureManager.CreateCoordinateSystemUsingNumericalValues(
                true, 0, 0.2, 0, false, 0, 0, 0);
            Assert.NotNull(frame);
            frame.Name = "OnlyFrame";

            ExportHelper helper = new ExportHelper(SwApp);          // enumerates coordinate systems now
            Assert.Equal("OnlyFrame", helper.DefaultLinkFrame());
            helper.CreateRobotFromActiveModel();
            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            helper.ExportLink(true);                                 // no name, Z-up must be ignored

            XElement visualOrigin = LoadExportedVisualOrigin(helper);
            AssertVector(new[] { 0.0, -0.2, 0.0 }, visualOrigin.Attribute("xyz").Value);
            AssertVector(new[] { 0.0, 0.0, 0.0 }, visualOrigin.Attribute("rpy").Value);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        // Collision as one bounding box: size = the body's extent, centre expressed in the
        // (Z-up rotated) link frame, and no mesh collision left in the file.
        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestExportLinkBoundingBoxCollision(string modelName, string partName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName, partName);
            PartDoc part = (PartDoc)doc;
            object[] bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            double[] box = (double[])((Body2)bodies[0]).GetBodyBox();
            double[] expectedSize = { box[3] - box[0], box[4] - box[1], box[5] - box[2] };
            double[] centre = { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 };

            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            helper.ExportLink(false, "Origin_global", PartCollisionGeometry.BoundingBox);   // BlockA ships with that frame

            string urdf = Path.Combine(helper.SavePath, helper.PackageName, "urdf", helper.URDFRobot.Name + ".urdf");
            XDocument document = XDocument.Load(urdf);
            XElement link = document.Descendants("link").First();
            List<XElement> collisions = link.Elements("collision").ToList();
            Assert.Single(collisions);
            Assert.Null(collisions[0].Element("geometry").Element("mesh"));
            AssertVector(expectedSize, collisions[0].Element("geometry").Element("box").Attribute("size").Value);
            // the box sits where the visual mesh's pose puts the part-frame centre
            XElement visualOrigin = link.Element("visual").Element("origin");
            double[] vxyz = visualOrigin.Attribute("xyz").Value.Split(' ').Select(double.Parse).ToArray();
            double[] vrpy = visualOrigin.Attribute("rpy").Value.Split(' ').Select(double.Parse).ToArray();
            double[] expectedCentre = MathOps.GetXYZ(MathOps.GetTransformation(vxyz, vrpy) * MathOps.GetTranslation(centre));
            AssertVector(expectedCentre, collisions[0].Element("origin").Attribute("xyz").Value);
            AssertVector(vrpy, collisions[0].Element("origin").Attribute("rpy").Value);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("TOY_BLOCK", "BlockA")]
        public void TestExportLinkUnknownCoordinateSystemThrows(string modelName, string partName)
        {
            OpenSWPartDocument(modelName, partName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            helper.SavePath = CreateRandomTempDirectory() + Path.DirectorySeparatorChar;
            Assert.Throws<InvalidOperationException>(() => helper.ExportLink(false, "NoSuchFrame"));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        private static XElement LoadExportedVisualOrigin(ExportHelper helper)
        {
            string urdf = Path.Combine(helper.SavePath, helper.PackageName, "urdf", helper.URDFRobot.Name + ".urdf");
            Assert.True(File.Exists(urdf), urdf);
            XDocument document = XDocument.Load(urdf);
            XElement origin = document.Descendants("visual").First().Element("origin");
            Assert.NotNull(origin);
            return origin;
        }

        private static void AssertVector(double[] expected, string actual)
        {
            double[] values = actual.Split(' ').Select(double.Parse).ToArray();
            Assert.Equal(expected.Length, values.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], values[i], 6);
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestCreateRobotFromTreeView(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);

            helper.CreateRobotFromTreeView(baseNode);
            Assert.NotNull(helper.URDFRobot);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new double[] { 0, 0, 1 }, "global_origin", new double[] { 0, 0, 1 })]
        public void TestLocalizeAxis(string modelName, double[] axis, string coordSys, double[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            Assert.Equal(expected, helper.LocalizeAxis(axis, coordSys));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new string[] {
            "Origin_global",
            "Origin_prox_joint",
            "Origin_dist_joint",
            "Origin_effector_joint" })]
        public void TestGetRefCoordinateSystems(string modelName, string[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            Assert.Equal(new List<string>(expected), helper.GetRefCoordinateSystems());
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new string[] {
            "Axis_prox_joint",
            "Axis_dist_joint",
            "Axis_effector_joint" })]
        public void TestGetRefAxes(string modelName, string[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            Assert.Equal(new List<string>(expected), helper.GetRefAxes());
            Assert.True(SwApp.CloseAllDocuments(true));
        }
    }
}