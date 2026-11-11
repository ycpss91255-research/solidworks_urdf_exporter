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
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using PrivateObject = Microsoft.VisualStudio.TestTools.UnitTesting.PrivateObject;
using SW2URDF.Utilities;
using Xunit;

namespace SW2URDF.Test
{
    public class TestComponentMassProperties
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThreeDxmlExportPreservesInertialAndCorrectsMeshPlacement(bool subassembly)
        {
            var helper = CreateHelper();
            var app = new Moq.Mock<ISldWorks>();
            var document = new Moq.Mock<ModelDoc2>();
            var extension = new Moq.Mock<ModelDocExtension>();
            var componentDocument = new Moq.Mock<ModelDoc2>();
            var componentExtension = new Moq.Mock<ModelDocExtension>();
            var component = new Moq.Mock<Component2>();
            var transform = new Moq.Mock<MathTransform>();
            var selection = new Moq.Mock<SelectionMgr>();
            selection.Setup(s => s.CreateSelectData()).Returns(new Moq.Mock<SelectData>().Object);
            document.SetupGet(d => d.SelectionManager).Returns(selection.Object);
            // A translated frame rotated 90 degrees about Z exposes both double translation
            // and unwanted rotation/sign changes of the off-diagonal inertia entries.
            transform.SetupGet(t => t.ArrayData).Returns(new double[] {
                0, 1, 0, -1, 0, 0, 0, 0, 1, 0.1, -0.2, 0.3, 1, 0, 0, 0 });
            document.SetupGet(d => d.Extension).Returns(extension.Object);
            componentDocument.SetupGet(d => d.Extension).Returns(componentExtension.Object);
            component.Setup(c => c.GetModelDoc2()).Returns(componentDocument.Object);
            var coordinateExtension = subassembly ? componentExtension : extension;
            coordinateExtension.Setup(e => e.GetCoordinateSystemTransformByName("frame")).Returns(transform.Object);
            helper.iSwApp = app.Object;
            helper.ActiveSWModel = document.Object;
            var link = new Link { Name = "link", SWMainComponent = component.Object };
            link.Joint.CoordinateSystemName = subassembly ? "frame <component-1>" : "frame";
            link.Inertial.Mass.Value = 2.5;
            link.Inertial.Origin.SetXYZ(new double[] { 0.01, 0.02, -0.03 });
            link.Inertial.Origin.SetRPY(new double[] { 0.1, 0.2, 0.3 });
            link.Inertial.Inertia.SetMomentMatrix(new double[] { 4, 0.1, 0.2, 0.1, 5, 0.3, 0.2, 0.3, 6 });
            double[] moment = link.Inertial.Inertia.GetMoment();
            link.Visual.Origin.SetXYZ(new double[] { 0.4, 0.5, 0.6 });
            link.Collision.Origin.SetXYZ(new double[] { 0.2, 0.3, 0.4 });

            new PrivateObject(helper).Invoke("Save3dxml", link, "unused.3dxml");

            Assert.Equal(2.5, link.Inertial.Mass.Value);
            Assert.Equal(new double[] { 0.01, 0.02, -0.03 }, link.Inertial.Origin.GetXYZ());
            Assert.Equal(new double[] { 0.1, 0.2, 0.3 }, link.Inertial.Origin.GetRPY());
            Assert.Equal(moment, link.Inertial.Inertia.GetMoment());
            double[] visual = { 0.7, -0.3, 0.3 };
            double[] collision = { 0.5, -0.1, 0.1 };
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(visual[i], link.Visual.Origin.GetXYZ()[i], 12);
                Assert.Equal(collision[i], link.Collision.Origin.GetXYZ()[i], 12);
            }
            Assert.Equal(-System.Math.PI / 2, link.Visual.Origin.Yaw, 12);
            Assert.Equal(-System.Math.PI / 2, link.Collision.Origin.Yaw, 12);
            coordinateExtension.Verify(e => e.GetCoordinateSystemTransformByName("frame"), Moq.Times.Once());
        }

        [Fact]
        public void PartLocalizationStillTransformsCenterOfMass()
        {
            var link = new Link();
            link.Inertial.Origin.SetXYZ(new double[] { 1, 2, 3 });
            var transform = MathOps.GetTranslation(new double[] { 0.1, 0.2, 0.3 });
            var method = typeof(ExportHelper).GetMethod("LocalizeLink",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(null, new object[] { link, transform });
            double[] expected = { 0.9, 1.8, 2.7 };
            for (int i = 0; i < 3; i++)
                Assert.Equal(expected[i], link.Inertial.Origin.GetXYZ()[i], 12);
        }

        private static ExportHelper CreateHelper()
        {
            return (ExportHelper)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ExportHelper));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EmptyLinkHasZeroMassWithoutQueryingSolidWorks(bool nullComponents)
        {
            var helper = CreateHelper();
            var link = new Link();
            if (nullComponents) link.SWComponents = null;
            link.Inertial.Mass.Value = 12;
            link.Inertial.Origin.X = 3;
            link.Inertial.Origin.Roll = 1;
            link.Inertial.Inertia.SetMomentMatrix(Enumerable.Repeat(7.0, 9).ToArray());
            new PrivateObject(helper).Invoke("ComputeInertialProperties", link);
            Assert.Equal(0, link.Inertial.Mass.Value);
            Assert.Equal(new double[9], link.Inertial.Inertia.GetMoment());
            Assert.Equal(new double[3], link.Inertial.Origin.GetXYZ());
            Assert.Equal(new double[3], link.Inertial.Origin.GetRPY());
        }

        [Fact]
        public void OldSolidWorksDoesNotCallUnavailableApi()
        {
            var helper = CreateHelper();
            var app = new Moq.Mock<ISldWorks>();
            app.Setup(sw => sw.RevisionNumber()).Returns("27.5.0");
            helper.iSwApp = app.Object;
            // ActiveSWModel is deliberately null: accessing the new API would fail.
            Assert.Null(new PrivateObject(helper).Invoke("GetComponentsMassProperty",
                new List<Component2>(), null));
        }
    }

    // The auto-generated coordinate systems and axes are built from a 3D sketch ("URDF Reference").
    // It used to be created only together with Origin_global, so an export whose global origin was a
    // user-made coordinate system but whose joints were "Automatically Generate" failed with
    // "Reference sketch  does not exist". These tests use a fake sketch manager that keeps the
    // state of the sketch (exists / is being edited).
    public class TestReferenceSketch
    {
        private class FakeSketchModel
        {
            public bool SketchExists;
            public bool Editing;
            public int SketchesCreated;
            public string SketchName = "3DSketch1";
            public const string ReferenceName = "URDF Reference";
            public ExportHelper Helper;
            public Moq.Mock<ModelDocExtension> Extension;

            // True while a call to CreateMassProperty2 sees a sketch in edit mode: SolidWorks
            // reports mass 0 for components then.
            public bool MassQueriedWhileEditing;

            // SolidWorks has not built the geometry while a rebuild is pending, and then reports
            // volume and mass 0 for components.
            public bool RebuildPending;
            public bool MassQueriedWhilePending;
            public int Rebuilds;

            public FakeSketchModel()
            {
                Helper = (ExportHelper)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(ExportHelper));
                var document = new Moq.Mock<ModelDoc2>();
                var extension = new Moq.Mock<ModelDocExtension>();
                var sketchManager = new Moq.Mock<SketchManager>();
                var sketch = new Moq.Mock<Sketch>();
                sketch.Setup(s => s.Is3D()).Returns(true);
                Extension = extension;
                extension.SetupGet(e => e.NeedsRebuild2).Returns(() => RebuildPending ? 1 : 0);
                document.Setup(d => d.ForceRebuild3(true)).Returns(true).Callback(() =>
                {
                    Rebuilds++;
                    RebuildPending = false;
                });
                var sketchFeature = sketch.As<IFeature>();
                sketchFeature.SetupProperty(f => f.Name);
                sketchFeature.Object.Name = SketchName;

                document.SetupGet(d => d.Extension).Returns(extension.Object);
                document.SetupGet(d => d.SketchManager).Returns(sketchManager.Object);
                extension.Setup(e => e.SelectByID2(Moq.It.IsAny<string>(), "SKETCH", 0, 0, 0, false, 0,
                    Moq.It.IsAny<Callout>(), 0))
                    .Returns((string name, string type, double x, double y, double z, bool append,
                        int mark, Callout callout, int option) => SketchExists && name == sketchFeature.Object.Name);
                sketchManager.SetupGet(m => m.ActiveSketch)
                    .Returns(() => Editing ? sketch.Object : null);
                sketchManager.Setup(m => m.Insert3DSketch(true)).Callback(() =>
                {
                    if (!Editing && !SketchExists)
                    {
                        SketchExists = true;
                        SketchesCreated++;
                    }
                    Editing = !Editing;
                });
                sketchManager.Setup(m => m.CreatePoint(Moq.It.IsAny<double>(), Moq.It.IsAny<double>(),
                    Moq.It.IsAny<double>())).Returns(new Moq.Mock<SketchPoint>().Object);
                var segment = new Moq.Mock<SketchSegment>();
                segment.SetupAllProperties();
                sketchManager.Setup(m => m.CreateLine(Moq.It.IsAny<double>(), Moq.It.IsAny<double>(),
                    Moq.It.IsAny<double>(), Moq.It.IsAny<double>(), Moq.It.IsAny<double>(),
                    Moq.It.IsAny<double>())).Returns(segment.Object);
                Helper.ActiveSWModel = document.Object;
            }
        }

        [Fact]
        public void EnsureReferenceSketchCreatesTheNamedSketchOnlyOnce()
        {
            var model = new FakeSketchModel();
            var helper = new PrivateObject(model.Helper);

            helper.Invoke("EnsureReferenceSketch");
            helper.Invoke("EnsureReferenceSketch");

            Assert.Equal(1, model.SketchesCreated);
            Assert.Equal(FakeSketchModel.ReferenceName, helper.GetField("referenceSketchName"));
        }

        [Fact]
        public void AutoGeneratedFrameBuildsItsOwnSketchWhenTheGlobalOriginIsUserMade()
        {
            // No Origin_global was created, so referenceSketchName was never set.
            var model = new FakeSketchModel();
            var helper = new PrivateObject(model.Helper);

            var geometry = (object[])helper.Invoke("AddSketchGeometry",
                new Type[] { typeof(Origin) }, new object[] { new Origin(true) });

            Assert.NotNull(geometry);
            Assert.Equal(1, model.SketchesCreated);
            Assert.Equal(FakeSketchModel.ReferenceName, helper.GetField("referenceSketchName"));
        }

        [Fact]
        public void AutoGeneratedAxisBuildsItsOwnSketchWhenTheGlobalOriginIsUserMade()
        {
            var model = new FakeSketchModel();
            var helper = new PrivateObject(model.Helper);
            var axis = new Axis();
            axis.SetXYZ(new double[] { 0, 0, 1 });

            // The axis direction check afterwards needs a real coordinate system, which the fake does
            // not have; only the sketch part matters here. Before the fix the sketch was created
            // anonymously ("3DSketch1") and the later lookup by name could not find it.
            try
            {
                helper.Invoke("AddSketchGeometry", new Type[] { typeof(Axis), typeof(Origin), typeof(string) },
                    new object[] { axis, new Origin(true), "Origin_x" });
            }
            catch (Exception e)
            {
                Assert.DoesNotContain("does not exist", e.ToString());
            }

            Assert.Equal(1, model.SketchesCreated);
            Assert.Equal(FakeSketchModel.ReferenceName, helper.GetField("referenceSketchName"));
        }

        // Insert3DSketch(true) toggles. When a sketch is already open (left over, or the user's
        // own) the first call used to close it and the second one reopened it, so the reference
        // sketch stayed in edit mode after the coordinate system was built.
        [Fact]
        public void BuildingTheReferenceSketchNeverLeavesASketchOpen()
        {
            var model = new FakeSketchModel { Editing = true };
            var helper = new PrivateObject(model.Helper);

            helper.Invoke("EnsureReferenceSketch");

            Assert.False(model.Editing, "a sketch is still in edit mode");
            Assert.Equal(1, model.SketchesCreated);
        }

        [Fact]
        public void AddingGeometryWhileTheReferenceSketchIsOpenLeavesItClosed()
        {
            var model = new FakeSketchModel();
            var helper = new PrivateObject(model.Helper);

            helper.Invoke("AddSketchGeometry",
                new Type[] { typeof(Origin) }, new object[] { new Origin(true) });
            model.Editing = true;
            helper.Invoke("AddSketchGeometry",
                new Type[] { typeof(Origin) }, new object[] { new Origin(true) });

            Assert.False(model.Editing, "a sketch is still in edit mode");
        }

        // Mass properties of components come out as 0 while a sketch is in edit mode.
        [Fact]
        public void MassPropertiesAreNeverQueriedWithASketchOpen()
        {
            var model = new FakeSketchModel { Editing = true };
            var app = new Moq.Mock<ISldWorks>();
            app.Setup(a => a.RevisionNumber()).Returns("31.5.0");
            model.Helper.iSwApp = app.Object;
            var mass = new Moq.Mock<IMassProperty2>();
            mass.Setup(m => m.Recalculate()).Returns(true);
            model.Extension.Setup(e => e.CreateMassProperty2()).Returns(mass.Object)
                .Callback(() => model.MassQueriedWhileEditing = model.Editing);
            var helper = new PrivateObject(model.Helper);

            helper.Invoke("GetComponentsMassProperty", new List<Component2>(), null);

            Assert.False(model.MassQueriedWhileEditing, "CreateMassProperty2 ran with a sketch open");
        }

        [Theory]
        [InlineData(0.0, 1, true)]
        [InlineData(0.0, 0, false)]
        [InlineData(0.0375, 1, false)]
        [InlineData(0.0375, 0, false)]
        public void ZeroMassIsRecalculatedOnlyWhileARebuildIsPending(double mass, int rebuildStatus, bool retry)
        {
            Assert.Equal(retry, ExportHelper.MassNeedsRetry(mass, rebuildStatus));
        }

        // Right after opening a model, and after the exporter inserted a coordinate system, the
        // model can have a rebuild pending. The first export then wrote mass 0 for a link and the
        // next one, once the rebuild had happened, the right value.
        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public void MassPropertiesAreNeverQueriedWithARebuildPending(bool pending, int expectedRebuilds)
        {
            var model = new FakeSketchModel { RebuildPending = pending };
            var app = new Moq.Mock<ISldWorks>();
            app.Setup(a => a.RevisionNumber()).Returns("31.5.0");
            model.Helper.iSwApp = app.Object;
            var mass = new Moq.Mock<IMassProperty2>();
            mass.Setup(m => m.Recalculate()).Returns(true);
            model.Extension.Setup(e => e.CreateMassProperty2()).Returns(mass.Object)
                .Callback(() => model.MassQueriedWhilePending = model.RebuildPending);
            var helper = new PrivateObject(model.Helper);

            helper.Invoke("GetComponentsMassProperty", new List<Component2>(), null);

            Assert.False(model.MassQueriedWhilePending, "CreateMassProperty2 ran with a rebuild pending");
            Assert.Equal(expectedRebuilds, model.Rebuilds);
        }
    }

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

        /// <summary>
        /// A link's exported mass and moment of inertia must match what SolidWorks itself reports
        /// for that link's components, which is what a user compares the export against.
        /// Computing these from the components' bodies instead silently drops mass properties the
        /// user has overridden, and misplaces bodies that belong to a subassembly.
        ///
        /// Both quantities asserted here are independent of the coordinate system: mass trivially
        /// so, and the trace of the inertia tensor about the center of mass because it is
        /// invariant under rotation. That keeps the test from having to resolve the link's
        /// coordinate system, which for a subassembly lives in a subcomponent.
        /// </summary>
        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        [InlineData("ORIGINAL_3_DOF_ARM")]
        public void TestInertialMatchesSolidWorks(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
            Assert.False(error);

            // The saved configuration stores components as persistent IDs. The export UI resolves
            // them before building the robot, so do the same here.
            List<string> problemLinks = new List<string>();
            CommonSwOperations.LoadSWComponents(doc, baseNode, problemLinks);
            Assert.Empty(problemLinks);

            Assert.True(helper.CreateRobotFromTreeView(baseNode));

            double totalMass = doc.Extension.CreateMassProperty().Mass;
            int linksChecked = 0;

            foreach (Link link in Flatten(helper.URDFRobot.BaseLink))
            {
                if (link.SWComponents.Count == 0)
                {
                    continue;
                }

                IMassProperty2 expected = (IMassProperty2)doc.Extension.CreateMassProperty2();
                Assert.NotNull(expected);
                expected.UseSystemUnits = true;
                expected.IncludeHiddenBodiesOrComponents = true;
                expected.SelectedItems = link.SWComponents.ToArray();
                Assert.True(expected.Recalculate());

                Assert.Equal(expected.Mass, link.Inertial.Mass.Value, 9);

                double[] expectedMoment = (double[])expected.GetMomentOfInertia(
                    (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
                double expectedTrace = expectedMoment[0] + expectedMoment[4] + expectedMoment[8];
                double actualTrace = link.Inertial.Inertia.Ixx +
                    link.Inertial.Inertia.Iyy + link.Inertial.Inertia.Izz;
                Assert.Equal(expectedTrace, actualTrace, 12);

                // A link whose parts carry no mass in the model (for example no material assigned) is
                // legitimately massless. The equality check above already proves it matches SolidWorks.
                if (expected.Mass > 0.0)
                {
                    Assert.True(link.Inertial.Mass.Value > 0.0, "link " + link.Name + " has no mass");
                }

                // An ignored or empty selection degrades into the whole document's properties.
                // No single link of these robots is the entire assembly.
                Assert.True(link.Inertial.Mass.Value < totalMass,
                    "link " + link.Name + " has the mass of the whole assembly");

                linksChecked++;
            }

            Assert.True(linksChecked > 0, "no links were checked for " + modelName);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("ORIGINAL_3_DOF_ARM")]
        public void TestInertialHonorsMassOverride(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            try
            {
                var assembly = (AssemblyDoc)doc;
                assembly.ResolveAllLightWeightComponents(true);
                Component2 component = ((object[])assembly.GetComponents(false))
                    .Cast<Component2>().OrderBy(comp => comp.Name2).First(comp =>
                        ((ModelDoc2)comp.GetModelDoc2()).GetType() == (int)swDocumentTypes_e.swDocPART);
                var part = (ModelDoc2)component.GetModelDoc2();
                if (part.ConfigurationManager.ActiveConfiguration.Name != component.ReferencedConfiguration)
                {
                    Assert.True(part.ShowConfiguration2(component.ReferencedConfiguration),
                        "Could not activate component configuration " + component.ReferencedConfiguration);
                }
                part.ClearSelection2(true);
                var partMass = (IMassProperty2)part.Extension.CreateMassProperty2();
                partMass.UseSystemUnits = true;
                var identity = (MathTransform)((MathUtility)SwApp.GetMathUtility()).CreateTransform(
                    new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 });
                Assert.True(partMass.SetCoordinateSystem(identity));
                Assert.True(partMass.Recalculate());
                double originalMass = partMass.Mass;
                double[] originalMoment = (double[])partMass.GetMomentOfInertia(
                    (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
                double overriddenMass = originalMass + 2.5;
                var options = (IMassPropertyOverrideOptions)partMass.GetOverrideOptions();
                options.OverrideMass = true;
                options.OverrideCenterOfMass = false;
                options.OverrideMomentsOfInertia = false;
                Assert.True(options.SetOverrideMassValue(overriddenMass));
                Assert.True(partMass.SetOverrideOptions(options,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null));
                part.ForceRebuild3(false);
                doc.ForceRebuild3(false);
                doc.ClearSelection2(true);

                var helper = new ExportHelper(SwApp);
                var access = new PrivateObject(helper);
                var node = ConfigurationSerialization.LoadBaseNodeFromModel(doc, out bool error);
                Assert.False(error);
                var link = new Link { Name = "overridden_part" };
                link.SWComponents.Add(component);
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
                var frame = (MathTransform)access.Invoke("GetCoordinateSystemTransform",
                    link.Joint.CoordinateSystemName);
                Assert.NotNull(frame);

                access.Invoke("ComputeInertialProperties", link);
                Assert.Equal(overriddenMass, link.Inertial.Mass.Value, 9);

                // Independently transform the part's own CoM and inertia into the link frame.
                part.ClearSelection2(true);
                var expected = (IMassProperty2)part.Extension.CreateMassProperty2();
                expected.UseSystemUnits = true;
                Assert.True(expected.SetCoordinateSystem(identity));
                Assert.True(expected.Recalculate());
                double[] overriddenMoment = (double[])expected.GetMomentOfInertia(
                    (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
                double massRatio = overriddenMass / originalMass;
                for (int i = 0; i < 9; i++)
                {
                    Assert.Equal(originalMoment[i] * massRatio, overriddenMoment[i], 9);
                }
                System.Console.WriteLine("{0}: mass ratio={1:R}, Ixx ratio={2:R}",
                    modelName, massRatio, overriddenMoment[0] / originalMoment[0]);
                var relative = MathOps.GetTransformation(frame).Inverse() *
                    MathOps.GetTransformation(component.Transform2);
                double[] center = (double[])expected.CenterOfMass;
                var point = MathNet.Numerics.LinearAlgebra.Double.DenseVector.OfArray(
                    new double[] { center[0], center[1], center[2], 1 });
                var transformedCenter = relative * point;
                double[] actualCenter = link.Inertial.Origin.GetXYZ();
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(transformedCenter[i], actualCenter[i], 9);
                }
                var moment = new MathNet.Numerics.LinearAlgebra.Double.DenseMatrix(3, 3,
                    (double[])expected.GetMomentOfInertia(
                        (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass)).Transpose();
                var rotation = relative.SubMatrix(0, 3, 0, 3);
                double[] expectedMoment = (rotation * moment * rotation.Transpose()).ToRowMajorArray();
                double[] actualMoment = link.Inertial.Inertia.GetMoment();
                for (int i = 0; i < 9; i++)
                {
                    Assert.Equal(expectedMoment[i], actualMoment[i], 9);
                }

                // Demonstrate that this fixture actually catches the original body-based bug.
                var oldLink = new Link { Name = "old_calculation" };
                oldLink.SWComponents.Add(component);
                access.Invoke("ComputeInertialPropertiesFromBodies", oldLink, frame);
                Assert.True(System.Math.Abs(oldLink.Inertial.Mass.Value - overriddenMass) > 1.0);
                System.Console.WriteLine("{0}: assigned={1:R} kg, exported={2:R} kg, old={3:R} kg",
                    modelName, overriddenMass, link.Inertial.Mass.Value, oldLink.Inertial.Mass.Value);

                // Combining an overridden part with another part must add their masses.
                Component2 other = ((object[])assembly.GetComponents(false)).Cast<Component2>()
                    .First(comp => comp.GetPathName() != component.GetPathName() &&
                        ((ModelDoc2)comp.GetModelDoc2()).GetType() == (int)swDocumentTypes_e.swDocPART);
                var otherLink = new Link { Name = "other_part" };
                otherLink.Joint.CoordinateSystemName = link.Joint.CoordinateSystemName;
                otherLink.SWComponents.Add(other);
                access.Invoke("ComputeInertialProperties", otherLink);
                Assert.True(otherLink.Inertial.Mass.Value > 0);
                link.SWComponents.Add(other);
                access.Invoke("ComputeInertialProperties", link);
                Assert.Equal(overriddenMass + otherLink.Inertial.Mass.Value, link.Inertial.Mass.Value, 9);
            }
            finally
            {
                // Never save the temporary override to the example files.
                SwApp.CloseAllDocuments(true);
            }
        }

        private static IEnumerable<Link> Flatten(Link link)
        {
            yield return link;
            foreach (Link child in link.Children)
            {
                foreach (Link descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    public class TestSTLValidation
    {
        [Fact]
        public void TestCorrectSTLMeshClearsHeaderAndPreservesGeometry()
        {
            string path = Path.GetTempFileName();
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(path)))
                {
                    writer.Write(new byte[80]);
                    writer.Write((uint)1);
                    writer.Write(new byte[50]);
                }

                Assert.True(ExportHelper.CorrectSTLMesh(path));
                byte[] data = File.ReadAllBytes(path);
                Assert.Equal(134, data.Length);
                Assert.Equal((uint)1, BitConverter.ToUInt32(data, 80));
                for (int i = 0; i < 80; i++)
                {
                    Assert.Equal(0, data[i]);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TestCorrectSTLMeshRejectsHeaderOnlyFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[80]);
                Assert.False(ExportHelper.CorrectSTLMesh(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TestCorrectSTLMeshWaitsForFileRelease()
        {
            string path = Path.GetTempFileName();
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(path)))
                {
                    writer.Write(new byte[80]);
                    writer.Write((uint)1);
                    writer.Write(new byte[50]);
                }

                FileStream lockedFile = new FileStream(
                    path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Task releaseLock = Task.Run(() =>
                {
                    Thread.Sleep(300);
                    lockedFile.Dispose();
                });

                Assert.True(ExportHelper.CorrectSTLMesh(path));
                releaseLock.Wait();
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TestCorrectSTLMeshRejectsTruncatedTriangleData()
        {
            string path = Path.GetTempFileName();
            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(path)))
                {
                    writer.Write(new byte[80]);
                    writer.Write((uint)2);
                    writer.Write(new byte[50]);
                }

                Assert.False(ExportHelper.CorrectSTLMesh(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    public class TestJointGeometry
    {
        [Theory]
        [InlineData("fixed", false)]
        [InlineData("revolute", true)]
        [InlineData("continuous", true)]
        [InlineData("prismatic", true)]
        [InlineData("Automatically Detect", true)]
        public void TestJointUsesAxis(string jointType, bool expected)
        {
            Assert.Equal(expected, ExportHelper.JointUsesAxis(jointType));
        }
    }
}
