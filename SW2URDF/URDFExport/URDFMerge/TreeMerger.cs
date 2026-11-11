using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows.Controls;

namespace SW2URDF.URDFExport.URDFMerge
{
    public class TreeMerger
    {
        public bool UseCSVInertial;
        public bool UseCSVVisualCollision;
        public bool UseCSVJointKinematics;
        public bool UseCSVJointOther;

        /// <summary>
        /// Helper class to Merge two URDFTreeViews
        /// </summary>
        /// <param name="useCSVInertial">Use loaded values for MoI and CoM properties of a link </param>
        /// <param name="useCSVVisualCollision">Use loaded values for meshes and material properties</param>
        /// <param name="useCSVJointKinematics">Use loaded values for joint coordinate system, joint
        /// axis and joint type</param>
        /// <param name="useCSVJointOther">Use loaded values for Joint Limits, Calibration, Dynamics,
        /// and Safety Controller</param>
        public TreeMerger(bool useCSVInertial, bool useCSVVisualCollision,
            bool useCSVJointKinematics, bool useCSVJointOther)
        {
            UseCSVInertial = useCSVInertial;
            UseCSVVisualCollision = useCSVVisualCollision;
            UseCSVJointKinematics = useCSVJointKinematics;
            UseCSVJointOther = useCSVJointOther;
        }

        public Link Merge(TreeView cadTree, URDFTreeCorrespondance correspondance)
        {
            if (cadTree.Items.Count != 1)
            {
                return null;
            }

            Link mergedRoot = MergeItem((TreeViewItem)cadTree.Items[0], correspondance);
            return mergedRoot;
        }

        private List<Link> MergeItems(ItemCollection cadCollection, URDFTreeCorrespondance correspondance)
        {
            List<Link> merged = new List<Link>();

            foreach (TreeViewItem item in cadCollection)
            {
                Link mergedItem = MergeItem(item, correspondance);
                merged.Add(mergedItem);
            }

            return merged;
        }

        private Link MergeItem(TreeViewItem cadItem, URDFTreeCorrespondance correspondance)
        {
            Link cadLink = (Link)cadItem.Tag;
            Link csvLink = correspondance.GetCorrespondingLink(cadItem);

            Link mergedLink = MergeLink(cadLink, csvLink);

            List<Link> children = MergeItems(cadItem.Items, correspondance);
            mergedLink.Children.Clear();
            mergedLink.Children.AddRange(children);
            return mergedLink;
        }

        private static bool HasValue(StringDictionary data, string key)
        {
            return data.ContainsKey(key) && !string.IsNullOrWhiteSpace(data[key]);
        }

        private Link MergeLink(Link cadLink, Link csvLink)
        {
            if (csvLink == null)
            {
                return cadLink;
            }

            Link mergedLink = cadLink.Clone();
            // SolidWorks components won't be loaded from the file. Use the components in the model
            mergedLink.SWMainComponent = cadLink.SWMainComponent;
            mergedLink.SWComponents = new List<Component2>(cadLink.SWComponents);

            // Cells of the CSV that are blank or could not be read are not in CsvData: for those the
            // value of the model stays. A link that did not come from the loader is taken as a whole.
            StringDictionary data = csvLink.CsvData;
            List<string> linkContext = new List<string> { "Link" };
            List<string> jointContext = new List<string> { "Link", "Joint" };

            if (UseCSVInertial)
            {
                if (data == null)
                {
                    mergedLink.Inertial.SetElement(csvLink.Inertial);
                }
                else
                {
                    mergedLink.Inertial.SetElementFromData(linkContext, data);
                }
                mergedLink.UsesCsvInertial = true;
            }

            if (UseCSVVisualCollision)
            {
                if (data == null)
                {
                    mergedLink.Visual.SetElement(csvLink.Visual);
                    mergedLink.Collision.SetElement(csvLink.Collision);
                }
                else
                {
                    mergedLink.Visual.SetElementFromData(linkContext, data);
                    mergedLink.Collision.SetElementFromData(linkContext, data);
                    mergedLink.UsesCsvVisualMesh = mergedLink.UsesCsvVisualMesh || HasValue(data, "Link.Visual.Geometry.Mesh.filename");
                    mergedLink.UsesCsvCollisionMesh = mergedLink.UsesCsvCollisionMesh || HasValue(data, "Link.Collision.Geometry.Mesh.filename");
                }
                mergedLink.UsesCsvVisualCollision = true;
            }

            if (UseCSVJointKinematics)
            {
                if (data == null)
                {
                    mergedLink.Joint.Origin.SetElement(csvLink.Joint.Origin);
                    mergedLink.Joint.CoordinateSystemName = csvLink.Joint.CoordinateSystemName;

                    mergedLink.Joint.Axis.SetElement(csvLink.Joint.Axis);
                    mergedLink.Joint.AxisName = csvLink.Joint.AxisName;

                    mergedLink.Joint.Type = csvLink.Joint.Type;
                }
                else
                {
                    mergedLink.Joint.Origin.SetElementFromData(jointContext, data);
                    mergedLink.Joint.Axis.SetElementFromData(jointContext, data);
                    if (data.ContainsKey("Link.CoordSysName"))
                    {
                        mergedLink.Joint.CoordinateSystemName = data["Link.CoordSysName"];
                    }
                    if (data.ContainsKey("Link.AxisName"))
                    {
                        mergedLink.Joint.AxisName = data["Link.AxisName"];
                    }
                    if (data.ContainsKey("Link.Joint.type"))
                    {
                        mergedLink.Joint.Type = data["Link.Joint.type"];
                    }
                }
                mergedLink.UsesCsvJointKinematics = true;
            }

            if (UseCSVJointOther)
            {
                if (data == null)
                {
                    mergedLink.Joint.Limit.SetElement(csvLink.Joint.Limit);
                    mergedLink.Joint.Calibration.SetElement(csvLink.Joint.Calibration);
                    mergedLink.Joint.Dynamics.SetElement(csvLink.Joint.Dynamics);
                    mergedLink.Joint.Safety.SetElement(csvLink.Joint.Safety);
                }
                else
                {
                    mergedLink.Joint.Limit.SetElementFromData(jointContext, data);
                    mergedLink.Joint.Calibration.SetElementFromData(jointContext, data);
                    mergedLink.Joint.Dynamics.SetElementFromData(jointContext, data);
                    mergedLink.Joint.Safety.SetElementFromData(jointContext, data);
                }
                mergedLink.UsesCsvJointLimits = true;
            }

            return mergedLink;
        }
    }
}