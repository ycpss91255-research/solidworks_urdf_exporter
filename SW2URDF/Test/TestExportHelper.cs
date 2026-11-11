using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
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
        // The STL and OBJ exports tessellate differently, so triangle counts are not comparable. Judge both
        // meshes against reality instead: each must enclose the exact volume of the link within 0.5 % and the
        // two bounding boxes must agree.
        private static List<double[]> ReadStlTriangles(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            List<double[]> triangles = new List<double[]>();
            Assert.True(bytes.Length >= 84 && bytes.Length == 84 + 50L * BitConverter.ToUInt32(bytes, 80),
                "Expected a binary STL: " + path);
            int count = (int)BitConverter.ToUInt32(bytes, 80);
            for (int i = 0; i < count; i++)
            {
                int offset = 84 + 50 * i + 12; // skip the normal
                double[] t = new double[9];
                for (int k = 0; k < 9; k++)
                {
                    t[k] = BitConverter.ToSingle(bytes, offset + 4 * k);
                }
                triangles.Add(t);
            }
            return triangles;
        }

        private static List<double[]> ReadObjTriangles(string path)
        {
            List<double[]> vertices = new List<double[]>();
            List<double[]> triangles = new List<double[]>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] p = line.Split(' ');
                if (p[0] == "v")
                {
                    vertices.Add(p.Skip(1).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                }
                else if (p[0] == "f")
                {
                    int[] index = p.Skip(1).Select(x => int.Parse(x.Split('/')[0], System.Globalization.CultureInfo.InvariantCulture) - 1).ToArray();
                    Assert.Equal(3, index.Length);
                    triangles.Add(index.SelectMany(i => vertices[i]).ToArray());
                }
            }
            return triangles;
        }

        // Applies the visual origin (xyz, rpy as Rz*Ry*Rx) of the named link to mesh triangles.
        private static List<double[]> ApplyVisualOrigin(List<double[]> triangles, XDocument urdf, string linkName)
        {
            XElement origin = urdf.Descendants("link").Single(l => (string)l.Attribute("name") == linkName)
                .Element("visual")?.Element("origin");
            if (origin == null)
            {
                return triangles;
            }
            Func<string, double[]> read = a => ((string)origin.Attribute(a) ?? "0 0 0").Split(' ', '\t')
                .Where(s => s.Length > 0).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            double[] xyz = read("xyz");
            double[] rpy = read("rpy");
            double cr = Math.Cos(rpy[0]), sr = Math.Sin(rpy[0]);
            double cp = Math.Cos(rpy[1]), sp = Math.Sin(rpy[1]);
            double cy = Math.Cos(rpy[2]), sy = Math.Sin(rpy[2]);
            double[,] r =
            {
                { cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr },
                { sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr },
                { -sp, cp * sr, cp * cr },
            };
            return triangles.Select(t =>
            {
                double[] m = new double[9];
                for (int v = 0; v < 3; v++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        m[3 * v + i] = xyz[i] + r[i, 0] * t[3 * v] + r[i, 1] * t[3 * v + 1] + r[i, 2] * t[3 * v + 2];
                    }
                }
                return m;
            }).ToList();
        }

        // Signed tetrahedron sum over a closed triangle mesh, absolute value.
        private static double MeshVolume(List<double[]> triangles)
        {
            double sum = 0;
            foreach (double[] t in triangles)
            {
                sum += t[0] * (t[4] * t[8] - t[5] * t[7])
                     - t[1] * (t[3] * t[8] - t[5] * t[6])
                     + t[2] * (t[3] * t[7] - t[4] * t[6]);
            }
            return Math.Abs(sum / 6.0);
        }

        private static double[] BoundingBox(List<double[]> triangles)
        {
            double[] box = { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            foreach (double[] t in triangles)
            {
                for (int k = 0; k < 9; k++)
                {
                    box[k % 3] = Math.Min(box[k % 3], t[k]);
                    box[3 + k % 3] = Math.Max(box[3 + k % 3], t[k]);
                }
            }
            return box;
        }

        // Exact volume (m3) of the solid bodies of the components, in the pose and configuration they have in
        // the assembly. Sub-assemblies without bodies of their own are searched recursively.
        private static double ExactVolume(IEnumerable<Component2> components)
        {
            double volume = 0;
            foreach (Component2 component in components)
            {
                object[] bodies = (object[])component.GetBodies3((int)swBodyType_e.swSolidBody, out object _);
                if (bodies != null && bodies.Length > 0)
                {
                    foreach (Body2 body in bodies)
                    {
                        volume += ((double[])body.GetMassProperties(1))[3];
                    }
                }
                else
                {
                    object[] children = (object[])component.GetChildren();
                    if (children != null)
                    {
                        volume += ExactVolume(children.Cast<Component2>());
                    }
                }
            }
            return volume;
        }

        private static void CollectLinks(Link link, List<Link> links)
        {
            links.Add(link);
            foreach (Link child in link.Children)
            {
                CollectLinks(child, links);
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestExportRobotObjMatchesStlAndExactVolume(string modelName)
        {
            string temp = CreateRandomTempDirectory();
            try
            {
                Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
                messageBoxMock.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
                URDFPackage.MessageBox = messageBoxMock.Object;
                ModelDoc2 doc = OpenSWDocument(modelName);
                LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
                Assert.False(error);
                CommonSwOperations.LoadSWComponents(doc, baseNode, new List<string>());

                Dictionary<string, double> exact = new Dictionary<string, double>();
                ExportHelper helper = new ExportHelper(SwApp);
                helper.SavePath = temp;
                helper.CreateRobotFromTreeView(baseNode);
                List<Link> links = new List<Link>();
                CollectLinks(helper.URDFRobot.BaseLink, links);
                foreach (Link link in links)
                {
                    Assert.NotEmpty(link.SWComponents);
                    exact[link.Name] = ExactVolume(link.SWComponents);
                }

                // The OBJ keeps assembly coordinates and puts the link frame into the URDF visual origin, so each
                // format goes into its own package and is compared after applying that origin
                helper.PackageName = "fidelity_stl";
                helper.ExportRobot(true, MeshExportFormat.STL);
                helper.PackageName = "fidelity_obj";
                helper.ExportRobot(true, MeshExportFormat.OBJ);
                Assert.True(SwApp.CloseAllDocuments(true));

                string stlMeshes = Path.Combine(temp, "fidelity_stl", "meshes");
                string objMeshes = Path.Combine(temp, "fidelity_obj", "meshes");
                XDocument stlUrdf = XDocument.Load(Directory.GetFiles(Path.Combine(temp, "fidelity_stl", "urdf"), "*.urdf").Single());
                XDocument objUrdf = XDocument.Load(Directory.GetFiles(Path.Combine(temp, "fidelity_obj", "urdf"), "*.urdf").Single());
                Assert.Equal(links.Count, exact.Count);
                foreach (KeyValuePair<string, double> entry in exact)
                {
                    string name = FileNames.SanitizeFileName(entry.Key);
                    // Express both meshes in the link frame: the visual origin in the URDF maps mesh to link coordinates
                    List<double[]> stl = ApplyVisualOrigin(ReadStlTriangles(Path.Combine(stlMeshes, name + ".STL")), stlUrdf, entry.Key);
                    List<double[]> obj = ApplyVisualOrigin(ReadObjTriangles(Path.Combine(objMeshes, name + ".obj")), objUrdf, entry.Key);
                    Assert.NotEmpty(stl);
                    Assert.NotEmpty(obj);
                    Assert.True(entry.Value > 0, "No exact volume for " + name);
                    double stlVolume = MeshVolume(stl);
                    double objVolume = MeshVolume(obj);
                    string volumes = name + ": exact " + entry.Value + " STL " + stlVolume + " OBJ " + objVolume + " m3";
                    Assert.True(Math.Abs(stlVolume / entry.Value - 1) < 0.005, "STL volume off by more than 0.5 %. " + volumes);
                    Assert.True(Math.Abs(objVolume / entry.Value - 1) < 0.005, "OBJ volume off by more than 0.5 %. " + volumes);

                    double[] stlBox = BoundingBox(stl);
                    double[] objBox = BoundingBox(obj);
                    for (int k = 0; k < 6; k++)
                    {
                        // Both meshes are inscribed in curved surfaces, so allow 1 % of the largest extent for chord error
                        double tolerance = 0.01 * Math.Max(stlBox[3] - stlBox[0], Math.Max(stlBox[4] - stlBox[1], stlBox[5] - stlBox[2]));
                        Assert.True(Math.Abs(stlBox[k] - objBox[k]) < tolerance,
                            name + " bounding box differs at index " + k + ": STL " + stlBox[k] + " OBJ " + objBox[k]);
                    }
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }
        private ModelDoc2 OpenPart(string modelName, string partName)
        {
            Assert.True(SwApp.CloseAllDocuments(true));
            string filename = Path.Combine(GetModelDirectory(modelName), partName + ".SLDPRT");
            Assert.True(File.Exists(filename));
            int errors = 0;
            int warnings = 0;
            ModelDoc2 doc = SwApp.OpenDoc6(filename, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            Assert.Equal(0, errors);
            Assert.NotNull(doc);
            return doc;
        }

        // Fixture parts: BOX_RED is (1, 0, 0), BOX_BLUE is (0, 0, 1), both opaque.
        [Theory]
        [InlineData("TWO_BOXES", "BOX_RED", 1.0, 0.0, 0.0)]
        [InlineData("TWO_BOXES", "BOX_BLUE", 0.0, 0.0, 1.0)]
        public void TestExportLinkObjColor(string modelName, string partName, double r, double g, double b)
        {
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;
            string temp = CreateRandomTempDirectory();
            try
            {
                string[] mtlTexts = new string[2];
                for (int run = 0; run < 2; run++)
                {
                    OpenPart(modelName, partName);
                    ExportHelper helper = new ExportHelper(SwApp);
                    helper.SavePath = Path.Combine(temp, run.ToString());
                    helper.PackageName = partName + "_package";
                    helper.CreateRobotFromActiveModel();
                    helper.URDFRobot.Name = partName.ToLowerInvariant();
                    helper.URDFRobot.BaseLink.Name = partName.ToLowerInvariant() + "_link";
                    helper.ExportLink(true, MeshExportFormat.OBJ);
                    Assert.True(SwApp.CloseAllDocuments(true));

                    string package = Path.Combine(helper.SavePath, helper.PackageName);
                    string obj = Path.Combine(package, "meshes", helper.URDFRobot.BaseLink.Name + ".obj");
                    string mtl = Path.ChangeExtension(obj, ".mtl");
                    Assert.True(File.Exists(obj));
                    Assert.True(File.Exists(mtl));
                    Assert.Equal(12, ObjLines(obj, "f").Length); // a box: 6 sides, 2 triangles each

                    string[] names = ObjLines(mtl, "newmtl").Select(l => l.Substring(7)).ToArray();
                    string name = Assert.Single(names);
                    Assert.StartsWith(helper.URDFRobot.BaseLink.Name + "_", name);
                    double[] kd = Assert.Single(ObjLines(mtl, "Kd")).Substring(3)
                        .Split(' ').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    Assert.Equal(r, kd[0], 3);
                    Assert.Equal(g, kd[1], 3);
                    Assert.Equal(b, kd[2], 3);
                    Assert.Equal("d 1", Assert.Single(ObjLines(mtl, "d")));

                    string urdfPath = Directory.GetFiles(Path.Combine(package, "urdf"), "*.urdf").Single();
                    XDocument urdf = XDocument.Load(urdfPath);
                    Assert.All(urdf.Descendants("mesh"), m => Assert.EndsWith(".obj", (string)m.Attribute("filename")));
                    Assert.Empty(urdf.Descendants("material"));
                    mtlTexts[run] = File.ReadAllText(mtl);
                }
                Assert.Equal(mtlTexts[0], mtlTexts[1]); // reproducible
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