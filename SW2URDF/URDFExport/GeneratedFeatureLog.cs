using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.URDFExport
{
    public enum GeneratedKind { Sketch, CoordinateSystem, Axis }

    /// <summary>One feature this run of the exporter put into the model.</summary>
    public sealed class GeneratedFeature
    {
        public GeneratedKind Kind { get; }
        public string Name { get; }

        /// <summary>The SolidWorks feature (or sketch entity), kept to find exactly this one again.</summary>
        public object Handle { get; }

        public GeneratedFeature(GeneratedKind kind, string name, object handle)
        {
            Kind = kind;
            Name = name;
            Handle = handle;
        }
    }

    /// <summary>
    /// Bookkeeping of what one run of the wizard generated in the model (the reference sketch,
    /// coordinate systems and axes) and which names it handed to the joints. Whatever was in the
    /// model before the run, or made by the user, is never in here, so cancelling can remove
    /// exactly what the run added and nothing else. Free of SolidWorks calls so it can be tested.
    /// </summary>
    public sealed class GeneratedFeatureLog
    {
        private readonly List<GeneratedFeature> created = new List<GeneratedFeature>();
        private readonly List<GeneratedFeature> sketchEntities = new List<GeneratedFeature>();
        private readonly HashSet<string> assigned = new HashSet<string>();

        public bool IsEmpty
        {
            get { return created.Count == 0 && sketchEntities.Count == 0; }
        }

        public IReadOnlyList<GeneratedFeature> Created
        {
            get { return created; }
        }

        /// <summary>Points and lines the run added to a reference sketch that already existed.</summary>
        public IReadOnlyList<GeneratedFeature> AddedSketchEntities
        {
            get { return sketchEntities; }
        }

        public void AddCreated(GeneratedKind kind, string name, object handle = null)
        {
            created.Add(new GeneratedFeature(kind, name, handle));
            assigned.Add(Key(kind, name));
        }

        public void AddSketchEntity(object handle)
        {
            sketchEntities.Add(new GeneratedFeature(GeneratedKind.Sketch, null, handle));
        }

        /// <summary>A name given to a joint without creating anything (an existing frame was reused).</summary>
        public void AddReused(GeneratedKind kind, string name)
        {
            assigned.Add(Key(kind, name));
        }

        public bool WasCreated(GeneratedKind kind, string name)
        {
            return created.Any(f => f.Kind == kind && f.Name == name);
        }

        public bool WasAssigned(GeneratedKind kind, string name)
        {
            return name != null && assigned.Contains(Key(kind, name));
        }

        /// <summary>
        /// The created features in the order they can be deleted: frames and axes (newest first),
        /// the reference sketch last because they are built from it.
        /// </summary>
        public IList<GeneratedFeature> RemovalOrder()
        {
            List<GeneratedFeature> frames = created.Where(f => f.Kind != GeneratedKind.Sketch).ToList();
            frames.Reverse();
            frames.AddRange(created.Where(f => f.Kind == GeneratedKind.Sketch));
            return frames;
        }

        public void Clear()
        {
            created.Clear();
            sketchEntities.Clear();
            assigned.Clear();
        }

        private static string Key(GeneratedKind kind, string name)
        {
            return kind + "|" + name;
        }

        /// <summary>
        /// Picks the name for a generated frame or axis: the plain name when it is free, an existing
        /// one with that name or a numbered variant when it is the same geometry (so previewing again
        /// adds nothing), otherwise the first free numbered name. Nothing existing is ever replaced.
        /// </summary>
        public static string ChooseName(
            string baseName, Func<string, bool> exists, Func<string, bool> sameGeometry, out bool reuse)
        {
            for (int index = 1; index < 10000; index++)
            {
                string candidate = index == 1 ? baseName :
                    baseName + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!exists(candidate))
                {
                    reuse = false;
                    return candidate;
                }
                if (sameGeometry(candidate))
                {
                    reuse = true;
                    return candidate;
                }
            }
            throw new InvalidOperationException("No free name found for " + baseName);
        }

        /// <summary>
        /// The name a joint should use for a frame or axis its saved settings already name. A frame
        /// the exporter generated earlier is only right for the geometry it was generated in, which
        /// is no longer the case after another SolidWorks configuration was activated or after the
        /// model was edited: then <see cref="ChooseName"/> picks (or creates) the one for the
        /// current geometry and the old one is left alone. A frame the user made, or one at the
        /// wanted place, is kept. <paramref name="changed"/> tells that the name is not the saved one.
        /// </summary>
        public static string RefreshName(
            string savedName, string baseName, Func<string, bool> exists, Func<string, bool> isGenerated,
            Func<string, bool> sameGeometry, out bool reuse, out bool changed)
        {
            reuse = true;
            changed = false;
            if (!isGenerated(savedName) || sameGeometry(savedName))
            {
                return savedName;
            }
            string name = ChooseName(baseName, exists, sameGeometry, out reuse);
            changed = name != savedName;
            return name;
        }

        /// <summary>True when two vectors run along the same line, in either direction.</summary>
        public static bool SameDirection(double[] first, double[] second, double tolerance)
        {
            if (first == null || second == null || first.Length != 3 || second.Length != 3)
            {
                return false;
            }
            double firstLength = Math.Sqrt(first.Sum(v => v * v));
            double secondLength = Math.Sqrt(second.Sum(v => v * v));
            if (firstLength < 1e-12 || secondLength < 1e-12)
            {
                return false;
            }
            double dot = 0;
            for (int i = 0; i < 3; i++)
            {
                dot += first[i] / firstLength * (second[i] / secondLength);
            }
            return Math.Abs(Math.Abs(dot) - 1.0) <= tolerance;
        }

        /// <summary>True when two frames (or two directions) are the same within a tolerance.</summary>
        public static bool SameValues(double[] first, double[] second, double tolerance)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }
            for (int i = 0; i < first.Length; i++)
            {
                if (Math.Abs(first[i] - second[i]) > tolerance)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// The coordinate system and axis names the joints had before a run generated frames, so a
    /// cancelled run can put them back ("Automatically Generate" must not turn into the name of a
    /// frame that no longer exists).
    /// </summary>
    public sealed class JointNameSnapshot
    {
        private readonly List<Entry> entries = new List<Entry>();

        private sealed class Entry
        {
            public Joint Joint;
            public string CoordinateSystemName;
            public string AxisName;
        }

        public static JointNameSnapshot Take(LinkNode root)
        {
            JointNameSnapshot snapshot = new JointNameSnapshot();
            snapshot.Add(root);
            return snapshot;
        }

        private void Add(LinkNode node)
        {
            entries.Add(new Entry
            {
                Joint = node.Link.Joint,
                CoordinateSystemName = node.Link.Joint.CoordinateSystemName,
                AxisName = node.Link.Joint.AxisName
            });
            foreach (LinkNode child in node.Nodes)
            {
                Add(child);
            }
        }

        /// <summary>
        /// Puts the earlier names back on the joints that still carry a name this run handed out.
        /// A name the user chose in the meantime is left alone.
        /// </summary>
        public void Restore(GeneratedFeatureLog log)
        {
            foreach (Entry entry in entries)
            {
                if (entry.Joint.CoordinateSystemName != entry.CoordinateSystemName &&
                    log.WasAssigned(GeneratedKind.CoordinateSystem, entry.Joint.CoordinateSystemName))
                {
                    entry.Joint.CoordinateSystemName = entry.CoordinateSystemName;
                }
                if (entry.Joint.AxisName != entry.AxisName &&
                    log.WasAssigned(GeneratedKind.Axis, entry.Joint.AxisName))
                {
                    entry.Joint.AxisName = entry.AxisName;
                }
            }
        }
    }
}
