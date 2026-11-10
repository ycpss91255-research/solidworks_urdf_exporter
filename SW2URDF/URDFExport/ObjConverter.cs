// Converts a SOLIDWORKS 3DXML file to Wavefront OBJ + MTL, in metres.
// No SOLIDWORKS, Python or environment dependency; safe to run on a worker thread.
// Kept free of C# 6+ syntax so that it can be compiled stand-alone by the offline tests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW2URDF.URDFExport
{
    public sealed class ObjConversionOptions
    {
        /// <summary>Link name; used in material and object names so that they are stable and unique per link.</summary>
        public string LinkName = "mesh";

        /// <summary>Receives a fraction between 0 and 1 (called on the converting thread).</summary>
        public IProgress<double> Progress;

        public CancellationToken CancellationToken;
    }

    public sealed class ObjConversionResult
    {
        public int Triangles;
        public int Vertices;
        public int Materials;
    }

    public static class ObjConverter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const double MillimetresPerMetre = 1000.0;

        public static Task<ObjConversionResult> ConvertAsync(string source, string target, ObjConversionOptions options)
        {
            CancellationToken token = options != null ? options.CancellationToken : CancellationToken.None;
            return Task.Run(() => Convert(source, target, options), token);
        }

        /// <summary>
        /// Reads <paramref name="source"/> (3DXML) and writes <paramref name="target"/> (.obj) and the
        /// matching .mtl next to it. Output is written to temporary files first and only replaces
        /// existing files when the whole conversion succeeded.
        /// </summary>
        public static ObjConversionResult Convert(string source, string target, ObjConversionOptions options)
        {
            if (options == null) options = new ObjConversionOptions();
            if (!string.Equals(Path.GetExtension(target), ".obj", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Target must have .obj extension", "target");
            string mtlTarget = Path.ChangeExtension(target, ".mtl");
            string directory = Path.GetDirectoryName(Path.GetFullPath(target));
            string stamp = Guid.NewGuid().ToString("N");
            string objTemp = Path.Combine(directory, ".obj-" + stamp + ".tmp");
            string mtlTemp = Path.Combine(directory, ".mtl-" + stamp + ".tmp");
            try
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                Report(options, 0.0);
                ThreeDxmlScene scene = ThreeDxmlReader.Read(source);
                options.CancellationToken.ThrowIfCancellationRequested();
                Report(options, 0.2);
                ObjConversionResult result;
                using (var obj = new StreamWriter(objTemp, false, new UTF8Encoding(false), 1 << 16) { NewLine = "\n" })
                using (var mtl = new StreamWriter(mtlTemp, false, new UTF8Encoding(false), 1 << 12) { NewLine = "\n" })
                {
                    result = Write(scene, obj, mtl, Path.GetFileName(mtlTarget), options);
                }
                if (result.Triangles == 0) throw new InvalidDataException("3DXML contains no triangles");
                options.CancellationToken.ThrowIfCancellationRequested();
                Replace(mtlTemp, mtlTarget);
                Replace(objTemp, target);
                Report(options, 1.0);
                return result;
            }
            finally
            {
                TryDelete(objTemp);
                TryDelete(mtlTemp);
            }
        }

        private static void Report(ObjConversionOptions options, double fraction)
        {
            if (options.Progress != null) options.Progress.Report(fraction);
        }

        private static void Replace(string temporary, string destination)
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }

        private static void TryDelete(string path)
        {
            // Scanners can briefly lock a freshly written file; retry before giving up.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (IOException) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { Thread.Sleep(50); }
            }
        }

        private sealed class MaterialEntry
        {
            public string Name;
            public SurfaceAppearance Appearance;
        }

        private static ObjConversionResult Write(ThreeDxmlScene scene, TextWriter obj, TextWriter mtl,
            string mtlName, ObjConversionOptions options)
        {
            string link = SanitizeName(options.LinkName, "mesh");
            var materials = new Dictionary<string, MaterialEntry>();
            var materialOrder = new List<MaterialEntry>();
            var usedMaterialNames = new HashSet<string>(StringComparer.Ordinal);
            var objectNames = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new ObjConversionResult();

            obj.Write("# sw2urdf: metres, native axes; link ");
            obj.Write(link);
            obj.Write("\nmtllib ");
            obj.Write(mtlName);
            obj.Write("\n");

            int vertexOffset = 1;
            for (int index = 0; index < scene.Placements.Count; index++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                ThreeDxmlPlacement placement = scene.Placements[index];
                ThreeDxmlPart part = placement.Part;
                if (part.Triangles.Length == 0) continue;

                double[] matrix = placement.Matrix;
                int vertexCount = part.Vertices.Length / 3;
                var position = new double[vertexCount * 3];
                for (int v = 0; v < vertexCount; v++)
                {
                    double x = part.Vertices[v * 3], y = part.Vertices[v * 3 + 1], z = part.Vertices[v * 3 + 2];
                    for (int r = 0; r < 3; r++)
                    {
                        double value = (matrix[r * 4] * x + matrix[r * 4 + 1] * y + matrix[r * 4 + 2] * z + matrix[r * 4 + 3])
                            / MillimetresPerMetre;
                        if (double.IsNaN(value) || double.IsInfinity(value))
                            throw new InvalidDataException("3DXML contains non-finite vertices");
                        position[v * 3 + r] = value;
                    }
                }

                // A mirroring transform reverses the winding; restore outward-facing triangles.
                bool mirrored = Determinant3(matrix) < 0;
                int triangleCount = part.Triangles.Length / 3;
                var triangle = new int[part.Triangles.Length];
                for (int t = 0; t < triangleCount; t++)
                {
                    int a = part.Triangles[t * 3], b = part.Triangles[t * 3 + 1], c = part.Triangles[t * 3 + 2];
                    if (mirrored) { int swap = a; a = c; c = swap; }
                    triangle[t * 3] = a; triangle[t * 3 + 1] = b; triangle[t * 3 + 2] = c;
                }

                double[] normal = VertexNormals(position, triangle, vertexCount);

                obj.Write("o ");
                obj.Write(UniqueObjectName(link, placement.Name, objectNames));
                obj.Write("\n");
                for (int v = 0; v < vertexCount; v++)
                {
                    obj.Write("v ");
                    obj.Write(position[v * 3].ToString("R", Invariant)); obj.Write(' ');
                    obj.Write(position[v * 3 + 1].ToString("R", Invariant)); obj.Write(' ');
                    obj.Write(position[v * 3 + 2].ToString("R", Invariant)); obj.Write('\n');
                }
                for (int v = 0; v < vertexCount; v++)
                {
                    obj.Write("vn ");
                    obj.Write(normal[v * 3].ToString("R", Invariant)); obj.Write(' ');
                    obj.Write(normal[v * 3 + 1].ToString("R", Invariant)); obj.Write(' ');
                    obj.Write(normal[v * 3 + 2].ToString("R", Invariant)); obj.Write('\n');
                }

                // Appearance of every triangle after applying the per-occurrence colour override.
                ColorOverride overridden = FindOverride(scene.Overrides, placement.Path);
                var materialOfAppearance = new MaterialEntry[part.Appearances.Count];
                MaterialEntry current = null;
                for (int t = 0; t < triangleCount; t++)
                {
                    if ((t & 0xFFF) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                    int appearanceIndex = part.TriangleAppearance[t];
                    MaterialEntry entry = materialOfAppearance[appearanceIndex];
                    if (entry == null)
                    {
                        SurfaceAppearance appearance = part.Appearances[appearanceIndex];
                        if (overridden != null)
                        {
                            appearance = appearance.Clone();
                            appearance.R = overridden.R; appearance.G = overridden.G;
                            appearance.B = overridden.B; appearance.Alpha = overridden.Alpha;
                        }
                        entry = GetMaterial(link, appearance, materials, materialOrder, usedMaterialNames);
                        materialOfAppearance[appearanceIndex] = entry;
                    }
                    if (!ReferenceEquals(entry, current))
                    {
                        obj.Write("usemtl ");
                        obj.Write(entry.Name);
                        obj.Write('\n');
                        current = entry;
                    }
                    obj.Write("f ");
                    for (int corner = 0; corner < 3; corner++)
                    {
                        string i = (triangle[t * 3 + corner] + vertexOffset).ToString(Invariant);
                        obj.Write(i); obj.Write("//"); obj.Write(i);
                        obj.Write(corner < 2 ? ' ' : '\n');
                    }
                }
                result.Triangles += triangleCount;
                result.Vertices += vertexCount;
                vertexOffset += vertexCount;
                Report(options, 0.2 + 0.8 * (index + 1) / scene.Placements.Count);
            }

            mtl.Write("# sw2urdf materials; link ");
            mtl.Write(link);
            mtl.Write('\n');
            foreach (MaterialEntry entry in materialOrder) WriteMaterial(mtl, entry);
            result.Materials = materialOrder.Count;
            return result;
        }

        // ---- materials ---------------------------------------------------------------

        private static MaterialEntry GetMaterial(string link, SurfaceAppearance appearance,
            Dictionary<string, MaterialEntry> materials, List<MaterialEntry> order, HashSet<string> usedNames)
        {
            string key = appearance.Key;
            MaterialEntry entry;
            if (materials.TryGetValue(key, out entry)) return entry;
            string name = link + "_" + Hex(appearance.R) + Hex(appearance.G) + Hex(appearance.B);
            if (appearance.Alpha < 1.0)
                name += "_a" + Math.Round(Clamp(appearance.Alpha, 0, 1) * 100).ToString(Invariant);
            string unique = name;
            for (int n = 2; usedNames.Contains(unique); n++) unique = name + "_" + n.ToString(Invariant);
            usedNames.Add(unique);
            entry = new MaterialEntry { Name = unique, Appearance = appearance };
            materials[key] = entry;
            order.Add(entry);
            return entry;
        }

        private static string Hex(double channel)
        {
            return ((int)Math.Round(Clamp(channel, 0, 1) * 255)).ToString("x2", Invariant);
        }

        private static double Clamp(double value, double low, double high)
        {
            return value < low ? low : (value > high ? high : value);
        }

        private static string Number(double value)
        {
            return value.ToString("0.#########", Invariant);
        }

        private static void WriteMaterial(TextWriter mtl, MaterialEntry entry)
        {
            SurfaceAppearance a = entry.Appearance;
            double r = Clamp(a.R, 0, 1), g = Clamp(a.G, 0, 1), b = Clamp(a.B, 0, 1);
            double ksr = Clamp(a.SpecularR, 0, 1), ksg = Clamp(a.SpecularG, 0, 1), ksb = Clamp(a.SpecularB, 0, 1);
            bool specular = ksr > 0 || ksg > 0 || ksb > 0;
            mtl.Write("newmtl " + entry.Name + "\n");
            mtl.Write("Ka " + Number(r) + " " + Number(g) + " " + Number(b) + "\n");
            mtl.Write("Kd " + Number(r) + " " + Number(g) + " " + Number(b) + "\n");
            mtl.Write("Ks " + Number(ksr) + " " + Number(ksg) + " " + Number(ksb) + "\n");
            mtl.Write("Ns " + Number(Clamp(a.Shininess, 0, 1) * 1000.0) + "\n");
            mtl.Write("d " + Number(Clamp(a.Alpha, 0, 1)) + "\n");
            mtl.Write("illum " + (specular ? "2" : "1") + "\n\n");
        }

        private static string UniqueObjectName(string link, string name, Dictionary<string, int> used)
        {
            string baseName = link + "_" + SanitizeName(name, "part");
            int count;
            used.TryGetValue(baseName, out count);
            used[baseName] = count + 1;
            return count == 0 ? baseName : baseName + "." + (count + 1).ToString(Invariant);
        }

        internal static string SanitizeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var builder = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
                builder.Append(char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c);
            return builder.ToString();
        }

        // ---- geometry -----------------------------------------------------------------

        private static double Determinant3(double[] m)
        {
            return m[0] * (m[5] * m[10] - m[6] * m[9])
                 - m[1] * (m[4] * m[10] - m[6] * m[8])
                 + m[2] * (m[4] * m[9] - m[5] * m[8]);
        }

        // Angle-weighted vertex normals; vertices are never merged.
        private static double[] VertexNormals(double[] p, int[] tri, int vertexCount)
        {
            var normal = new double[vertexCount * 3];
            for (int t = 0; t < tri.Length; t += 3)
            {
                int ia = tri[t] * 3, ib = tri[t + 1] * 3, ic = tri[t + 2] * 3;
                double abx = p[ib] - p[ia], aby = p[ib + 1] - p[ia + 1], abz = p[ib + 2] - p[ia + 2];
                double acx = p[ic] - p[ia], acy = p[ic + 1] - p[ia + 1], acz = p[ic + 2] - p[ia + 2];
                double bcx = p[ic] - p[ib], bcy = p[ic + 1] - p[ib + 1], bcz = p[ic + 2] - p[ib + 2];
                double nx = aby * acz - abz * acy, ny = abz * acx - abx * acz, nz = abx * acy - aby * acx;
                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length == 0 || double.IsNaN(length)) continue;
                nx /= length; ny /= length; nz /= length;
                double angleA = Angle(abx, aby, abz, acx, acy, acz);
                double angleB = Angle(-abx, -aby, -abz, bcx, bcy, bcz);
                double angleC = Math.PI - angleA - angleB;
                Accumulate(normal, ia, nx, ny, nz, angleA);
                Accumulate(normal, ib, nx, ny, nz, angleB);
                Accumulate(normal, ic, nx, ny, nz, angleC);
            }
            for (int v = 0; v < vertexCount; v++)
            {
                double x = normal[v * 3], y = normal[v * 3 + 1], z = normal[v * 3 + 2];
                double length = Math.Sqrt(x * x + y * y + z * z);
                if (length == 0) continue;
                normal[v * 3] = x / length; normal[v * 3 + 1] = y / length; normal[v * 3 + 2] = z / length;
            }
            return normal;
        }

        private static void Accumulate(double[] normal, int offset, double x, double y, double z, double weight)
        {
            normal[offset] += x * weight; normal[offset + 1] += y * weight; normal[offset + 2] += z * weight;
        }

        private static double Angle(double ax, double ay, double az, double bx, double by, double bz)
        {
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), ax * bx + ay * by + az * bz);
        }

        // ---- colour overrides ----------------------------------------------------------

        // An override applies when its occurrence ids appear, in order, along the instance path.
        // The most specific (longest, then deepest) override wins.
        private static ColorOverride FindOverride(List<ColorOverride> overrides, string[] path)
        {
            ColorOverride best = null;
            int bestLength = -1, bestPosition = -1;
            foreach (ColorOverride candidate in overrides)
            {
                int cursor = 0;
                bool matches = true;
                foreach (string id in candidate.Ids)
                {
                    while (cursor < path.Length && path[cursor] != id) cursor++;
                    if (cursor >= path.Length) { matches = false; break; }
                    cursor++;
                }
                if (!matches) continue;
                int last = Array.IndexOf(path, candidate.Ids[candidate.Ids.Length - 1]);
                if (candidate.Ids.Length > bestLength || (candidate.Ids.Length == bestLength && last > bestPosition))
                {
                    best = candidate;
                    bestLength = candidate.Ids.Length;
                    bestPosition = last;
                }
            }
            return best;
        }
    }
}
