// Offline xUnit tests for the 3DXML -> OBJ/MTL converter (no SOLIDWORKS needed). They use small
// synthetic 3DXML archives that follow the layout SOLIDWORKS writes. The class has a parameterless
// constructor so a reflection runner (scripts/test_logic.ps1) can invoke the facts directly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
public class TestObjConverter
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static void Check(bool condition, string message)
    {
        Assert.True(condition, message);
    }

    static void CheckNear(double actual, double expected, double tolerance, string message)
    {
        Check(Math.Abs(actual - expected) <= tolerance, message + " (expected " + expected + ", got " + actual + ")");
    }

    // ---- synthetic 3DXML -----------------------------------------------------------

    class Instance
    {
        public string Matrix;           // 12 numbers: three rotation columns, then translation
        public Instance(string matrix) { Matrix = matrix; }
    }

    class Options
    {
        public double[] FaceColor;      // r g b a on every face, or null
        public bool Material;           // add the rendering material library
        public double Transparency;     // material transparency
        public bool Texture;            // texture coordinates plus a texture image
        public bool Empty;              // no triangles
        public bool Shared;             // 8 shared corner vertices instead of 24 split ones
        public List<string> Overrides = new List<string>();   // "ids|r g b a"
    }

    // A 20 mm cube centred on the origin. Six sides use strips, triangle lists and fans.
    // Like SOLIDWORKS, every Face sits in its own Faces element.
    static string CubeRep(Options o)
    {
        double h = 10;
        double[][] corners = {
            new[]{-h,-h,-h}, new[]{h,-h,-h}, new[]{h,h,-h}, new[]{-h,h,-h},
            new[]{-h,-h,h}, new[]{h,-h,h}, new[]{h,h,h}, new[]{-h,h,h} };
        // Sides as counter-clockwise quads seen from outside.
        int[][] sides = {
            new[]{0,3,2,1}, new[]{4,5,6,7}, new[]{0,1,5,4}, new[]{1,2,6,5}, new[]{2,3,7,6}, new[]{3,0,4,7} };
        var positions = new StringBuilder();
        var faces = new StringBuilder();
        string color = o.FaceColor == null ? "" : string.Format(Inv,
            "<SurfaceAttributes xsi:type=\"SurfaceAttributesType\"><Color xsi:type=\"RGBAColorType\" red=\"{0}\" green=\"{1}\" blue=\"{2}\" alpha=\"{3}\"/></SurfaceAttributes>",
            o.FaceColor[0], o.FaceColor[1], o.FaceColor[2], o.FaceColor[3]);
        for (int s = 0; s < 6; s++)
        {
            if (!o.Shared)
            {
                foreach (int c in sides[s])
                {
                    if (positions.Length > 0) positions.Append(',');
                    positions.Append(string.Format(Inv, "{0} {1} {2}", corners[c][0], corners[c][1], corners[c][2]));
                }
            }
            int[] index = o.Shared ? sides[s] : new[] { s * 4, s * 4 + 1, s * 4 + 2, s * 4 + 3 };
            int b0 = index[0], b1 = index[1], b2 = index[2], b3 = index[3];
            string attribute;
            if (s < 2) attribute = string.Format("strips=\"{0} {1} {3} {2}\"", b0, b1, b2, b3);
            else if (s < 4) attribute = string.Format("triangles=\"{0} {1} {2} {0} {2} {3}\"", b0, b1, b2, b3);
            else attribute = string.Format("fans=\"{0} {1} {2} {3}\"", b0, b1, b2, b3);
            faces.Append("<Faces><Face " + attribute + ">" + color + "</Face></Faces>");
        }
        if (o.Shared)
            positions.Append(string.Join(",", corners.Select(c => string.Format(Inv, "{0} {1} {2}", c[0], c[1], c[2]))));
        string normals = string.Join(",", Enumerable.Repeat("0 0 1", o.Shared ? 8 : 24));
        if (o.Empty) faces.Clear();
        string material = o.Material
            ? "<SurfaceAttributes xsi:type=\"SurfaceAttributesType\"><MaterialApplication mappingChannel=\"0\" blendType=\"REPLACE\" mappingSide=\"FRONT_AND_BACK\"><MaterialId id=\"urn:3DXML:CATMaterialRef.3dxml#0\"/></MaterialApplication></SurfaceAttributes>"
            : "";
        string uv = o.Texture ? "<TextureCoordinates dimension=\"2\">" + string.Join(",", Enumerable.Repeat("0 0", o.Shared ? 8 : 24)) + "</TextureCoordinates>" : "";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><XMLRepresentation version=\"1.2\" xmlns=\"http://www.3ds.com/xsd/3DXML\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<Root xsi:type=\"BagRepType\"><Rep xsi:type=\"BagRepType\"><Rep xsi:type=\"PolygonalRepType\">" + faces
            + "<VertexBuffer><Positions>" + positions + "</Positions><Normals>" + normals + "</Normals>" + uv + "</VertexBuffer>"
            + material + "</Rep></Rep></Root></XMLRepresentation>";
    }

    static void Build3dxml(string path, IList<Instance> instances, Options o)
    {
        var structure = new StringBuilder();
        structure.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Model_3dxml xmlns=\"http://www.3ds.com/xsd/3DXML\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Header><SchemaVersion>4.2</SchemaVersion></Header><ProductStructure root=\"1\">");
        structure.Append("<Reference3D xsi:type=\"Reference3DType\" id=\"1\" name=\"root\"/><Reference3D xsi:type=\"Reference3DType\" id=\"2\" name=\"box\"/>");
        structure.Append("<ReferenceRep xsi:type=\"ReferenceRepType\" id=\"3\" format=\"TESSELLATED\" associatedFile=\"urn:3DXML:Tess_3.3DRep\" version=\"1.2\"/>");
        structure.Append("<InstanceRep xsi:type=\"InstanceRepType\" id=\"4\"><IsAggregatedBy>2</IsAggregatedBy><IsInstanceOf>3</IsInstanceOf></InstanceRep>");
        for (int i = 0; i < instances.Count; i++)
            structure.Append(string.Format("<Instance3D xsi:type=\"Instance3DType\" id=\"{0}\" name=\"box-{1}\"><IsAggregatedBy>1</IsAggregatedBy><IsInstanceOf>2</IsInstanceOf><RelativeMatrix>{2}</RelativeMatrix></Instance3D>",
                10 + i, i + 1, instances[i].Matrix));
        structure.Append("</ProductStructure><DefaultView>");
        foreach (string entry in o.Overrides)
        {
            string[] parts = entry.Split('|');
            string[] c = parts[1].Split(' ');
            structure.Append("<DefaultViewProperty><OccurenceId>");
            foreach (string id in parts[0].Split(' ')) structure.Append("<id>urn:3DXML:NameSetTree.3dxml#" + id + "</id>");
            structure.Append("</OccurenceId><GraphicProperties><SurfaceAttributes><Color xsi:type=\"RGBAColorType\" red=\"" + c[0]
                + "\" green=\"" + c[1] + "\" blue=\"" + c[2] + "\" alpha=\"" + c[3] + "\"/></SurfaceAttributes></GraphicProperties></DefaultViewProperty>");
        }
        structure.Append("</DefaultView></Model_3dxml>");

        if (File.Exists(path)) File.Delete(path);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "Manifest.xml", "<Manifest><Root>NameSetTree.3dxml</Root></Manifest>");
            Add(zip, "NameSetTree.3dxml", structure.ToString());
            Add(zip, "Tess_3.3DRep", CubeRep(o));
            if (o.Material)
            {
                Add(zip, "CATMaterialRef.3dxml", "<Model_3dxml xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://www.3ds.com/xsd/3DXML\"><CATMaterialRef>"
                    + "<CATMatReference xsi:type=\"CATMatReferenceType\" id=\"0\" name=\"Material_0\"/>"
                    + "<MaterialDomain xsi:type=\"MaterialDomainType\" id=\"1\" name=\"Material_0_Rendering\" format=\"TECHREP\" associatedFile=\"urn:3DXML:MaterialDef_0.3DRep\" version=\"1.0\"/>"
                    + "<MaterialDomainInstance xsi:type=\"MaterialDomainInstanceType\" id=\"2\" name=\"Material_0.1\"><IsAggregatedBy>0</IsAggregatedBy><IsInstanceOf>1</IsInstanceOf></MaterialDomainInstance>"
                    + "</CATMaterialRef></Model_3dxml>");
                Add(zip, "MaterialDef_0.3DRep", "<Osm xmlns=\"http://www.3ds.com/xsd/osm.xsd\"><Feature Alias=\"RenderingRootFeature\" Id=\"1\"/>"
                    + "<Feature Alias=\"RenderingFeature\" Id=\"2\"><Attr Name=\"DiffuseColor\" Type=\"double\" Value=\"[0.25,0.5,0.75]\"/>"
                    + "<Attr Name=\"SpecularCoef\" Type=\"double\" Value=\"0.5\"/><Attr Name=\"SpecularColor\" Type=\"double\" Value=\"[1.000000,1.000000,1.000000]\"/>"
                    + "<Attr Name=\"SpecularExponent\" Type=\"double\" Value=\"0.216\"/>"
                    + "<Attr Name=\"Transparency\" Type=\"double\" Value=\"" + o.Transparency.ToString(Inv) + "\"/>"
                    + (o.Texture ? "<Attr Name=\"TextureImage\" Type=\"string\" Value=\"urn:3DXML:Image.3dxml#1\"/>" : "")
                    + "</Feature></Osm>");
            }
        }
    }

    static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(content);
    }

    // ---- OBJ / MTL parsing ----------------------------------------------------------

    class Obj
    {
        public List<double[]> V = new List<double[]>();
        public List<double[]> N = new List<double[]>();
        public List<int[]> F = new List<int[]>();
        public List<string> FaceMaterial = new List<string>();
        public List<string> Objects = new List<string>();
        public string MtlLib;
        public List<string> Lines = new List<string>();
    }

    static Obj ReadObj(string path)
    {
        var obj = new Obj();
        string material = null;
        foreach (string line in File.ReadAllLines(path))
        {
            obj.Lines.Add(line);
            string[] f = line.Split(' ');
            switch (f[0])
            {
                case "v": obj.V.Add(f.Skip(1).Select(x => double.Parse(x, Inv)).ToArray()); break;
                case "vn": obj.N.Add(f.Skip(1).Select(x => double.Parse(x, Inv)).ToArray()); break;
                case "f":
                    obj.F.Add(f.Skip(1).Select(x => int.Parse(x.Split('/')[0], Inv) - 1).ToArray());
                    obj.FaceMaterial.Add(material);
                    Check(f.Skip(1).All(x => { string[] p = x.Split('/'); return p.Length == 3 && p[1] == "" && p[0] == p[2]; }), "face uses v//vn");
                    break;
                case "usemtl": material = string.Join(" ", f.Skip(1)); break;
                case "o": obj.Objects.Add(string.Join(" ", f.Skip(1))); break;
                case "mtllib": obj.MtlLib = string.Join(" ", f.Skip(1)); break;
            }
        }
        return obj;
    }

    class Mtl : Dictionary<string, Dictionary<string, string>> { }

    static Mtl ReadMtl(string path)
    {
        var mtl = new Mtl();
        Dictionary<string, string> current = null;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#") || line.Trim().Length == 0) continue;
            int space = line.IndexOf(' ');
            string key = line.Substring(0, space), value = line.Substring(space + 1);
            if (key == "newmtl") { current = new Dictionary<string, string>(); mtl[value] = current; }
            else current[key] = value;
        }
        return mtl;
    }

    static double[] Numbers(string value)
    {
        return value.Split(' ').Select(x => double.Parse(x, Inv)).ToArray();
    }

    static double[] Cross(double[] a, double[] b)
    {
        return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }

    static double[] Sub(double[] a, double[] b)
    {
        return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    }

    static ObjConversionResult Convert(string source, string target, string link, CancellationToken token = default(CancellationToken))
    {
        return ObjConverter.Convert(source, target, new ObjConversionOptions { LinkName = link, CancellationToken = token });
    }

    // ---- tests ----------------------------------------------------------------------

    static void TestInstanceTransformUnitsAndNormals(string dir)
    {
        string source = Path.Combine(dir, "instances.3dxml"), target = Path.Combine(dir, "instances.obj");
        // Instance 1: 90 degrees about Z (columns: (0,1,0) (-1,0,0) (0,0,1)) and (1000,2000,3000) mm; instance 2: identity.
        Build3dxml(source, new[] { new Instance("0 1 0 -1 0 0 0 0 1 1000 2000 3000"), new Instance("1 0 0 0 1 0 0 0 1 0 0 0") },
            new Options { FaceColor = new[] { 0.0, 0.0, 1.0, 1.0 } });
        ObjConversionResult result = Convert(source, target, "base_link");
        Obj obj = ReadObj(target);
        Check(result.Triangles == 24 && obj.F.Count == 24, "12 triangles per instance, strips/lists/fans all expanded");
        Check(obj.V.Count == 48 && obj.N.Count == 48, "one vn per v");
        Check(obj.MtlLib == "instances.mtl", "mtllib names the sibling MTL");
        // Instance 1 vertex 0 is (-10,-10,-10) mm -> rotated (10,-10,-10) -> plus translation, in metres.
        CheckNear(obj.V[0][0], 1.010, 1e-12, "x in metres");
        CheckNear(obj.V[0][1], 1.990, 1e-12, "y in metres");
        CheckNear(obj.V[0][2], 2.990, 1e-12, "z in metres");
        CheckNear(obj.V[24][0], -0.010, 1e-12, "identity instance x in metres");
        // Normals of a cube vertex shared by three sides do not exist (24 split vertices): unit and axis aligned.
        foreach (double[] n in obj.N)
        {
            CheckNear(Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]), 1.0, 1e-9, "unit normal");
            CheckNear(Math.Abs(n[0]) + Math.Abs(n[1]) + Math.Abs(n[2]), 1.0, 1e-9, "axis-aligned normal");
        }
        // Every triangle faces away from its instance centre.
        for (int t = 0; t < obj.F.Count; t++)
        {
            double[] a = obj.V[obj.F[t][0]], b = obj.V[obj.F[t][1]], c = obj.V[obj.F[t][2]];
            double[] centre = t < 12 ? new[] { 1.0, 2.0, 3.0 } : new[] { 0.0, 0.0, 0.0 };
            double[] normal = Cross(Sub(b, a), Sub(c, a));
            double[] outward = Sub(new[] { (a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3 }, centre);
            Check(normal[0] * outward[0] + normal[1] * outward[1] + normal[2] * outward[2] > 0, "outward winding, triangle " + t);
        }
        Check(obj.Objects.SequenceEqual(new[] { "base_link_box-1", "base_link_box-2" }), "one object per instance, link-prefixed: " + string.Join(",", obj.Objects));
        Check(Directory.GetFiles(dir, ".*.tmp").Length == 0, "no temporary files left");
    }

    static void TestMirroredInstanceKeepsOutwardWinding(string dir)
    {
        string source = Path.Combine(dir, "mirror.3dxml"), target = Path.Combine(dir, "mirror.obj");
        Build3dxml(source, new[] { new Instance("-1 0 0 0 1 0 0 0 1 500 0 0") }, new Options { FaceColor = new[] { 0.5, 0.5, 0.5, 1.0 } });
        Convert(source, target, "mirror_link");
        Obj obj = ReadObj(target);
        CheckNear(obj.V[0][0], 0.5 + 0.010, 1e-12, "mirrored x");
        for (int t = 0; t < obj.F.Count; t++)
        {
            double[] a = obj.V[obj.F[t][0]], b = obj.V[obj.F[t][1]], c = obj.V[obj.F[t][2]];
            double[] normal = Cross(Sub(b, a), Sub(c, a));
            double[] outward = Sub(new[] { (a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3 }, new[] { 0.5, 0.0, 0.0 });
            Check(normal[0] * outward[0] + normal[1] * outward[1] + normal[2] * outward[2] > 0, "mirrored winding reversed, triangle " + t);
        }
        // Vertex normals must agree with the corrected winding.
        for (int t = 0; t < obj.F.Count; t++)
        {
            double[] a = obj.V[obj.F[t][0]], b = obj.V[obj.F[t][1]], c = obj.V[obj.F[t][2]];
            double[] normal = Cross(Sub(b, a), Sub(c, a));
            double[] vn = obj.N[obj.F[t][0]];
            Check(normal[0] * vn[0] + normal[1] * vn[1] + normal[2] * vn[2] > 0, "vn agrees with winding, triangle " + t);
        }
    }

    static void TestColorOverrideAndOpacity(string dir)
    {
        string source = Path.Combine(dir, "colors.3dxml"), target = Path.Combine(dir, "colors.obj");
        var o = new Options { FaceColor = new[] { 0.0, 0.0, 1.0, 1.0 } };
        o.Overrides.Add("1 10|1 0.25 0 0.5");     // only occurrence 10 (the first instance)
        Build3dxml(source, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0"), new Instance("1 0 0 0 1 0 0 0 1 50 0 0") }, o);
        Convert(source, target, "arm link");
        Obj obj = ReadObj(target);
        Mtl mtl = ReadMtl(Path.ChangeExtension(target, ".mtl"));
        Check(mtl.Count == 2, "two materials: override and native colour");
        string over = obj.FaceMaterial[0], native = obj.FaceMaterial[12];
        Check(over == "arm_link_ff4000_a50", "override material name: " + over);
        Check(native == "arm_link_0000ff", "native material name: " + native);
        Check(obj.FaceMaterial.Take(12).All(m => m == over) && obj.FaceMaterial.Skip(12).All(m => m == native), "per-occurrence assignment");
        CheckNear(Numbers(mtl[over]["Kd"])[1], 0.25, 1e-12, "override Kd");
        Check(mtl[over]["d"] == "0.5", "override opacity: " + mtl[over]["d"]);
        Check(mtl[native]["d"] == "1", "opaque d");
        Check(!mtl[over].ContainsKey("Tr") && !mtl[native].ContainsKey("Tr"), "Tr is not written");
        Check(obj.Objects[0].StartsWith("arm_link_"), "object names use the sanitized link name");
    }

    static void TestMaterialFields(string dir)
    {
        string source = Path.Combine(dir, "material.3dxml"), target = Path.Combine(dir, "material.obj");
        Build3dxml(source, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0") }, new Options { Material = true, Transparency = 0.25 });
        Convert(source, target, "link_a");
        Mtl mtl = ReadMtl(Path.ChangeExtension(target, ".mtl"));
        Check(mtl.Count == 1, "one material");
        Dictionary<string, string> m = mtl.Values.First();
        string[] keys = { "Ka", "Kd", "Ks", "Ns", "d", "illum" };
        Check(keys.All(k => m.ContainsKey(k)), "Ka Kd Ks Ns d illum all present");
        Check(m["Kd"] == "0.25 0.5 0.75" && m["Ka"] == m["Kd"], "Ka equals Kd; Kd from material diffuse: " + m["Kd"]);
        Check(m["Ks"] == "0.5 0.5 0.5", "Ks = specular colour * coefficient: " + m["Ks"]);
        Check(m["Ns"] == "216", "Ns = shininess * 1000: " + m["Ns"]);
        Check(m["d"] == "0.75", "d = 1 - transparency: " + m["d"]);
        Check(m["illum"] == "2", "illum 2 with specular");
        Check(mtl.Keys.First() == "link_a_4080bf_a75", "stable name: " + mtl.Keys.First());

        // No material: fixed defaults, no specular, illum 1.
        string plain = Path.Combine(dir, "plain.3dxml"), plainTarget = Path.Combine(dir, "plain.obj");
        Build3dxml(plain, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0") }, new Options());
        Convert(plain, plainTarget, "link_a");
        Dictionary<string, string> d = ReadMtl(Path.ChangeExtension(plainTarget, ".mtl")).Values.First();
        Check(d["Ks"] == "0 0 0" && d["Ns"] == "0" && d["illum"] == "1" && d["d"] == "1", "defaults without a material");
        Check(d["Kd"] == "0.5 0.5 0.5", "default grey");
    }

    static void TestRepeatableOutput(string dir)
    {
        string source = Path.Combine(dir, "repeat.3dxml");
        var o = new Options { FaceColor = new[] { 0.1, 0.2, 0.3, 0.9 } };
        o.Overrides.Add("1 11|0.9 0.8 0.7 1");
        Build3dxml(source, new[] { new Instance("0 1 0 -1 0 0 0 0 1 1 2 3"), new Instance("1 0 0 0 1 0 0 0 1 4 5 6") }, o);
        string a = Path.Combine(dir, "repeat-a.obj"), b = Path.Combine(dir, "repeat-b.obj");
        Convert(source, a, "link");
        Convert(source, b, "link");
        Check(File.ReadAllText(a).Replace("repeat-a", "X") == File.ReadAllText(b).Replace("repeat-b", "X"), "OBJ is reproducible");
        Check(File.ReadAllText(Path.ChangeExtension(a, ".mtl")) == File.ReadAllText(Path.ChangeExtension(b, ".mtl")), "MTL is reproducible");
        Check(!File.ReadAllText(a).Contains("\r"), "LF line endings");
        Check(File.ReadAllBytes(a)[0] != 0xEF, "no byte order mark");
    }

    static void TestFailuresKeepExistingOutput(string dir)
    {
        string target = Path.Combine(dir, "keep.obj"), mtl = Path.ChangeExtension(target, ".mtl");
        File.WriteAllText(target, "existing OBJ"); File.WriteAllText(mtl, "existing MTL");
        int before = Directory.GetFiles(dir).Length;

        Action<Action, Type, string> expect = (action, type, what) =>
        {
            try { action(); }
            catch (Exception e)
            {
                Check(type.IsInstanceOfType(e), what + " throws " + type.Name + ", got " + e.GetType().Name + ": " + e.Message);
                Check(File.ReadAllText(target) == "existing OBJ" && File.ReadAllText(mtl) == "existing MTL", what + " keeps existing output");
                return;
            }
            throw new Exception("FAILED: " + what + " did not throw");
        };

        expect(() => Convert(Path.Combine(dir, "missing.3dxml"), target, "l"), typeof(IOException), "missing source");

        string empty = Path.Combine(dir, "empty.3dxml");
        Build3dxml(empty, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0") }, new Options { Empty = true });
        expect(() => Convert(empty, target, "l"), typeof(InvalidDataException), "no triangles");

        string texture = Path.Combine(dir, "texture.3dxml");
        Build3dxml(texture, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0") }, new Options { Material = true, Texture = true });
        try { Convert(texture, target, "l"); throw new Exception("FAILED: texture accepted"); }
        catch (NotSupportedException e)
        {
            Check(e.Message.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0 && e.Message.Contains("UV"), "texture error is explicit: " + e.Message);
        }
        Check(File.ReadAllText(target) == "existing OBJ", "texture failure keeps existing output");

        string good = Path.Combine(dir, "cancel.3dxml");
        Build3dxml(good, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0") }, new Options());
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        expect(() => Convert(good, target, "l", cancelled.Token), typeof(OperationCanceledException), "cancelled conversion");

        expect(() => ObjConverter.Convert(good, Path.Combine(dir, "keep.stl"), null), typeof(ArgumentException), "non-obj target");
        Check(Directory.GetFiles(dir).Length == before + 3, "no temporary files left (only the three inputs added)");
    }

    static void TestBackgroundAndProgress(string dir)
    {
        string source = Path.Combine(dir, "async.3dxml"), target = Path.Combine(dir, "async.obj");
        Build3dxml(source, new[] { new Instance("1 0 0 0 1 0 0 0 1 0 0 0"), new Instance("1 0 0 0 1 0 0 0 1 9 0 0") }, new Options());
        var seen = new List<double>();
        int callerThread = Thread.CurrentThread.ManagedThreadId, workerThread = -1;
        var progress = new SyncProgress(seen, () => workerThread = Thread.CurrentThread.ManagedThreadId);
        var task = ObjConverter.ConvertAsync(source, target, new ObjConversionOptions { LinkName = "l", Progress = progress });
        task.Wait();
        Check(task.Result.Triangles == 24, "async conversion result");
        Check(workerThread != callerThread, "conversion runs on a worker thread");
        Check(seen.Count > 0 && seen[seen.Count - 1] == 1.0 && seen.SequenceEqual(seen.OrderBy(x => x)), "progress is monotonic and ends at 1");
    }

    sealed class SyncProgress : IProgress<double>
    {
        readonly List<double> values; readonly Action onReport;
        public SyncProgress(List<double> values, Action onReport) { this.values = values; this.onReport = onReport; }
        public void Report(double value) { values.Add(value); onReport(); }
    }


    // ---- facts ----------------------------------------------------------------------

    static void InTempDirectory(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sw2urdf-obj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact] public void TestObjInstanceTransformUnitsAndNormals() { InTempDirectory(TestInstanceTransformUnitsAndNormals); }
    [Fact] public void TestObjMirroredInstanceKeepsOutwardWinding() { InTempDirectory(TestMirroredInstanceKeepsOutwardWinding); }
    [Fact] public void TestObjColorOverrideAndOpacity() { InTempDirectory(TestColorOverrideAndOpacity); }
    [Fact] public void TestObjMaterialFields() { InTempDirectory(TestMaterialFields); }
    [Fact] public void TestObjRepeatableOutput() { InTempDirectory(TestRepeatableOutput); }
    [Fact] public void TestObjBackgroundAndProgress() { InTempDirectory(TestBackgroundAndProgress); }
    [Fact] public void TestObjFailuresKeepExistingOutput() { InTempDirectory(TestFailuresKeepExistingOutput); }
}
}
