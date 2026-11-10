// Reads the tessellated subset of the 3DXML format that SOLIDWORKS writes:
// a zip archive holding an XML product structure, one tessellated
// representation per part, and an optional material library.
// Kept free of SOLIDWORKS references and of C# 6+ syntax so that it can be
// compiled stand-alone by the offline test script.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SW2URDF.URDFExport
{
    /// <summary>Colour and shading values of one group of triangles.</summary>
    internal sealed class SurfaceAppearance
    {
        public double R = 0.5, G = 0.5, B = 0.5, Alpha = 1.0;
        public double SpecularR, SpecularG, SpecularB;
        // SOLIDWORKS specular exponent / shininess in the range 0 - 1.
        public double Shininess;

        public SurfaceAppearance Clone()
        {
            return (SurfaceAppearance)MemberwiseClone();
        }

        public string Key
        {
            get
            {
                return string.Join(" ", new[] { R, G, B, Alpha, SpecularR, SpecularG, SpecularB, Shininess }
                    .Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            }
        }
    }

    /// <summary>All triangles of one ReferenceRep, in the native part frame (millimetres).</summary>
    internal sealed class ThreeDxmlPart
    {
        public string Id;
        public double[] Vertices;          // x y z per vertex
        public int[] Triangles;            // three vertex indices per triangle
        public int[] TriangleAppearance;   // index into Appearances, one per triangle
        public List<SurfaceAppearance> Appearances = new List<SurfaceAppearance>();
    }

    /// <summary>One occurrence of a part: the full instance path and its accumulated transform.</summary>
    internal sealed class ThreeDxmlPlacement
    {
        public ThreeDxmlPart Part;
        public string Name;
        public string[] Path;      // ids from the root reference to the representation
        public double[] Matrix;    // 4 x 4, row major, column-vector convention
    }

    internal sealed class ColorOverride
    {
        public string[] Ids;
        public double R, G, B, Alpha;
    }

    internal sealed class ThreeDxmlScene
    {
        public List<ThreeDxmlPlacement> Placements = new List<ThreeDxmlPlacement>();
        public List<ColorOverride> Overrides = new List<ColorOverride>();
    }

    internal static class ThreeDxmlReader
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private sealed class Node
        {
            public string Id, Name, Type, Aggregate, Instance;
            public double[] Matrix;
        }

        public static ThreeDxmlScene Read(string path)
        {
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                string rootFile = ReadElementText(archive, "Manifest.xml", "Root");
                XDocument structure = LoadXml(archive, rootFile);
                XElement product = structure.Descendants().FirstOrDefault(e => e.Name.LocalName == "ProductStructure");
                if (product == null) throw new InvalidDataException("3DXML has no ProductStructure.");
                string rootId = (string)product.Attribute("root");

                Dictionary<string, MaterialInfo> materials = ReadMaterials(archive);
                var scene = new ThreeDxmlScene();
                var order = new List<string>();
                var nodes = new Dictionary<string, Node>();
                Action<string> touch = id =>
                {
                    if (!nodes.ContainsKey(id)) { nodes[id] = new Node { Id = id }; order.Add(id); }
                };

                XElement view = structure.Descendants().FirstOrDefault(e => e.Name.LocalName == "DefaultView");
                if (view != null)
                {
                    foreach (XElement property in view.Descendants().Where(e => e.Name.LocalName == "DefaultViewProperty"))
                    {
                        XElement color = property.Elements().Where(e => e.Name.LocalName == "GraphicProperties")
                            .SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == "SurfaceAttributes")
                            .SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name.LocalName == "Color");
                        if (color == null) continue;
                        string[] ids = property.Elements().Where(e => e.Name.LocalName == "OccurenceId")
                            .SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == "id")
                            .Select(e => LastPart(e.Value.Trim())).ToArray();
                        foreach (string id in ids) touch(id);
                        if (ids.Length > 0)
                        {
                            scene.Overrides.Add(new ColorOverride
                            {
                                Ids = ids,
                                R = Channel(color, "red"), G = Channel(color, "green"),
                                B = Channel(color, "blue"), Alpha = Channel(color, "alpha")
                            });
                        }
                    }
                }

                // Geometry, in file order.
                var parts = new List<ThreeDxmlPart>();
                var partById = new Dictionary<string, ThreeDxmlPart>();
                foreach (XElement rep in product.Descendants().Where(e => e.Name.LocalName == "ReferenceRep"))
                {
                    string format = (string)rep.Attribute("format");
                    if (format != "TESSELLATED") continue;
                    string id = (string)rep.Attribute("id");
                    string file = LastPart((string)rep.Attribute("associatedFile"), ':');
                    touch(id);
                    ThreeDxmlPart part = ReadRepresentation(archive, file, id, materials);
                    parts.Add(part);
                    partById[id] = part;
                }
                foreach (XElement e in product.Descendants().Where(e => e.Name.LocalName == "Reference3D"))
                {
                    string id = (string)e.Attribute("id");
                    touch(id);
                    nodes[id].Name = (string)e.Attribute("name");
                    nodes[id].Type = "Reference3D";
                }
                foreach (XElement e in product.Descendants().Where(e => e.Name.LocalName == "InstanceRep"))
                {
                    string id = (string)e.Attribute("id");
                    touch(id);
                    nodes[id].Type = "InstanceRep";
                    nodes[id].Aggregate = Child(e, "IsAggregatedBy");
                    nodes[id].Instance = Child(e, "IsInstanceOf");
                }
                foreach (XElement e in product.Descendants().Where(e => e.Name.LocalName == "Instance3D"))
                {
                    string id = (string)e.Attribute("id");
                    touch(id);
                    nodes[id].Type = "Instance3D";
                    nodes[id].Name = (string)e.Attribute("name");
                    nodes[id].Aggregate = Child(e, "IsAggregatedBy");
                    nodes[id].Instance = Child(e, "IsInstanceOf");
                    nodes[id].Matrix = ParseMatrix(Child(e, "RelativeMatrix"));
                }

                // Directed graph: aggregate -> element -> instanced reference.
                var edges = new Dictionary<string, List<string>>();
                Action<string, string> link = (from, to) =>
                {
                    List<string> list;
                    if (!edges.TryGetValue(from, out list)) { list = new List<string>(); edges[from] = list; }
                    if (!list.Contains(to)) list.Add(to);
                };
                foreach (string id in order)
                {
                    Node node = nodes[id];
                    if (node.Aggregate != null) link(node.Aggregate, id);
                    if (node.Instance != null) link(id, node.Instance);
                }

                var pathsByPart = new Dictionary<string, List<string[]>>();
                foreach (ThreeDxmlPart part in parts) pathsByPart[part.Id] = new List<string[]>();
                var current = new List<string>();
                var onPath = new HashSet<string>();
                Action<string> walk = null;
                walk = id =>
                {
                    current.Add(id);
                    onPath.Add(id);
                    List<string[]> found;
                    if (current.Count > 1 && pathsByPart.TryGetValue(id, out found))
                        found.Add(current.ToArray());
                    List<string> next;
                    if (edges.TryGetValue(id, out next))
                    {
                        foreach (string child in next)
                            if (!onPath.Contains(child)) walk(child);
                    }
                    onPath.Remove(id);
                    current.RemoveAt(current.Count - 1);
                };
                if (rootId != null) walk(rootId);

                foreach (ThreeDxmlPart part in parts)
                {
                    foreach (string[] route in pathsByPart[part.Id])
                    {
                        double[] matrix = Identity();
                        foreach (string stepId in route)
                        {
                            Node node = nodes[stepId];
                            if (node.Matrix != null) matrix = Multiply(matrix, node.Matrix);
                        }
                        scene.Placements.Add(new ThreeDxmlPlacement
                        {
                            Part = part,
                            Path = route,
                            Matrix = matrix,
                            Name = PlacementName(route, nodes, part.Id)
                        });
                    }
                }
                return scene;
            }
        }

        private static string PlacementName(string[] path, Dictionary<string, Node> nodes, string fallback)
        {
            // path: ... Instance3D, Reference3D, InstanceRep, ReferenceRep
            if (path.Length >= 4 && nodes[path[path.Length - 4]].Type == "Instance3D"
                && !string.IsNullOrEmpty(nodes[path[path.Length - 4]].Name))
                return nodes[path[path.Length - 4]].Name;
            if (path.Length >= 3 && !string.IsNullOrEmpty(nodes[path[path.Length - 3]].Name))
                return nodes[path[path.Length - 3]].Name;
            return fallback;
        }

        // ---- materials -------------------------------------------------------------

        private sealed class MaterialInfo
        {
            public SurfaceAppearance Appearance;
            public bool HasTexture;
        }

        private static Dictionary<string, MaterialInfo> ReadMaterials(ZipArchive archive)
        {
            var result = new Dictionary<string, MaterialInfo>();
            if (archive.GetEntry("CATMaterialRef.3dxml") == null) return result;
            XDocument reference = LoadXml(archive, "CATMaterialRef.3dxml");
            var byDomain = new Dictionary<string, MaterialInfo>();
            foreach (XElement domain in reference.Descendants().Where(e => e.Name.LocalName == "MaterialDomain"))
            {
                string file = LastPart((string)domain.Attribute("associatedFile"), ':');
                if (archive.GetEntry(file) == null) continue;
                XDocument definition = LoadXml(archive, file);
                XElement rendering = definition.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "Feature" && (string)e.Attribute("Alias") == "RenderingFeature");
                if (rendering == null) continue;
                var attributes = rendering.Elements().Where(e => e.Name.LocalName == "Attr")
                    .ToDictionary(e => (string)e.Attribute("Name"), e => (string)e.Attribute("Value"), StringComparer.Ordinal);
                var info = new MaterialInfo { Appearance = new SurfaceAppearance() };
                double[] diffuse = Vector(attributes, "DiffuseColor");
                if (diffuse != null) { info.Appearance.R = diffuse[0]; info.Appearance.G = diffuse[1]; info.Appearance.B = diffuse[2]; }
                double[] specular = Vector(attributes, "SpecularColor");
                double coefficient = Scalar(attributes, "SpecularCoef", 1.0);
                if (specular != null)
                {
                    info.Appearance.SpecularR = specular[0] * coefficient;
                    info.Appearance.SpecularG = specular[1] * coefficient;
                    info.Appearance.SpecularB = specular[2] * coefficient;
                }
                info.Appearance.Shininess = Scalar(attributes, "SpecularExponent", 0.0);
                info.Appearance.Alpha = 1.0 - Scalar(attributes, "Transparency", 0.0);
                info.HasTexture = attributes.ContainsKey("TextureImage");
                byDomain[(string)domain.Attribute("id")] = info;
            }
            foreach (XElement instance in reference.Descendants().Where(e => e.Name.LocalName == "MaterialDomainInstance"))
            {
                string of = Child(instance, "IsInstanceOf");
                MaterialInfo info;
                if (of == null || !byDomain.TryGetValue(of, out info)) continue;
                foreach (XElement aggregate in instance.Elements().Where(e => e.Name.LocalName == "IsAggregatedBy"))
                    result[aggregate.Value.Trim()] = info;
            }
            return result;
        }

        private static double[] Vector(Dictionary<string, string> attributes, string name)
        {
            string value;
            if (!attributes.TryGetValue(name, out value)) return null;
            double[] numbers = ParseNumbers(value.Trim('[', ']', ' '));
            return numbers.Length >= 3 ? numbers : null;
        }

        private static double Scalar(Dictionary<string, string> attributes, string name, double fallback)
        {
            string value;
            if (!attributes.TryGetValue(name, out value)) return fallback;
            double[] numbers = ParseNumbers(value.Trim('[', ']', ' '));
            return numbers.Length > 0 ? numbers[0] : fallback;
        }

        // ---- tessellated representation -----------------------------------------

        private sealed class FaceRecord
        {
            public int Start, Count;
            public double[] Color;    // r g b a, or null
        }

        private sealed class RepContext
        {
            public double[] Positions;
            public bool HasUv;
            public string MaterialId;
            public List<int> Triangles = new List<int>();
            public List<FaceRecord> Faces = new List<FaceRecord>();
        }

        private static ThreeDxmlPart ReadRepresentation(ZipArchive archive, string file, string id,
            Dictionary<string, MaterialInfo> materials)
        {
            ZipArchiveEntry entry = archive.GetEntry(file);
            if (entry == null) throw new InvalidDataException("3DXML is missing representation " + file);
            var vertices = new List<double>();
            var triangles = new List<int>();
            var appearanceIds = new List<int>();
            var part = new ThreeDxmlPart { Id = id };
            var appearanceIndex = new Dictionary<string, int>();
            var stack = new Stack<RepContext>();

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                bool more = reader.Read();
                while (more)
                {
                    // True when a handler already moved the reader onto the next node.
                    bool advanced = false;
                    if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Rep")
                    {
                        Finish(stack.Pop(), part, vertices, triangles, appearanceIds, appearanceIndex, materials);
                    }
                    else if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.LocalName)
                        {
                            case "Rep":
                                if (!reader.IsEmptyElement) stack.Push(new RepContext());
                                break;

                            case "Positions":
                                if (stack.Count > 0)
                                {
                                    stack.Peek().Positions = ParseNumbers(reader.ReadElementContentAsString());
                                    advanced = true;
                                }
                                break;

                            case "Normals":
                                // Normals are recomputed from the final geometry.
                                reader.Skip();
                                advanced = true;
                                break;

                            case "TextureCoordinates":
                                if (stack.Count > 0) stack.Peek().HasUv = true;
                                break;

                            case "Face":
                                if (stack.Count > 0) ReadFace(reader, stack.Peek());
                                break;

                            case "MaterialId":
                                if (stack.Count > 0) stack.Peek().MaterialId = LastPart(reader.GetAttribute("id"));
                                break;
                        }
                    }
                    more = advanced ? !reader.EOF : reader.Read();
                }
            }            part.Vertices = vertices.ToArray();
            part.Triangles = triangles.ToArray();
            part.TriangleAppearance = appearanceIds.ToArray();
            return part;
        }

        private static void ReadFace(XmlReader reader, RepContext context)
        {
            int start = context.Triangles.Count / 3;
            string strips = reader.GetAttribute("strips");
            string list = reader.GetAttribute("triangles");
            string fans = reader.GetAttribute("fans");
            if (strips != null)
            {
                foreach (string strip in strips.Split(','))
                {
                    int[] s = ParseInts(strip);
                    for (int i = 0; i + 2 < s.Length; i++)
                    {
                        if (i % 2 == 0) AddTriangle(context.Triangles, s[i], s[i + 1], s[i + 2]);
                        else AddTriangle(context.Triangles, s[i + 2], s[i + 1], s[i]);
                    }
                }
            }
            if (list != null)
            {
                int[] t = ParseInts(list);
                for (int i = 0; i + 2 < t.Length; i += 3) AddTriangle(context.Triangles, t[i], t[i + 1], t[i + 2]);
            }
            if (fans != null)
            {
                foreach (string fan in fans.Split(','))
                {
                    int[] f = ParseInts(fan);
                    for (int i = 1; i + 1 < f.Length; i++) AddTriangle(context.Triangles, f[0], f[i], f[i + 1]);
                }
            }
            double[] color = null;
            if (!reader.IsEmptyElement)
            {
                using (XmlReader sub = reader.ReadSubtree())
                {
                    while (sub.Read())
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "Color")
                        {
                            color = new[] { Channel(sub, "red"), Channel(sub, "green"), Channel(sub, "blue"), Channel(sub, "alpha") };
                        }
                    }
                }
            }
            context.Faces.Add(new FaceRecord { Start = start, Count = context.Triangles.Count / 3 - start, Color = color });
        }

        private static void AddTriangle(List<int> list, int a, int b, int c)
        {
            list.Add(a); list.Add(b); list.Add(c);
        }

        private static void Finish(RepContext context, ThreeDxmlPart part, List<double> vertices, List<int> triangles,
            List<int> appearanceIds, Dictionary<string, int> appearanceIndex, Dictionary<string, MaterialInfo> materials)
        {
            if (context.Positions == null) return;
            MaterialInfo material = null;
            if (context.MaterialId != null) materials.TryGetValue(context.MaterialId, out material);
            if (context.HasUv && material != null && material.HasTexture)
                throw new NotSupportedException(
                    "Image textures and UV coordinates are not supported by the OBJ exporter; it writes solid colours only.");
            int offset = vertices.Count / 3;
            int vertexCount = context.Positions.Length / 3;
            vertices.AddRange(context.Positions);
            foreach (FaceRecord face in context.Faces)
            {
                SurfaceAppearance appearance = material != null ? material.Appearance.Clone() : new SurfaceAppearance();
                if (face.Color != null)
                {
                    appearance.R = face.Color[0]; appearance.G = face.Color[1];
                    appearance.B = face.Color[2]; appearance.Alpha = face.Color[3];
                }
                string key = appearance.Key;
                int index;
                if (!appearanceIndex.TryGetValue(key, out index))
                {
                    index = part.Appearances.Count;
                    part.Appearances.Add(appearance);
                    appearanceIndex[key] = index;
                }
                for (int t = face.Start * 3; t < (face.Start + face.Count) * 3; t++)
                {
                    int vertex = context.Triangles[t];
                    if (vertex < 0 || vertex >= vertexCount)
                        throw new InvalidDataException("3DXML triangle index out of range.");
                    triangles.Add(vertex + offset);
                }
                for (int t = 0; t < face.Count; t++) appearanceIds.Add(index);
            }
        }

        // ---- helpers ---------------------------------------------------------------

        private static double Channel(XElement element, string name)
        {
            string value = (string)element.Attribute(name);
            return value == null ? 1.0 : double.Parse(value, NumberStyles.Float, Invariant);
        }

        private static double Channel(XmlReader reader, string name)
        {
            string value = reader.GetAttribute(name);
            return value == null ? 1.0 : double.Parse(value, NumberStyles.Float, Invariant);
        }

        private static string Child(XElement element, string name)
        {
            XElement child = element.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            return child == null ? null : child.Value.Trim();
        }

        private static string LastPart(string value, char separator = '#')
        {
            if (value == null) return null;
            int index = value.LastIndexOf(separator);
            return index < 0 ? value : value.Substring(index + 1);
        }

        private static XDocument LoadXml(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name);
            if (entry == null) throw new InvalidDataException("3DXML is missing " + name);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, settings))
                return XDocument.Load(reader);
        }

        private static string ReadElementText(ZipArchive archive, string file, string element)
        {
            XElement found = LoadXml(archive, file).Descendants().FirstOrDefault(e => e.Name.LocalName == element);
            if (found == null) throw new InvalidDataException(file + " has no " + element + " element.");
            return found.Value.Trim();
        }

        private static bool IsSeparator(char c)
        {
            return c == ' ' || c == ',' || c == '\t' || c == '\r' || c == '\n';
        }

        internal static double[] ParseNumbers(string text)
        {
            var list = new List<double>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && IsSeparator(text[i])) i++;
                int start = i;
                while (i < text.Length && !IsSeparator(text[i])) i++;
                if (i > start) list.Add(double.Parse(text.Substring(start, i - start), NumberStyles.Float, Invariant));
            }
            return list.ToArray();
        }

        private static int[] ParseInts(string text)
        {
            var list = new List<int>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && IsSeparator(text[i])) i++;
                int start = i;
                while (i < text.Length && !IsSeparator(text[i])) i++;
                if (i > start) list.Add(int.Parse(text.Substring(start, i - start), NumberStyles.Integer, Invariant));
            }
            return list.ToArray();
        }

        private static double[] ParseMatrix(string text)
        {
            if (text == null) return null;
            double[] n = ParseNumbers(text);
            if (n.Length != 12) throw new InvalidDataException("RelativeMatrix must have 12 values.");
            // Three columns of the rotation, then the translation.
            var m = Identity();
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++) m[r * 4 + c] = n[c * 3 + r];
                m[r * 4 + 3] = n[9 + r];
            }
            return m;
        }

        internal static double[] Identity()
        {
            return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        internal static double[] Multiply(double[] a, double[] b)
        {
            var result = new double[16];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[r * 4 + k] * b[k * 4 + c];
                    result[r * 4 + c] = sum;
                }
            return result;
        }
    }
}
