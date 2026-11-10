using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.UI;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Moq;
using System.Windows;
using System.Collections.Generic;
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


        private string ExportRobotTo(string modelName, MeshExportFormat format, string saveDirectory, out int linkCount)
        {
            // The package creation message box would block an unattended run.
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            helper.SavePath = saveDirectory;
            helper.PackageName = modelName + "_" + format;
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);
            // The saved tree only holds component ids; resolve them to the open components for the export
            List<string> missing = new List<string>();
            CommonSwOperations.LoadSWComponents(doc, baseNode, missing);
            Assert.Empty(missing);
            helper.CreateRobotFromTreeView(baseNode);
            helper.ExportRobot(true, format);
            linkCount = CommonSwOperations.GetCount(helper.URDFRobot.BaseLink);
            Assert.True(SwApp.CloseAllDocuments(true));
            return Path.Combine(saveDirectory, helper.PackageName);
        }

        private static int CountStlTriangles(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 84 && bytes.Length == 84 + 50L * BitConverter.ToUInt32(bytes, 80))
            {
                return (int)BitConverter.ToUInt32(bytes, 80);
            }
            return Regex.Matches(System.Text.Encoding.ASCII.GetString(bytes), "facet normal").Count;
        }

        private static string[] ObjLines(string path, string prefix)
        {
            return File.ReadAllLines(path).Where(l => l.StartsWith(prefix + " ")).ToArray();
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        public void TestExportRobotObjFiles(string modelName, int expNumLinks)
        {
            string temp = CreateRandomTempDirectory();
            try
            {
                string objPackage = ExportRobotTo(modelName, MeshExportFormat.OBJ, temp, out int objLinks);
                Assert.Equal(expNumLinks, objLinks);

                string[] objFiles = Directory.GetFiles(Path.Combine(objPackage, "meshes"), "*.obj");
                Assert.Equal(expNumLinks, objFiles.Length);
                foreach (string obj in objFiles)
                {
                    string linkName = Path.GetFileNameWithoutExtension(obj);
                    string mtl = Path.ChangeExtension(obj, ".mtl");
                    Assert.True(File.Exists(mtl), "Missing MTL for " + linkName);
                    Assert.Single(ObjLines(obj, "mtllib"));

                    // Materials are used, defined, named after the link and have the explicit fields
                    string prefix = ObjConverter.SanitizeName(linkName, "mesh") + "_";
                    string[] defined = ObjLines(mtl, "newmtl").Select(l => l.Substring(7)).ToArray();
                    string[] used = ObjLines(obj, "usemtl").Select(l => l.Substring(7)).Distinct().ToArray();
                    Assert.NotEmpty(defined);
                    Assert.All(used, m => Assert.Contains(m, defined));
                    Assert.All(defined, m => Assert.StartsWith(prefix, m));
                    foreach (string key in new[] { "Ka", "Kd", "Ks", "Ns", "d", "illum" })
                    {
                        Assert.Equal(defined.Length, ObjLines(mtl, key).Length);
                    }
                }

                // URDF points to the .obj meshes and has no <material>
                string urdfPath = Directory.GetFiles(Path.Combine(objPackage, "urdf"), "*.urdf").Single();
                XDocument urdf = XDocument.Load(urdfPath);
                var meshes = urdf.Descendants("mesh").Select(m => (string)m.Attribute("filename")).ToList();
                Assert.NotEmpty(meshes);
                Assert.All(meshes, f => Assert.EndsWith(".obj", f));
                Assert.Empty(urdf.Descendants("material"));
                Assert.Equal(expNumLinks, urdf.Descendants("link").Count());
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }


        // Open question for the maintainers: the STL and the 3DXML (OBJ source) paths tessellate
        // differently, so this may legitimately differ. Kept separate so the other OBJ checks stay meaningful.
        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestExportRobotObjTriangleCountMatchesStl(string modelName)
        {
            string temp = CreateRandomTempDirectory();
            try
            {
                string objPackage = ExportRobotTo(modelName, MeshExportFormat.OBJ, temp, out int _);
                string stlPackage = ExportRobotTo(modelName, MeshExportFormat.STL, temp, out int _);
                string[] objFiles = Directory.GetFiles(Path.Combine(objPackage, "meshes"), "*.obj");
                Assert.NotEmpty(objFiles);
                foreach (string obj in objFiles)
                {
                    string linkName = Path.GetFileNameWithoutExtension(obj);
                    string stl = Path.Combine(stlPackage, "meshes", linkName + ".STL");
                    Assert.True(File.Exists(stl), "Missing STL for " + linkName);
                    Assert.True(CountStlTriangles(stl) > 0, "Empty STL for " + linkName);
                    Assert.Equal(CountStlTriangles(stl), ObjLines(obj, "f").Length);
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }
        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestExportRobotObjIsReproducible(string modelName)
        {
            string temp = CreateRandomTempDirectory();
            try
            {
                string first = ExportRobotTo(modelName, MeshExportFormat.OBJ, Path.Combine(temp, "a"), out int _);
                string second = ExportRobotTo(modelName, MeshExportFormat.OBJ, Path.Combine(temp, "b"), out int _);
                foreach (string mtl in Directory.GetFiles(Path.Combine(first, "meshes"), "*.mtl"))
                {
                    string other = Path.Combine(second, "meshes", Path.GetFileName(mtl));
                    Assert.Equal(File.ReadAllText(mtl), File.ReadAllText(other));
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
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

        /*
         * TODO(SIMINT-164) Part document tests not working (OpenSWPartDocument)
        [Theory]
        [InlineData("TOY_BLOCK")]
        public void TestExportLink(string modelName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.ExportLink(true);
            Assert.True(true, "Part export failed");
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("TOY_BLOCK")]
        public void TestCreateRobotFromActiveModel(string modelName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            Assert.NotNull(helper.URDFRobot);
            Assert.True(SwApp.CloseAllDocuments(true));
        }
        */

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