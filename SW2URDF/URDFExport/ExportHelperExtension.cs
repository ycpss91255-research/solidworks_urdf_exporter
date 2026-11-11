/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SW2URDF.URDFExport
{
    public partial class ExportHelper
    {
        private string referenceSketchName;
        private string ExportErrorWhy;

        // What this run generated in the model (frames, axes, reference sketch) and the joint names
        // from before it, so a cancelled preview can undo exactly that. Created on first use because
        // some tests build an ExportHelper without running its constructor.
        private GeneratedFeatureLog generatedFeatures;
        private JointNameSnapshot jointNames;

        public GeneratedFeatureLog GeneratedFeatures
        {
            get { return generatedFeatures ?? (generatedFeatures = new GeneratedFeatureLog()); }
        }

        #region SW to Robot and link methods

        //Used right now only by the Part Exporter, but this starts the building of the robot
        public void CreateRobotFromActiveModel()
        {
            URDFRobot = new Robot();
            URDFRobot.Name = ActiveSWModel.GetTitle();

            Configuration swConfig = ActiveSWModel.ConfigurationManager?.ActiveConfiguration;
            string[] displayStates = swConfig?.GetDisplayStates() as string[];
            if (displayStates != null)
            {
                foreach (string state in displayStates)
                {
                    if (state.Equals("URDF Export"))
                    {
                        swConfig.ApplyDisplayState("URDF Export");
                    }
                }
            }

            //Each Robot contains a single base link, build this link
            Link baseLink = CreateBaseLinkFromActiveModel();
            URDFRobot.SetBaseLink(baseLink);
            EnsureBaseLink(URDFRobot, URDFRobot.Name);
        }

        /// <summary>
        /// The part exporter needs a base link to show. When none could be built (for example the
        /// active document is not a part) a default link is used instead of failing.
        /// </summary>
        public static void EnsureBaseLink(Robot robot, string name)
        {
            if (robot.BaseLink == null)
            {
                Link link = new Link(null);
                link.Name = name;
                robot.SetBaseLink(link);
            }
        }

        /// <summary>
        /// Name of the active SolidWorks configuration of the exported model, null if unknown.
        /// </summary>
        public string ActiveConfigurationName
        {
            get
            {
                try
                {
                    string name = ActiveSWModel?.ConfigurationManager?.ActiveConfiguration?.Name;
                    return string.IsNullOrEmpty(name) ? null : name;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Part exporter: replaces the inertial, visual and collision values of the freshly built
        /// base link by the ones saved for the active configuration (own attribute, else the
        /// shared legacy one). Returns false, leaving the link as computed, when nothing is saved.
        /// </summary>
        public bool LoadPartSettings()
        {
            LinkNode saved = ConfigurationSerialization.LoadBaseNodeFromModel(
                ActiveSWModel, out bool error, UiText.FromSolidWorks(iSwApp));
            if (error || saved == null)
            {
                return false;
            }
            ApplySavedSettings(URDFRobot.BaseLink, saved.Link);
            return true;
        }

        /// <summary>
        /// Part exporter: saves the current base link for the active configuration, silently.
        /// </summary>
        public void SavePartSettings()
        {
            ConfigurationSerialization.SaveConfigTreeXML(
                (SldWorks)iSwApp, ActiveSWModel, new LinkNode(URDFRobot.BaseLink), false, UiText.FromSolidWorks(iSwApp));
        }

        public static void ApplySavedSettings(Link target, Link saved)
        {
            if (target == null || saved == null)
            {
                return;
            }
            target.Inertial.SetElement(saved.Inertial);
            target.Visual.SetElement(saved.Visual);
            target.Collision.SetElement(saved.Collision);
        }

        // This method now only works for the part exporter
        private Link CreateBaseLinkFromActiveModel()
        {
            // If the model is a part
            if (ActiveSWModel.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                return CreateLinkFromPartModel(ActiveSWModel);
            }
            return null;
        }

        // This creates a Link from a Part ModelDoc. It basically just extracts the material
        // properties and saves them to the appropriate fields.
        private static Link CreateLinkFromPartModel(ModelDoc2 swModel)
        {
            Link Link = new Link(null);
            Link.Name = swModel.GetTitle();

            Link.isFixedFrame = false;

            //Get link properties from SolidWorks part
            IMassProperty swMass = swModel.Extension.CreateMassProperty();
            Link.Inertial.Mass.Value = swMass.Mass;

            // returned as double with values [Lxx, Lxy, Lxz, Lyx, Lyy, Lyz, Lzx, Lzy, Lzz]
            double[] moment = swMass.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);
            Link.Inertial.Inertia.SetMomentMatrix(moment);

            double[] centerOfMass = swMass.CenterOfMass;
            Link.Inertial.Origin.SetXYZ(centerOfMass);
            Link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // Will this ever not be zeros?
            Link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = swModel.MaterialPropertyValues;
            Link.Visual.Material.Color.Red = values[0];
            Link.Visual.Material.Color.Green = values[1];
            Link.Visual.Material.Color.Blue = values[2];
            Link.Visual.Material.Color.Alpha = 1.0 - values[7];
            Link.Visual.Material.Name = "material_" + Link.Name;

            return Link;
        }

        //This is only used by the Part Exporter, but it localizes the link to the Origin_global
        // coordinate system
        private static void LocalizeLink(Link Link, Matrix<double> GlobalTransform)
        {
            Matrix<double> GlobalTransformInverse = GlobalTransform.Inverse();
            Matrix<double> linkCoMTransform = MathOps.GetTranslation(Link.Inertial.Origin.GetXYZ());
            Matrix<double> localLinkCoMTransform = GlobalTransformInverse * linkCoMTransform;

            // The inertia is kept in URDF's sign convention already (see SetMomentMatrix) and is given
            // in the global frame. GlobalTransform takes link coordinates to global ones, so the
            // tensor in the link frame is R^T * I * R. GetMoment returns a symmetric matrix, so the
            // column-major constructor below reads it correctly.
            Matrix<double> linkGlobalMomentInertia =
                new DenseMatrix(3, 3, Link.Inertial.Inertia.GetMoment());
            Matrix<double> GlobalRotMat =
                GlobalTransform.SubMatrix(0, 3, 0, 3);
            Matrix<double> linkLocalMomentInertia =
                GlobalRotMat.Transpose() * linkGlobalMomentInertia * GlobalRotMat;

            Link.Inertial.Origin.SetXYZ(MathOps.GetXYZ(localLinkCoMTransform));
            Link.Inertial.Origin.SetRPY(new double[] { 0, 0, 0 });

            // Already in URDF's sign convention: written as it is, not converted a second time
            Link.Inertial.Inertia.SetUrdfMomentMatrix(linkLocalMomentInertia.ToRowMajorArray());

            LocalizeMeshOrigins(Link, GlobalTransform);
        }

        // Mesh placement is independent of the already-local assembly inertial properties.
        private static void LocalizeMeshOrigins(Link Link, Matrix<double> GlobalTransform)
        {
            Matrix<double> GlobalTransformInverse = GlobalTransform.Inverse();
            Matrix<double> linkVisualTransform =
                MathOps.GetTransformation(Link.Visual.Origin.GetXYZ(), Link.Visual.Origin.GetRPY());
            Matrix<double> localVisualTransform = GlobalTransformInverse * linkVisualTransform;

            Matrix<double> linkCollisionTransform =
                MathOps.GetTransformation(Link.Collision.Origin.GetXYZ(), Link.Collision.Origin.GetRPY());
            Matrix<double> localCollisionTransform = GlobalTransformInverse * linkCollisionTransform;

            Link.Collision.Origin.SetXYZ(MathOps.GetXYZ(localCollisionTransform));
            Link.Collision.Origin.SetRPY(MathOps.GetRPY(localCollisionTransform));

            Link.Visual.Origin.SetXYZ(MathOps.GetXYZ(localVisualTransform));
            Link.Visual.Origin.SetRPY(MathOps.GetRPY(localVisualTransform));
        }

        // The one used by the Assembly Exporter
        public bool CreateRobotFromTreeView(LinkNode baseNode)
        {
            ExportErrorWhy = "";
            URDFRobot = new Robot();

            // A new run: forget the previous one and remember the joint names it starts with
            GeneratedFeatures.Clear();
            jointNames = JointNameSnapshot.Take(baseNode);

            progressBar.Start(0, CommonSwOperations.GetCount(baseNode.Nodes) + 1, "Building links");
            int count = 0;

            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle("Building link: " + baseNode.Name);
            
            Link baseLink = CreateLink(baseNode, 1);
            if (baseLink == null || !string.IsNullOrWhiteSpace(ExportErrorWhy))
            {
                MessageBox.Show(ExportErrorWhy);
                logger.Warn(ExportErrorWhy);
                progressBar.End();

                // Nothing will be shown, so what was generated on the way is taken back
                DiscardGeneratedFeatures();
                return false;
            }
            URDFRobot.SetBaseLink(baseLink);
            baseNode.Link = baseLink;

            progressBar.End();
            return true;
        }

        private Link CreateBaseLinkFromComponents(LinkNode node)
        {
            // Build the link from the partdoc
            Link link = CreateLinkFromComponents(null, node);
            string savedFrame = node.Link.Joint.CoordinateSystemName;
            // A saved name whose frame is gone (the reference sketch was deleted, taking its frames
            // with it) is generated again, as it is for the other joints
            if (savedFrame == "Automatically Generate" ||
                (!string.IsNullOrWhiteSpace(savedFrame) && !CheckRefCoordsysExists(savedFrame)))
            {
                CreateBaseRefOrigin(true);
                if (!GeneratedFeatures.WasCreated(GeneratedKind.CoordinateSystem, "Origin_global"))
                {
                    // Already in the model: not ours to remove, but the joint's name still has to be
                    // put back if the run is cancelled
                    GeneratedFeatures.AddReused(GeneratedKind.CoordinateSystem, "Origin_global");
                }
                node.Link.Joint.CoordinateSystemName = "Origin_global";
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            }
            else
            {
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            }
            return link;
        }

        //Method which builds an entire link and iterates through.
        private Link CreateLink(LinkNode node, int count)
        {
            progressBar.UpdateTitle("Building link: " + node.Name);
            progressBar.UpdateProgress(count);
            Link link;
            if (node.IsBaseNode)
            {
                link = CreateBaseLinkFromComponents(node);
                URDFRobot.SetBaseLink(link);
            }
            else
            {
                LinkNode parentNode = (LinkNode)node.Parent;
                link = CreateLinkFromComponents(parentNode.Link, node);
            }
            node.Link = link;
            if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
            {
                return null;
            }

            // Reset list of children, don't worry the links that were saved are still attached to the child nodes
            link.Children.Clear();
            foreach (LinkNode child in node.Nodes)
            {
                Link childLink = CreateLink(child, count + 1);

                if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
                {
                    return null;
                }
                else
                {
                    link.Children.Add(childLink);
                }
            }
            return link;
        }

        /// <summary>
        /// Builds a MassProperty2 for the given components, evaluated with respect to the given
        /// coordinate system.
        ///
        /// This selects whole components rather than their Body2 bodies. That distinction matters:
        /// bodies carry only geometry, so mass properties computed from them are always
        /// volume * material density. Any values a user has assigned through
        /// Tools > Mass Properties > Override Mass Properties live on the component's
        /// configuration, not on its bodies, and are therefore silently discarded by the
        /// body-based calculation. Selecting components makes SolidWorks report the same numbers
        /// as its own Mass Properties dialog, overrides included.
        ///
        /// Returns null when this SolidWorks version or document cannot provide a MassProperty2,
        /// in which case the caller falls back to the body-based calculation.
        /// </summary>
        /// <param name="components">Components to evaluate, child components included automatically</param>
        /// <param name="coordinateSystemTransform">Coordinate system to evaluate against, or null for the document origin</param>
        /// <returns>A recalculated IMassProperty2, or null if unavailable</returns>
        private IMassProperty2 GetComponentsMassProperty(
            List<Component2> components, MathTransform coordinateSystemTransform)
        {
            // CreateMassProperty2 was introduced in SolidWorks 2020 (revision 28).
            // Older versions do not return null: calling the missing method would fail.
            int revision = int.Parse(iSwApp.RevisionNumber().Split('.')[0],
                System.Globalization.CultureInfo.InvariantCulture);
            if (revision < 28)
            {
                return null;
            }

            EnsureCalculableModel();
            IMassProperty2 swMass = (IMassProperty2)ActiveSWModel.Extension.CreateMassProperty2();
            if (swMass == null)
            {
                return null;
            }

            // Mass in kg and moments in kg*m^2 regardless of the document's unit system.
            swMass.UseSystemUnits = true;

            // The exporter hides components while exporting meshes, and users may keep an
            // assembly-level display state that hides cosmetic parts. Those parts still have mass,
            // and the body-based calculation this replaces always counted them.
            swMass.IncludeHiddenBodiesOrComponents = true;

            if (coordinateSystemTransform != null)
            {
                if (!swMass.SetCoordinateSystem(coordinateSystemTransform))
                {
                    throw new Exception("Failed to set the coordinate system for component mass properties");
                }
            }

            // This must be a Component2[]. Passing an object[] of components fails the COM call
            // with RPC_E_SERVERFAULT.
            swMass.SelectedItems = components.ToArray();

            if (!swMass.Recalculate())
            {
                throw new Exception("Failed to calculate mass properties of components " +
                    string.Join(", ", components.Select(component => component.Name2)));
            }

            return swMass;
        }

        /// <summary>
        /// Gets the Moment of Inertia of specific component bodies with respect to the coordinate system.
        /// This reuses some code with other methods because creating the mass property has to happen every time
        /// </summary>
        /// <param name="bodies">Component Bodies with which to get the MOI</param>
        /// <param name="coordinateSystemTransform">The coordinate system to take the MOI with respect to</param>
        /// <returns>Moment of Inertia array</returns>
        private double[] GetComponentsMomentOfInertia(List<Body2> bodies, MathTransform coordinateSystemTransform)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            swMass.SetCoordinateSystem(coordinateSystemTransform);
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }

            return (double[])swMass.GetMomentOfInertia(
            (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
        }

        /// <summary>
        /// Gets the components mass. This reuses some code with other methods because creating the
        /// mass property has to happen every time
        /// </summary>
        /// <param name="bodies">Component Bodies with which to get the mass</param>
        /// <returns>Mass value of component bodies</returns>
        private double GetCompomentsMass(List<Body2> bodies)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }
            return swMass.Mass;
        }

        /// <summary>
        /// Gets the Center of Mass with respect to the coordinate system. This reuses some code
        /// with other similar methods because creating the mass property has to happen every time.
        /// </summary>
        /// <param name="bodies">Component bodies with which to get the mass</param>
        /// <param name="coordinateSystemTransform">Coordinate system take get the centor of mess with respect to</param>
        /// <returns>3D double array of center of mass</returns>
        private double[] GetCompomentsCenterOfMass(List<Body2> bodies, MathTransform coordinateSystemTransform)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            swMass.SetCoordinateSystem(coordinateSystemTransform);
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }
            return swMass.CenterOfMass;
        }

        private void ComputeInertialProperties(Link link)
        {
            // A link without components has no mass. Neither SolidWorks API reports it that way:
            // an empty selection makes both fall back to the whole document, which would give this
            // link the mass and inertia of the entire robot.
            if (link.SWComponents == null || link.SWComponents.Count == 0)
            {
                logger.Info("Link " + link.Name + " has no components, setting its inertial " +
                    "properties to zero");
                link.Inertial.Mass.Value = 0;
                link.Inertial.Inertia.SetMomentMatrix(new double[9]);
                link.Inertial.Origin.SetXYZ(new double[3] { 0, 0, 0 });
                link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });
                return;
            }

            // Before the coordinate system transform is read: a rebuild afterwards could leave it stale
            EnsureCalculableModel();

            // Get the SolidWorks MathTransform that corresponds to the child coordinate system
            MathTransform jointTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);

            IMassProperty2 swMass = GetComponentsMassProperty(link.SWComponents, jointTransform);
            if (swMass != null && MassNeedsRetry(swMass.Mass, ActiveSWModel.Extension.NeedsRebuild2))
            {
                // Using a coordinate system that was only just inserted can leave the model with a
                // pending rebuild and then SolidWorks reports mass 0. Rebuild once and ask again,
                // with the coordinate system read again.
                logger.Info("Mass of link " + link.Name + " is 0 with a rebuild pending, " +
                    "rebuilding the model and calculating it again");
                ActiveSWModel.ForceRebuild3(true);
                jointTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);
                swMass = GetComponentsMassProperty(link.SWComponents, jointTransform);
            }
            if (swMass == null)
            {
                logger.Warn("MassProperty2 is unavailable for link " + link.Name +
                    ", falling back to calculating mass properties from bodies. Mass properties " +
                    "overridden in SolidWorks will not be reflected in the export.");
                ComputeInertialPropertiesFromBodies(link, jointTransform);
                return;
            }

            // Returned as double with values [Lxx, Lxy, Lxz, Lyx, Lyy, Lyz, Lzx, Lzy, Lzz], taken
            // about the center of mass but oriented with the axes of the coordinate system set
            // above. That is what URDF's inertia element expects.
            double[] moment = (double[])swMass.GetMomentOfInertia(
                (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
            link.Inertial.Inertia.SetMomentMatrix(moment);

            link.Inertial.Mass.Value = swMass.Mass;

            double[] centerOfMass = (double[])swMass.CenterOfMass;
            link.Inertial.Origin.SetXYZ(centerOfMass);
            link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });
        }

        /// <summary>
        /// Computes a link's inertial properties from its bodies' geometry and material density.
        ///
        /// Kept only as a last resort for when MassProperty2 is unavailable. It is known to be
        /// wrong in two ways: it cannot see mass properties the user has overridden in
        /// SolidWorks, and for links built from a subassembly it misplaces the bodies, which
        /// corrupts the center of mass and the moment of inertia.
        /// </summary>
        private void ComputeInertialPropertiesFromBodies(Link link, MathTransform jointTransform)
        {
            EnsureCalculableModel();
            List<Body2> bodies = GetBodies(link.SWComponents);

            double[] moment = GetComponentsMomentOfInertia(bodies, jointTransform);
            link.Inertial.Inertia.SetMomentMatrix(moment);

            link.Inertial.Mass.Value = GetCompomentsMass(bodies);

            double[] centerOfMass = GetCompomentsCenterOfMass(bodies, jointTransform);
            link.Inertial.Origin.SetXYZ(centerOfMass);
            link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });
        }

        private static void ComputeVisualCollisionProperties(Link link)
        {
            link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            if (link.SWComponents.Count == 0)
            {
                return;
            }

            ModelDoc2 mainCompdoc = link.SWComponents[0].GetModelDoc2();

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = mainCompdoc.MaterialPropertyValues;
            link.Visual.Material.Color.Red = values[0];
            link.Visual.Material.Color.Green = values[1];
            link.Visual.Material.Color.Blue = values[2];
            link.Visual.Material.Color.Alpha = 1.0 - values[7];
        }

        //Method which builds a single link
        private Link CreateLinkFromComponents(Link parent, LinkNode node)
        {
            node.Link.isFixedFrame = node.Link.SWComponents.Count == 0;
            node.Link.SWMainComponent = null;
            if (node.Link.isFixedFrame && parent != null)
            {
                node.Link.Joint.Type = "fixed";
                node.Link.Joint.Parent.Name = parent.Name;
                node.Link.Joint.Child.Name = node.Link.Name;
            }
            if (node.Link.SWComponents.Count > 0)
            {
                List<Component2> components = node.Link.SWComponents;
                node.Link.SWMainComponent = components[0];
            }

            if (parent != null && ComputeJointKinematics)
            {
                logger.Info("Creating joint " + node.Link.Name);
                bool error = CreateJoint(parent, node.Link);
                if (error)
                {
                    logger.Warn(
                        string.Format("Creating joint from parent {0} to child {1} failed", 
                            parent.Name, node.Link.Name));
                }
            }

            if (ComputeInertialValues && !node.Link.isFixedFrame)
            {
                ComputeInertialProperties(node.Link);
            }

            if (ComputeVisualCollision && !node.Link.isFixedFrame)
            {
                ComputeVisualCollisionProperties(node.Link);
            }

            return node.Link;
        }

        private List<Body2> GetBodies(List<Component2> components)
        {
            List<Body2> bodies = new List<Body2>();
            foreach (Component2 comp in components)
            {
                // Retrieving the Body2 bodies of the component. Also need to recur through the assembly tree
                object[] componentBodies =
                    (object[])comp.GetBodies3((int)swBodyType_e.swSolidBody, out _);
                if (componentBodies != null)
                {
                    foreach (Body2 obj in componentBodies)
                    {
                        bodies.Add(obj);
                    }
                }
                object[] children = comp.GetChildren();
                if (children != null)
                {
                    List<Component2> childComponents = new List<Component2>();
                    foreach (Component2 child in children)
                    {
                        childComponents.Add(child);
                    }
                    bodies.AddRange(GetBodies(childComponents));
                }
            }
            return bodies;
        }

        #endregion SW to Robot and link methods

        #region Joint methods

        //Base method for constructing a joint from a parent link and child link.
        private bool CreateJoint(Link parent, Link child)
        {
            CheckRefGeometryExists(child);
            RefreshGeneratedReferences(parent, child);

            string coordSysName = child.Joint.CoordinateSystemName;
            string axisName = child.Joint.AxisName;
            string jointType = child.Joint.Type;

            child.Joint.Parent.Name = parent.Name;
            child.Joint.Child.Name = child.Name;
            if (child.isFixedFrame)
            {
                if (coordSysName == "Automatically Generate")
                {
                    ExportErrorWhy = string.Format(
                        UiText.FromSolidWorks(iSwApp).Get("Select an existing reference coordinate system for empty link {0}"), child.Name);
                    return false;
                }
                axisName = "";
                jointType = "fixed";
                child.Joint.Type = jointType;
            }
            else if (coordSysName == "Automatically Generate" ||
                (JointUsesAxis(jointType) && axisName == "Automatically Generate") ||
                jointType == "Automatically Detect")
            {
                // We have to estimate the joint if the user specifies automatic for either the
                // reference coordinate system, the reference axis or the joint type.
                EstimateGlobalJointFromComponents(parent, child);
                bool autoGenerateError = (
                    child.Joint.Origin.X == 0.0 && child.Joint.Origin.Y == 0.0 && child.Joint.Origin.Z == 0.0 &&
                    child.Joint.Origin.Roll == 0.0 && child.Joint.Origin.Pitch == 0.0 && child.Joint.Origin.Yaw == 0.0);

                if (autoGenerateError)
                {
                    ExportErrorWhy = string.Format("Inferring the joint geometry failed for the joint {0} " +
                        "from link {1} to {2} failed. Check that the mates have not fully defined the " +
                        "components in link {1} and that there is exactly one degree of freedom.",
                        child.Joint.Name, child.Name, parent.Name);
                    return false;
                }
            }

            if (coordSysName == "Automatically Generate")
            {
                // An existing frame of the same name and place (from an earlier preview) is used
                // again; any other existing frame is left alone and the next free name is used.
                Origin wanted = child.Joint.Origin;
                child.Joint.CoordinateSystemName = GeneratedFeatureLog.ChooseName(
                    "Origin_" + child.Joint.Name, CoordinateSystemExists,
                    name => CoordinateSystemIsAt(name, wanted), out bool reuse);
                if (reuse)
                {
                    GeneratedFeatures.AddReused(
                        GeneratedKind.CoordinateSystem, child.Joint.CoordinateSystemName);
                }
                else
                {
                    CreateRefOrigin(child.Joint);
                }
            }

            if (axisName == "Automatically Generate")
            {
                double[] wantedAxis = child.Joint.Axis.GetXYZ();
                child.Joint.AxisName = GeneratedFeatureLog.ChooseName(
                    "Axis_" + child.Joint.Name, AxisExists,
                    name => AxisPointsAlong(name, wantedAxis), out bool reuse);
                if (reuse || child.Joint.Type == "fixed")
                {
                    GeneratedFeatures.AddReused(GeneratedKind.Axis, child.Joint.AxisName);
                }
                else
                {
                    CreateRefAxis(child.Joint);
                }
            }

            EstimateGlobalJointFromRefGeometry(child);

            coordSysName = parent.Joint.CoordinateSystemName;

            LocalizeJoint(child.Joint, coordSysName);
            return true;
        }

        // The saved settings name the coordinate system and axis of a joint. When an earlier export
        // generated them (they hang on the "URDF Reference" sketch) they are only right for the
        // geometry they were generated in: the position of the frame does not follow the
        // components when another SolidWorks configuration is activated or the model is edited.
        // The position the current geometry gives is worked out on a copy of the link, and a
        // frame (or axis) that is somewhere else is replaced by the one for the current geometry,
        // an existing one at the right place or a new one. The earlier one is left alone, and the
        // joint type, limits and everything else saved for the joint are kept.
        private void RefreshGeneratedReferences(Link parent, Link child)
        {
            if (child.isFixedFrame || child.SWMainComponent == null ||
                child.Joint.CoordinateSystemName == "Automatically Generate")
            {
                return;
            }

            string frameName = child.Joint.CoordinateSystemName;
            string axisName = child.Joint.AxisName;
            bool frameGenerated = IsGeneratedFeature(frameName, "COORDSYS");
            bool axisGenerated = JointUsesAxis(child.Joint.Type) &&
                axisName != "Automatically Generate" && IsGeneratedFeature(axisName, "AXIS");
            if (!frameGenerated && !axisGenerated)
            {
                return;
            }

            Link probe = new Link { Name = child.Name };
            probe.Joint.Name = child.Joint.Name;
            probe.SWComponents.AddRange(child.SWComponents);
            probe.SWMainComponent = child.SWMainComponent;
            EstimateGlobalJointFromComponents(parent, probe);
            Origin wanted = probe.Joint.Origin;
            if (wanted.X == 0.0 && wanted.Y == 0.0 && wanted.Z == 0.0 &&
                wanted.Roll == 0.0 && wanted.Pitch == 0.0 && wanted.Yaw == 0.0)
            {
                logger.Warn("The place of joint " + child.Joint.Name +
                    " could not be worked out, its saved coordinate system and axis are used as they are");
                return;
            }

            if (frameGenerated)
            {
                string name = GeneratedFeatureLog.RefreshName(
                    frameName, "Origin_" + child.Joint.Name, CoordinateSystemExists,
                    n => IsGeneratedFeature(n, "COORDSYS"), n => CoordinateSystemIsAtPlace(n, wanted),
                    out bool reuse, out bool changed);
                if (changed)
                {
                    logger.Info("Coordinate system " + frameName + " does not fit the active configuration, using " + name);
                    if (reuse)
                    {
                        GeneratedFeatures.AddReused(GeneratedKind.CoordinateSystem, name);
                    }
                    else
                    {
                        CreateRefOrigin(wanted, name);
                    }
                    child.Joint.CoordinateSystemName = name;
                }
            }

            if (axisGenerated && probe.Joint.Type != "fixed")
            {
                double[] wantedAxis = probe.Joint.Axis.GetXYZ();
                string name = GeneratedFeatureLog.RefreshName(
                    axisName, "Axis_" + child.Joint.Name, AxisExists,
                    n => IsGeneratedFeature(n, "AXIS"), n => AxisPointsAlong(n, wantedAxis),
                    out bool reuse, out bool changed);
                if (changed)
                {
                    logger.Info("Axis " + axisName + " does not fit the active configuration, using " + name);
                    if (reuse)
                    {
                        GeneratedFeatures.AddReused(GeneratedKind.Axis, name);
                    }
                    else
                    {
                        probe.Joint.CoordinateSystemName = child.Joint.CoordinateSystemName;
                        probe.Joint.AxisName = name;
                        CreateRefAxis(probe.Joint);
                    }
                    child.Joint.AxisName = name;
                }
            }
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Joint Joint)
        {
            CreateRefOrigin(Joint.Origin, Joint.CoordinateSystemName);
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Origin Origin, string CoordinateSystemName)
        {
            // Adds the sketch segments and point to the 3D sketch. The sketchEnties are the actual
            // items created (and their locations)
            object[] sketchEntities = AddSketchGeometry(Origin);

            SketchPoint OriginPoint = (SketchPoint)sketchEntities[0];
            SketchSegment xaxis = (SketchSegment)sketchEntities[1];
            SketchSegment yaxis = (SketchSegment)sketchEntities[2];

            double originX = (double)sketchEntities[3]; //OriginPoint X
            double originY = (double)sketchEntities[4];
            double originZ = (double)sketchEntities[5];

            double xAxisX = (double)sketchEntities[6];
            double xAxisY = (double)sketchEntities[7];
            double xAxisZ = (double)sketchEntities[8];

            double yAxisX = (double)sketchEntities[9];
            double yAxisY = (double)sketchEntities[10];
            double yAxisZ = (double)sketchEntities[11];

            ActiveSWModel.ClearSelection2(true);
            SelectionMgr selectionManager = ActiveSWModel.SelectionManager;
            SelectData data = selectionManager.CreateSelectData();

            // First select the origin
            bool SelectedOrigin = false;
            bool SelectedXAxis = false;
            bool SelectedYAxis = false;
            if (OriginPoint != null)
            {
                data.Mark = 1;
                SelectedOrigin = OriginPoint.Select4(true, data);
            }
            if (!SelectedOrigin)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", originX, originY, originZ, true, 1, null, 0);
            }

            // Second, select the xaxis
            if (xaxis != null)
            {
                data.Mark = 2;
                SelectedXAxis = xaxis.Select4(true, data);
            }
            if (!SelectedXAxis)
            {
               ActiveSWModel.Extension.SelectByID2
                 ("", "EXTSKETCHPOINT", xAxisX, xAxisY, xAxisZ, true, 2, null, 0);
            }

            // Third, select the yaxis
            if (yaxis != null)
            {
                data.Mark = 4;
                SelectedYAxis = yaxis.Select4(true, data);
            }
            if (!SelectedYAxis)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", yAxisX, yAxisY, yAxisZ, true, 4, null, 0);
            }

            //From the selected items, insert a coordinate system.
            Feature coordinates =
                ActiveSWModel.FeatureManager.InsertCoordinateSystem(false, false, false);
            if (coordinates != null)
            {
                coordinates.Name = CoordinateSystemName;
                GeneratedFeatures.AddCreated(
                    GeneratedKind.CoordinateSystem, coordinates.Name, coordinates);
            }
        }

        //Creates the Origin_global coordinate system
        private void CreateBaseRefOrigin(bool zIsUp)
        {
            if (!ActiveSWModel.Extension.SelectByID2(
                    "Origin_global", "COORDSYS", 0, 0, 0, false, 0, null, 0))
            {
                Joint Joint = new Joint();
                if (zIsUp)
                {
                    Joint.Origin.SetRPY(new double[] { -Math.PI / 2, 0, 0 });
                }
                else
                {
                    Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
                }
                Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
                Joint.CoordinateSystemName = "Origin_global";
                EnsureReferenceSketch();
                CreateRefOrigin(Joint);
            }
        }

        // Materializes a top-level reference coordinate system at the global pose of a
        // (possibly nested) coordinate system, and returns its name. The STL export
        // coordinate-system preference only resolves coordinate systems at the top level of the
        // document being saved, so a system defined inside a sub-assembly cannot be used to
        // localize the mesh and SOLIDWORKS falls back to the global frame.
        // The global transform is taken from GetCoordinateSystemTransform -- the same one the
        // joint origin uses, which composes the owning component's Transform2 and is therefore
        // correct at any nesting depth -- so the generated top-level system coincides with the
        // selected one and the mesh is baked at the link origin, exactly as the auto-generated
        // Origin_<joint> systems already are. Returns null if the transform cannot be resolved.
        private string CreateGlobalMeshCoordinateSystem(Link link, string coordsysName)
        {
            MathTransform globalTransform = GetCoordinateSystemTransform(coordsysName);
            if (globalTransform == null)
            {
                logger.Warn("Could not resolve a global transform for '" + coordsysName +
                    "'; mesh for link " + link.Name + " will be exported in the global frame");
                return null;
            }

            Joint meshFrame = new Joint();
            meshFrame.Origin.SetXYZ(MathOps.GetXYZ(globalTransform));
            meshFrame.Origin.SetRPY(MathOps.GetRPY(globalTransform));

            // Like the generated joint frames: one of the same name and place (from an earlier
            // export or preview) is used again, any other one is left alone and the next free
            // name is taken. The name starts with Origin_ so that a cancelled preview removes it
            // and a completed export files it in the URDF Export Configuration folder.
            Origin wanted = meshFrame.Origin;
            string name = GeneratedFeatureLog.ChooseName(
                "Origin_mesh_" + link.Name.Replace('/', '_'), CoordinateSystemExists,
                existing => CoordinateSystemIsAt(existing, wanted), out bool reuse);
            if (reuse)
            {
                GeneratedFeatures.AddReused(GeneratedKind.CoordinateSystem, name);
                return name;
            }
            meshFrame.CoordinateSystemName = name;
            CreateRefOrigin(meshFrame);
            return name;
        }

        // Creates a Reference Axis to be used to calculate the joint axis
        private void CreateRefAxis(Joint Joint)
        {
            //Adds sketch segment
            SketchSegment rotaxis = AddSketchGeometry(Joint.Axis, Joint.Origin, Joint.CoordinateSystemName);
            if (rotaxis != null)
            {
                //Use special method to create the axis
                Feature featAxis = InsertAxis(rotaxis);
                if (featAxis != null)
                {
                    featAxis.Name = Joint.AxisName;
                    GeneratedFeatures.AddCreated(GeneratedKind.Axis, featAxis.Name, featAxis);
                }
            }
        }

        // Takes a links joint and calculates the local transform from the global transforms of
        // the parent and child. It also converts the axis to local values
        private void LocalizeJoint(Joint Joint, string parentCoordsysName)
        {
            MathTransform parentTransform = GetCoordinateSystemTransform(parentCoordsysName);
            
            Matrix<double> ParentJointGlobalTransform =
                MathOps.GetTransformation(parentTransform);
            MathTransform coordsysTransform =
                GetCoordinateSystemTransform(Joint.CoordinateSystemName);
           
            //Transform from global origin to child joint
            Matrix<double> ChildJointGlobalTransform =
                MathOps.GetTransformation(coordsysTransform);
            Matrix<double> ChildJointOrigin =
                ParentJointGlobalTransform.Inverse() * ChildJointGlobalTransform;
            
            //Localize the axis to the Link's coordinate system.
            Joint.Axis.SetXYZ(LocalizeAxis(Joint.Axis.GetXYZ(), Joint.CoordinateSystemName));

            // Get the array values and threshold them so small values are set to 0.
            Joint.Origin.SetXYZ(MathOps.GetXYZ(ChildJointOrigin));
            Joint.Origin.SetXYZ(MathOps.Threshold(Joint.Origin.GetXYZ(), 0.00001));
            Joint.Origin.SetRPY(MathOps.GetRPY(ChildJointOrigin));
            Joint.Origin.SetRPY(MathOps.Threshold(Joint.Origin.GetRPY(), 0.00001));
        }

        // Funny method I created that inserts a RefAxis and then finds the reference to it.
        private Feature InsertAxis(SketchSegment axis)
        {
            //First select the axis
            SelectData data = ActiveSWModel.SelectionManager.CreateSelectData();
            axis.Select4(false, data);

            //Get the features before the axis is created
            object[] featuresBefore, featuresAfter;
            featuresBefore = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            //Create the axis
            ActiveSWModel.InsertAxis2(true);

            //Get the features after the axis is created
            featuresAfter = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            // If it was created, try to find it
            if (featuresBefore.Length < featuresAfter.Length)
            {
                //It was probably added at the end (hence .Reverse())
                foreach (Feature feat in featuresAfter.Reverse())
                {
                    //If the feature in featuresAfter is not in features before, its gotta be the
                    // axis we inserted
                    if (!featuresBefore.Contains(feat))
                    {
                        return feat;
                    }
                }
            }
            return null;
        }

        // The auto-generated coordinate systems and axes are built from the points and lines of
        // the "URDF Reference" 3D sketch, so every one of them needs it. It is created the first
        // time one is needed, which is not only for Origin_global: when the user picks their own
        // global origin, the sketch is still needed for the joints set to "Automatically Generate".
        private void EnsureReferenceSketch()
        {
            if (referenceSketchName == null)
            {
                referenceSketchName = Setup3DSketch();
            }
            else
            {
                UnsuppressInActiveConfiguration(referenceSketchName, "SKETCH");
            }
        }

        /// <summary>
        /// A mass of 0 together with a pending rebuild is not a result: SolidWorks has not built the
        /// geometry yet. Free of SolidWorks calls so it can be tested.
        /// </summary>
        public static bool MassNeedsRetry(double mass, int rebuildStatus)
        {
            return mass == 0.0 && rebuildStatus != (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt;
        }

        // Mass properties are only valid for a model that is rebuilt and not in sketch edit mode.
        // With a rebuild pending (the state right after opening a model, and again after a
        // coordinate system was inserted) SolidWorks has not built the geometry of the components
        // yet and reports volume 0 and mass 0 for them. A top level rebuild is enough.
        private void EnsureCalculableModel()
        {
            EnsureNoActiveSketch();
            if (ActiveSWModel.Extension.NeedsRebuild2 !=
                (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt)
            {
                logger.Info("The model has a rebuild pending, rebuilding it before reading mass properties");
                ActiveSWModel.ForceRebuild3(true);
            }
        }

        // Leaves sketch edit mode when some sketch is in it. SolidWorks reports mass 0 for components
        // while a sketch is being edited, so nothing that queries mass properties may run then. It
        // also protects the Insert3DSketch(true) calls below, which only toggle: with a sketch
        // already open the pair of calls in Setup3DSketch would end with the sketch still open.
        private void EnsureNoActiveSketch()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Sketch active = ActiveSWModel.SketchManager.ActiveSketch;
                if (active == null)
                {
                    return;
                }
                logger.Warn("A sketch was in edit mode, leaving it");
                if (active.Is3D())
                {
                    ActiveSWModel.SketchManager.Insert3DSketch(true);
                }
                else
                {
                    ActiveSWModel.SketchManager.InsertSketch(true);
                }
            }
        }

        // A feature added while another configuration was active is suppressed in this one when
        // the configuration has "Suppress new features" ticked. A suppressed sketch cannot be
        // edited, so the points of a new frame could not be added.
        private void UnsuppressInActiveConfiguration(string name, string selectType)
        {
            try
            {
                Feature feature = FindFeature(name, selectType);
                if (feature != null && IsSuppressedHere(feature))
                {
                    logger.Info(name + " is suppressed in the active configuration, unsuppressing it there");
                    feature.SetSuppression2(
                        (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration, null);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not unsuppress " + name, e);
            }
        }

        private static bool IsSuppressedHere(Feature feature)
        {
            bool[] suppressed = feature.IsSuppressed2(
                (int)swInConfigurationOpts_e.swThisConfiguration, null) as bool[];
            return suppressed != null && suppressed.Length > 0 && suppressed[0];
        }

        private Feature FindFeature(string name, string selectType)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            ActiveSWModel.ClearSelection2(true);
            Feature feature = null;
            if (ActiveSWModel.Extension.SelectByID2(name, selectType, 0, 0, 0, false, 0, null, 0))
            {
                feature = ActiveSWModel.SelectionManager.GetSelectedObject6(1, -1) as Feature;
            }
            ActiveSWModel.ClearSelection2(true);
            return feature;
        }

        // True for a coordinate system or axis that was made from the "URDF Reference" sketch, which
        // is what the exporter does; one the user made is not
        private bool IsGeneratedFeature(string name, string selectType)
        {
            try
            {
                Feature feature = FindFeature(name, selectType);
                object[] parents = feature == null ? null : feature.GetParents() as object[];
                return parents != null && parents.OfType<Feature>().Any(p => p.Name == "URDF Reference");
            }
            catch (Exception e)
            {
                logger.Warn("Could not tell whether " + name + " was generated", e);
                return false;
            }
        }

        // Inserts a sketch into the main assembly and name it
        private string Setup3DSketch()
        {
            EnsureNoActiveSketch();
            bool sketchExists =
                ActiveSWModel.Extension.SelectByID2(
                    "URDF Reference", "SKETCH", 0, 0, 0, false, 0, null, 0);
            if (sketchExists)
            {
                // From an earlier preview or made by the user: used as it is, nothing is added
                ActiveSWModel.ClearSelection2(true);
                UnsuppressInActiveConfiguration("URDF Reference", "SKETCH");
                return "URDF Reference";
            }
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            ActiveSWModel.SketchManager.CreatePoint(0, 0, 0);
            IFeature sketch = (IFeature)ActiveSWModel.SketchManager.ActiveSketch;
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            sketch.Name = "URDF Reference";
            GeneratedFeatures.AddCreated(GeneratedKind.Sketch, sketch.Name, sketch);
            return sketch.Name;
        }

        #region Generated features: lookups and clean-up

        private bool CoordinateSystemExists(string name)
        {
            ActiveSWModel.ClearSelection2(true);
            bool exists = ActiveSWModel.Extension.SelectByID2(
                name, "COORDSYS", 0, 0, 0, false, 0, null, 0);
            ActiveSWModel.ClearSelection2(true);
            return exists;
        }

        private bool AxisExists(string name)
        {
            ActiveSWModel.ClearSelection2(true);
            bool exists = ActiveSWModel.Extension.SelectByID2(
                name, "AXIS", 0, 0, 0, false, 0, null, 0);
            ActiveSWModel.ClearSelection2(true);
            return exists;
        }

        // True when the existing coordinate system is where a freshly generated one would be put
        private bool CoordinateSystemIsAt(string name, Origin wanted)
        {
            try
            {
                double[] existing = MathOps.GetTransformation(GetCoordinateSystemTransform(name))
                    .ToRowMajorArray();
                double[] expected = MathOps.GetTransformation(wanted.GetXYZ(), wanted.GetRPY())
                    .ToRowMajorArray();
                return GeneratedFeatureLog.SameValues(existing, expected, 1e-6);
            }
            catch (Exception e)
            {
                logger.Warn("Could not compare the coordinate system " + name, e);
                return false;
            }
        }

        // True when the existing coordinate system is at the wanted position, whatever its orientation
        private bool CoordinateSystemIsAtPlace(string name, Origin wanted)
        {
            try
            {
                double[] existing = MathOps.GetTransformation(GetCoordinateSystemTransform(name))
                    .ToRowMajorArray();
                return GeneratedFeatureLog.SamePlace(existing, wanted.GetXYZ(), 1e-5);
            }
            catch (Exception e)
            {
                logger.Warn("Could not compare the coordinate system " + name, e);
                return false;
            }
        }

        // True when the existing axis runs along the wanted direction (either way round)
        private bool AxisPointsAlong(string name, double[] wanted)
        {
            try
            {
                return GeneratedFeatureLog.SameDirection(EstimateAxis(name), wanted, 1e-6);
            }
            catch (Exception e)
            {
                logger.Warn("Could not compare the axis " + name, e);
                return false;
            }
        }

        /// <summary>
        /// Undoes what this run of the exporter generated in the model: the joints get their earlier
        /// coordinate system and axis names back and the coordinate systems, axes and reference
        /// sketch that the run created are deleted. Features that were in the model before, and
        /// anything the user made, are never touched. Does nothing a second time. Never throws.
        /// </summary>
        public void DiscardGeneratedFeatures()
        {
            GeneratedFeatureLog log = GeneratedFeatures;
            try
            {
                if (jointNames != null)
                {
                    jointNames.Restore(log);
                }
                if (ActiveSWModel != null && !log.IsEmpty)
                {
                    logger.Info("Removing the " + log.Created.Count + " feature(s) generated for the preview");
                    foreach (GeneratedFeature feature in log.RemovalOrder())
                    {
                        DeleteGeneratedFeature(feature);
                    }
                    if (!log.Created.Any(f => f.Kind == GeneratedKind.Sketch))
                    {
                        DeleteAddedSketchEntities(log);
                    }
                    ActiveSWModel.ClearSelection2(true);
                    ActiveSWModel.ForceRebuild3(true);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not remove the features generated for the preview", e);
            }
            finally
            {
                log.Clear();
                jointNames = null;
                referenceSketchName = null;
            }
        }

        private void DeleteGeneratedFeature(GeneratedFeature generated)
        {
            try
            {
                Feature feature = generated.Handle as Feature;
                // The feature must still be the one that was created: not deleted, renamed or
                // replaced since (undo, or the user's own edits)
                if (feature == null || feature.Name != generated.Name)
                {
                    logger.Info("Generated feature " + generated.Name + " is no longer there, skipped");
                    return;
                }
                object[] children = feature.GetChildren() as object[];
                if (children != null && children.Length > 0)
                {
                    logger.Info("Generated feature " + generated.Name + " is used by other features, kept");
                    return;
                }
                ActiveSWModel.ClearSelection2(true);
                if (feature.Select2(false, 0))
                {
                    ActiveSWModel.Extension.DeleteSelection2(0);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not remove the generated feature " + generated.Name, e);
            }
        }

        // The points and lines this run added to a reference sketch that was already there
        private void DeleteAddedSketchEntities(GeneratedFeatureLog log)
        {
            if (log.AddedSketchEntities.Count == 0 || referenceSketchName == null)
            {
                return;
            }
            try
            {
                ActiveSWModel.ClearSelection2(true);
                if (!ActiveSWModel.Extension.SelectByID2(
                    referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0))
                {
                    return;
                }
                ActiveSWModel.SketchManager.Insert3DSketch(true);
                SelectData data = ActiveSWModel.SelectionManager.CreateSelectData();
                bool append = false;
                foreach (GeneratedFeature entity in log.AddedSketchEntities)
                {
                    try
                    {
                        bool selected = false;
                        if (entity.Handle is SketchPoint)
                        {
                            selected = ((SketchPoint)entity.Handle).Select4(append, data);
                        }
                        else if (entity.Handle is SketchSegment)
                        {
                            selected = ((SketchSegment)entity.Handle).Select4(append, data);
                        }
                        append = append || selected;
                    }
                    catch (Exception e)
                    {
                        logger.Warn("A sketch entity of the preview could not be selected", e);
                    }
                }
                if (append)
                {
                    ActiveSWModel.Extension.DeleteSelection2(0);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not remove the sketch entities of the preview", e);
            }
            finally
            {
                ActiveSWModel.ClearSelection2(true);
                if (ActiveSWModel.SketchManager.ActiveSketch != null)
                {
                    ActiveSWModel.SketchManager.Insert3DSketch(true);
                }
            }
        }

        #endregion Generated features: lookups and clean-up

        // Adds lines and a point to create the entities for a reference coordinates
        private object[] AddSketchGeometry(Origin Origin)
        {
            //Find if the sketch exists first
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                EnsureReferenceSketch();
                bool sketchExists =
                    ActiveSWModel.Extension.SelectByID2(
                        referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                if (!sketchExists)
                {
                    throw new Exception("Reference sketch " + referenceSketchName + " does not exist");
                }
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            //Calculate the lines that need to be drawn
            Matrix<double> transform = MathOps.GetRotation(Origin.GetRPY());
            Matrix<double> Axes = 0.01 * DenseMatrix.CreateIdentity(4);
            Matrix<double> tA = transform * Axes;

            // origin at X, Y, Z
            SketchPoint OriginPoint = ActiveSWModel.SketchManager.CreatePoint(Origin.X,
                                                                      Origin.Y,
                                                                      Origin.Z);

            // xAxis is a 1cm line from the origin in the direction of the xaxis of the coordinate system
            SketchSegment XAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 0],
                                                                         Origin.Y + tA[1, 0],
                                                                         Origin.Z + tA[2, 0]);
            XAxis.ConstructionGeometry = true;

            //yAxis is a 1cm line from the origin in the direction of the yaxis of the coordinate system
            SketchSegment YAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 1],
                                                                         Origin.Y + tA[1, 1],
                                                                         Origin.Z + tA[2, 1]);
            YAxis.ConstructionGeometry = true;

            // Remembered so that a cancelled preview can take them out of a sketch that was already
            // there (a sketch created by the run is deleted as a whole)
            GeneratedFeatures.AddSketchEntity(OriginPoint);
            GeneratedFeatures.AddSketchEntity(XAxis);
            GeneratedFeatures.AddSketchEntity(YAxis);

            //Close the sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            // Return an array of objects representing the sketch items that were just inserted,
            // as well as the actual locations of those objecs (aids selection).
            return new object[] { OriginPoint, XAxis, YAxis,
                Origin.X, Origin.Y, Origin.Z,
                Origin.X + tA[0, 0], Origin.Y + tA[1, 0], Origin.Z + tA[2, 0],
                Origin.X + tA[0, 1], Origin.Y + tA[1, 1], Origin.Z + tA[2, 1] };
        }

        //Inserts a sketch segment for use when creating a Reference Axis
        private SketchSegment AddSketchGeometry(Axis axis, Origin origin, string coordSysName)
        {
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                EnsureReferenceSketch();
                ActiveSWModel.Extension.SelectByID2(
                    referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            bool flip = CheckReverseAxis(axis, coordSysName);
            double sign = (flip) ? -1.0 : 1.0;

            //Insert sketch segment 0.1m long centered on the origin.
            SketchSegment rotAxis = ActiveSWModel.SketchManager.CreateLine(
                origin.X + sign * 0.05 * axis.X,
                origin.Y + sign * 0.05 * axis.Y,
                origin.Z + sign * 0.05 * axis.Z,
                origin.X - sign * 0.05 * axis.X,
                origin.Y - sign * 0.05 * axis.Y,
                origin.Z - sign * 0.05 * axis.Z);
            if (rotAxis == null)
            {
                return null;
            }
            rotAxis.ConstructionGeometry = true;
            rotAxis.Width = 2;
            GeneratedFeatures.AddSketchEntity(rotAxis);

            //Close sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            return rotAxis;
        }

        // Checks if an axis to be created should be flipped, so as to favor positive directions of rotation
        // This prefers that the first non-zero value be positive
        private bool CheckReverseAxis(Axis axis, string coordSysName)
        {
            //axis is a double[] {x, y, z}
            double[] transformedAxis = LocalizeAxis(axis.GetXYZ(), coordSysName);

            // If x is negative, flip
            if (transformedAxis[0] < 0)
            {
                return true;
            }
            // Else if x is 0 and y is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 && transformedAxis[1] < 0)
            {
                return true;
            }
            // Else if x and y are 0 and z is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 &&
                     Math.Abs(transformedAxis[1]) < 0.00001 &&
                     transformedAxis[2] < 0)
            {
                return true;
            }
            return false;
        }

        //Calculates the free degree of freedom (if exists), and then determines the location of the joint,
        // the axis of rotation/translation, and the type of joint
        public Boolean EstimateGlobalJointFromComponents(Link parent, Link child)
        {
            //Create the ref objects
            int degreesOfFreedom;

            // Fix parent components so that only the actual degree of freedom can be detected.
            List<Component2> fixedComponents = FixComponents(parent);

            // Surpress Limit Mates to properly find degrees of freedom. They don't work with the API call
            List<Mate2> limitMates = SuppressLimitMates(child.SWMainComponent);
            Boolean success = false;
            if (child.SWMainComponent != null)
            {
                // The wonderful undocumented API call I found to get the degrees of freedom in a joint.
                // https://forum.solidworks.com/thread/57414
                int remainingDOFs =
                    child.SWMainComponent.GetRemainingDOFs(
                        out int R1Status, out MathPoint RPoint1, out int R1DirStatus, out MathVector RDir1,
                        out int R2Status, out MathPoint RPoint2, out int R2DirStatus, out MathVector RDir2,
                        out int L1Status, out MathVector LDir1,
                        out int L2Status, out MathVector LDir2);
                if (RPoint1 != null)
                {
                    logger.Info("R1: " + R1Status + ", " + RPoint1 + ", " + R1DirStatus + ", " + RDir1.ArrayData);
                }
                else
                {
                    logger.Info("R1: " + R1Status + ", " + R1DirStatus);
                }

                if (RPoint2 != null)
                {
                    logger.Info("R2: " + R2Status + ", " + RPoint2 + ", " + R2DirStatus + ", " + RDir2.ArrayData);
                }
                else
                {
                    logger.Info("R2: " + R2Status + ", " + R2DirStatus);
                }
                if (LDir1 != null)
                {
                    logger.Info("L1: " + L1Status + ", " + LDir1.ArrayData);
                }
                else
                {
                    logger.Info("L1: " + L1Status);
                }
                if (LDir2 != null)
                {
                    logger.Info("L2: " + ", " + LDir2.ArrayData);
                }
                else
                {
                    logger.Info("L2: " + L2Status);
                }

                degreesOfFreedom = remainingDOFs;

                // Convert the gotten degrees of freedom to a joint type, origin and axis
                child.Joint.Type = "fixed";
                child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));

                if (degreesOfFreedom == 0 && (R1Status + L1Status > 0))
                {
                    success = true;
                    if (R1Status == 1)
                    {
                        child.Joint.Type = "continuous";
                        child.Joint.Axis.SetXYZ(RDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(RPoint1.ArrayData);
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                        MoveOrigin(parent, child);
                    }
                    else if (L1Status == 1)
                    {
                        child.Joint.Type = "prismatic";
                        child.Joint.Axis.SetXYZ(LDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(MathOps.GetXYZ(child.SWMainComponent.Transform2));
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(child.SWMainComponent.Transform2));
                        MoveOrigin(parent, child);
                    }
                }
                child.Joint.Origin.SetXYZ(MathOps.Threshold(child.Joint.Origin.GetXYZ(), 0.00001));
                child.Joint.Origin.SetRPY(MathOps.Threshold(child.Joint.Origin.GetRPY(), 0.00001));
                UnsuppressLimitMates(limitMates);
                if (limitMates.Count > 0 && ComputeJointLimits)
                {
                    AddLimits(child.Joint, limitMates, parent.SWMainComponent, child.SWMainComponent);
                }
            }

            UnFixComponents(fixedComponents);
            return success;
        }

        //This now needs to be able to get the component, and it's associated coordinate system name.
        //Then it needs to transform to the top level assembly (sounds like fun).
        private void EstimateGlobalJointFromRefGeometry(Link child)
        {
            MathTransform GlobalCoordsysTransform =
                GetCoordinateSystemTransform(child.Joint.CoordinateSystemName);
            if (GlobalCoordsysTransform == null)
            {
                logger.Warn(
                    string.Format("Joint transform for coordinate system {0} could not be computed for joint {1}", 
                        child.Joint.CoordinateSystemName, child.Joint.Name));
                return;
            }
            child.Joint.Origin.SetXYZ(MathOps.GetXYZ(GlobalCoordsysTransform));
            child.Joint.Origin.SetRPY(MathOps.GetRPY(GlobalCoordsysTransform));
            if (child.Joint.Type != "fixed")
            {
                EstimateAxis(child.Joint);
            }
        }

        // Method to get the SolidWorks MathTransform from a coordinate system. This method can account for
        // coordinate systems that are embedded in subcomponents, and apply the correct transformation to return
        // it to a global transform. It assumes that the coordinate system name is formatted like:
        // "Coordinate System 1 <assy/subassy/comp>" where the full Component2.Name2 is between the <>
        private MathTransform GetCoordinateSystemTransform(string CoordinateSystemName)
        {
            ModelDoc2 ComponentModel = ActiveSWModel;
            MathTransform ComponentTransform = default;
            if (CoordinateSystemName == null)
            {
                throw new Exception("Coordinate system string is null");
            }
            if (CoordinateSystemName.Contains("<") && CoordinateSystemName.Contains(">"))
            {
                string componentStr = "";
                int indexFirst = CoordinateSystemName.IndexOf('<');
                int indexLast = CoordinateSystemName.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    componentStr =
                        CoordinateSystemName.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string CoordinateSystemNameUnTrimmed = CoordinateSystemName.Substring(0, indexFirst);
                    CoordinateSystemName = CoordinateSystemNameUnTrimmed.Trim();
                }
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                    }
                }
            }
            MathTransform LocalCoordsysTransform =
                ComponentModel.Extension.GetCoordinateSystemTransformByName(CoordinateSystemName);
            MathTransform GlobalCoordsysTransform = (ComponentTransform == null) ?
                LocalCoordsysTransform : LocalCoordsysTransform.Multiply(ComponentTransform);
            return GlobalCoordsysTransform;
        }

        private void MoveOrigin(Link parent, Link nonLocalizedChild)
        {
            double xMax = Double.MinValue;
            double yMax = Double.MinValue;
            double zMax = Double.MinValue;
            double xMin = Double.MaxValue;
            double yMin = Double.MaxValue;
            double zMin = Double.MaxValue;
            double[] points;

            foreach (Component2 comp in nonLocalizedChild.SWComponents)
            {
                // Returns box as [ XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2 ]
                points = comp.GetBox(false, false);
                xMax = MathOps.Max(points[0], points[3], xMax);
                yMax = MathOps.Max(points[1], points[4], yMax);
                zMax = MathOps.Max(points[2], points[5], zMax);
                xMin = MathOps.Min(points[0], points[3], xMin);
                yMin = MathOps.Min(points[1], points[4], yMin);
                zMin = MathOps.Min(points[2], points[5], zMin);
            }
            string coordsys = parent.Joint.CoordinateSystemName;
            MathTransform parentTransform = GetCoordinateSystemTransform(coordsys);

            double[] xyzParent = MathOps.GetXYZ(parentTransform);
            double[] xyzJointAxis = nonLocalizedChild.Joint.Axis.GetXYZ();
            double[] xyzOrigin = nonLocalizedChild.Joint.Origin.GetXYZ();
            double[] idealOrigin =
                MathOps.ClosestPointOnLineToPoint(xyzParent, xyzJointAxis, xyzOrigin);

            nonLocalizedChild.Joint.Origin.SetXYZ(
                MathOps.ClosestPointOnLineWithinBox(xMin, xMax, yMin, yMax, zMin, zMax,
                    nonLocalizedChild.Joint.Axis.GetXYZ(), idealOrigin));
        }

        // Calculates the axis from a Reference Axis in the model
        private void EstimateAxis(Joint Joint)
        {
            Joint.Axis.SetXYZ(EstimateAxis(Joint.AxisName));
        }

        //This doesn't seem to get the right values for the estimatedAxis. Check the actual values
        public double[] EstimateAxis(string axisName)
        {
            //Select the axis
            ActiveSWModel.ClearSelection2(true);

            return GetRefAxis(axisName);
        }

        private double[] GetRefAxis(string axisStr)
        {
            ModelDoc2 ComponentModel = ActiveSWModel;
            string axisName = axisStr;
            MathTransform ComponentTransform = default;

            if (axisStr.Contains("<") && axisStr.Contains(">"))
            {
                string componentStr = "";
                int indexFirst = axisStr.IndexOf('<');
                int indexLast = axisStr.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    componentStr = axisStr.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string CoordinateSystemNameUnTrimmed = axisStr.Substring(0, indexFirst);
                    axisName = CoordinateSystemNameUnTrimmed.Trim();
                }
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                object[] components = assy.GetComponents(false);
                foreach (Component2 comp in components)
                {
                    if (comp.Name2 == componentStr)
                    {
                        ComponentModel = comp.GetModelDoc2();
                        ComponentTransform = comp.Transform2;
                    }
                }
            }
            //Calculate!
            double[] axisParams;
            double[] axisVector = new double[3];

            bool selected =
                ComponentModel.Extension.SelectByID2(axisName, "AXIS", 0, 0, 0, false, 0, null, 0);
            if (selected)
            {
                Feature feat = ComponentModel.SelectionManager.GetSelectedObject6(1, 0);
                RefAxis axis = (RefAxis)feat.GetSpecificFeature2();

                // GetRefAxisParams returns {startX, startY, startZ, endX, endY, endZ}
                axisParams = axis.GetRefAxisParams();
                axisVector[0] = axisParams[0] - axisParams[3];
                axisVector[1] = axisParams[1] - axisParams[4];
                axisVector[2] = axisParams[2] - axisParams[5];

                // Normalize and cleanup
                axisVector = MathOps.PNorm(axisVector, 2);

                // Transform to proper coordinates
                axisVector = GlobalAxis(axisVector, ComponentTransform);
            }

            return axisVector;
        }

        //This is called whenever the pull down menu is changed and the axis needs to be
        // recalculated in reference to the coordinate system
        public double[] LocalizeAxis(double[] Axis, string coordsys)
        {
            MathTransform coordsysTransform = GetCoordinateSystemTransform(coordsys);
            return LocalizeAxis(Axis, coordsysTransform);
        }

        // This is called by the above method and the getRefAxis method
        private static double[] LocalizeAxis(double[] Axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Vector<double> vec = new DenseVector(new double[] { Axis[0], Axis[1], Axis[2], 0 });
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                vec = transform.Inverse() * vec;
                Axis[0] = vec[0]; Axis[1] = vec[1]; Axis[2] = vec[2];
            }
            return MathOps.Threshold(Axis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, Matrix<double> transform)
        {
            double[] transformedAxis = new double[axis.Length];
            if (transform != null)
            {
                Vector<double> transformedVector = new DenseVector(new double[] { axis[0], axis[1], axis[2], 0 });
                transformedVector = transform * transformedVector;
                transformedAxis[0] = transformedVector[0];
                transformedAxis[1] = transformedVector[1];
                transformedAxis[2] = transformedVector[2];
            }
            return MathOps.Threshold(transformedAxis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                return GlobalAxis(axis, transform);
            }
            return axis;
        }

        // Creates a list of all the features of this type.
        private Dictionary<string, List<Feature>> GetFeaturesOfType(string featureName, bool topLevelOnly)
        {
            Dictionary<string, List<Feature>> features = new Dictionary<string, List<Feature>>();
            GetFeaturesOfType(ActiveSWModel, featureName, topLevelOnly, "", features);
            return features;
        }

        private void GetFeaturesOfType(ModelDoc2 modelDoc, string featureName,
            bool topLevelOnly, string keyName, Dictionary<string, List<Feature>> features)
        {
            string fileName = (string.IsNullOrWhiteSpace(keyName)) ? modelDoc.GetTitle() : keyName;
            logger.Info("Retrieving features of type [" + featureName + "] from " + fileName);

            features[keyName] = new List<Feature>();

            object[] featureObjects = modelDoc.FeatureManager.GetFeatures(false);
            if (featureObjects == null)
            {
                logger.Info("No features found in " + modelDoc.GetTitle());
                return;
            }

            logger.Info("Found " + featureObjects.Length + " in " + fileName);
            foreach (Feature feat in featureObjects)
            {
                if (feat.GetTypeName2() == featureName)
                {
                    features[keyName].Add(feat);
                }
            }

            logger.Info("Found " + features[keyName].Count + " features of type [" + featureName + "] in " + fileName);
            if (!topLevelOnly && modelDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                logger.Info("Proceeding through assembly components");
                AssemblyDoc assyDoc = (AssemblyDoc)modelDoc;

                // Get top level components in an assembly. If the user wants to use a reference
                // coordinate system or axis not located in the top level assembly, then it will
                // need to be in a top level component. This will probably be ok because most
                // users keep their reference geometry in the top level assembly as it is.
                object[] components = assyDoc.GetComponents(true);

                // If there are no components in an assembly, this object will be null.
                if (components != null)
                {
                    logger.Info(components.Length + " components to check");
                    foreach (Component2 comp in components)
                    {
                        ModelDoc2 doc = comp.GetModelDoc2();
                        if (doc != null)
                        {
                            //We already have all the components in an assembly, we don't want
                            // to recur as we go through them. (topLevelOnly = true)
                            GetFeaturesOfType(doc, featureName, true, comp.Name2, features);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> GetComponentRefGeoNames(string StringToParse)
        {
            string RefGeoName = StringToParse;
            string ComponentName = "";
            if (StringToParse.Contains("<") && StringToParse.Contains(">"))
            {
                int indexFirst = StringToParse.IndexOf('<');
                int indexLast = StringToParse.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    ComponentName = StringToParse.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string RefGeoNameUnTrimmed = StringToParse.Substring(0, indexFirst);
                    RefGeoName = RefGeoNameUnTrimmed.Trim();
                }
            }

            Dictionary<string, string> dict = new Dictionary<string, string>
            {
                ["geo"] = RefGeoName,
                ["component"] = ComponentName
            };
            return dict;
        }

        private List<string> FindRefGeoNames(string FeatureName)
        {
            Dictionary<string, List<Feature>> features = GetFeaturesOfType(FeatureName, false);
            List<string> featureNames = new List<string>();
            foreach (string key in features.Keys)
            {
                foreach (Feature feat in features[key])
                {
                    if (String.IsNullOrWhiteSpace(key))
                    {
                        featureNames.Add(feat.Name);
                    }
                    else
                    {
                        featureNames.Add(feat.Name + " <" + key + ">");
                    }
                }
            }
            return featureNames;
        }

        public void UpdateReferenceGeometries()
        {
            List<string> coordinateSystemNames = FindRefGeoNames("CoordSys");
            List<string> axesNames = FindRefGeoNames("RefAxis");

            ReferenceCoordinateSystemNames.Clear();
            ReferenceCoordinateSystemNames.AddRange(coordinateSystemNames);

            ReferenceAxesNames.Clear();
            ReferenceAxesNames.AddRange(axesNames);
        }

        public List<string> GetRefCoordinateSystems()
        {
            return new List<string>(ReferenceCoordinateSystemNames);
        }

        public List<string> GetRefAxes()
        {
            return new List<string>(ReferenceAxesNames);
        }

        //This method adds in the limits from a limit mate, to make a joint a revolute joint.
        // It really needs to checked for correctness.
        private static void AddLimits(Joint Joint, List<Mate2> limitMates,
            Component2 parentComponent, Component2 childComponent)
        {
            logger.Info("Parent SW Component: " + parentComponent.Name2);
            logger.Info("Child SW Component: " + childComponent.Name2);
            // The number of limit Mates should only be one. But for completeness, I cycle through
            // every found limit mate.
            foreach (Mate2 swMate in limitMates)
            {
                logger.Info("Determining limit mate eligibility ");
                List<Component2> entities = new List<Component2>();
                for (int i = 0; i < swMate.GetMateEntityCount(); i++)
                {
                    MateEntity2 entity = swMate.MateEntity(i);
                    
                    // Check if entity.ReferenceComponent is null and skip if so
                    if (entity.ReferenceComponent == null)
                    {
                        logger.Warn("Mate entity has no reference component");
                        continue;
                    }
                    
                    entities.Add(entity.ReferenceComponent);
                    logger.Info("Adding component entity: " + entity.ReferenceComponent.Name2);

                    Component2 parent = entity.ReferenceComponent.GetParent();
                    while (parent != null)
                    {
                        logger.Info("Adding component entity: " + parent.Name2);
                        entities.Add(parent);
                        parent = parent.GetParent();
                    }
                }

                if (entities.Contains(parentComponent) && entities.Contains(childComponent))
                {
                    // [TODO] This assumes the limit mate limits the right degree of freedom,
                    // it really should check that assumption
                    if ((Joint.Type == "continuous" && swMate.Type ==
                            (int)swMateType_e.swMateANGLE) ||
                        (Joint.Type == "prismatic" && swMate.Type ==
                            (int)swMateType_e.swMateDISTANCE))
                    {
                        // Unclear if flipped is the right thing we want to be checking here.
                        // From a sample size of 1, in SolidWorks it appears that an aligned and
                        // anti-aligned mates are NOT flipped...
                        if (!swMate.Flipped)
                        {
                            // Reverse mate directions, for some reason
                            Joint.Limit.Upper = -swMate.MinimumVariation;
                            Joint.Limit.Lower = -swMate.MaximumVariation;
                        }
                        else
                        {
                            // Lucky me that no conversion is necessary
                            Joint.Limit.Upper = swMate.MaximumVariation;
                            Joint.Limit.Lower = swMate.MinimumVariation;
                        }
                        if (Joint.Type == "continuous")
                        {
                            Joint.Type = "revolute";
                        }
                    }
                }
            }
        }

        // Suppresses limit mates to make it easier to find the free degree of freedom in a joint
        private static List<Mate2> SuppressLimitMates(IComponent2 component)
        {
            List<Mate2> limitMates = new List<Mate2>();

            object[] objs = component.GetMates();

            //limit mates aren't always present
            if (objs != null)
            {
                foreach (object obj in objs)
                {
                    if (obj is Mate2 swMate)
                    {
                        if (swMate.MinimumVariation != swMate.MaximumVariation)
                        {
                            limitMates.Add(swMate);
                        }
                    }
                }
            }

            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.Select(false);
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }

            return limitMates;
        }

        // Unsuppresses limit mates that were suppressed before
        private static void UnsuppressLimitMates(List<Mate2> limitMates)
        {
            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }
        }

        //Unfixes components that were fixed to find the free degree of freedom
        private void UnFixComponents(List<Component2> components)
        {
            foreach (Component2 comp in components)
            {
                logger.Info("Unfixing component " + comp.GetID());
            }

            CommonSwOperations.SelectComponents(ActiveSWModel, components, true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            assy.UnfixComponent();
        }

        //Verifies that the reference geometry still exists. This can happen if the reference
        // geometry was deleted but the configuration was kept
        private void CheckRefGeometryExists(Link link)
        {
            if (!CheckRefCoordsysExists(link.Joint.CoordinateSystemName))
            {
                link.Joint.CoordinateSystemName = "Automatically Generate";
            }
            // Fixed URDF joints do not have an axis. Marking a missing fixed-joint axis
            // for automatic generation would unnecessarily invoke the DOF estimator.
            if (JointUsesAxis(link.Joint.Type) && !CheckRefAxisExists(link.Joint.AxisName))
            {
                link.Joint.AxisName = "Automatically Generate";
            }
        }

        internal static bool JointUsesAxis(string jointType)
        {
            return jointType != "fixed";
        }

        private bool CheckRefCoordsysExists(string OriginName)
        {
            return ReferenceCoordinateSystemNames.Contains(OriginName);
        }

        private bool CheckRefAxisExists(string AxisName)
        {
            return ReferenceAxesNames.Contains(AxisName);
        }

        private List<Component2> GetParentAncestorComponents(Link node)
        {
            List<Component2> components = new List<Component2>(node.SWComponents);
            if (node.Parent != null)
            {
                components.AddRange(GetParentAncestorComponents(node.Parent));
            }
            return components;
        }

        //Used to fix components to estimate the degree of freedom.
        private List<Component2> FixComponents(Link parent)
        {
            logger.Info("Fixing components for " + parent.Name);
            List<Component2> componentsToFix = GetParentAncestorComponents(parent);
            List<Component2> componentsToUnfix = new List<Component2>();
            foreach (Component2 comp in componentsToFix)
            {
                logger.Info("Fixing " + comp.GetID());
                if (!comp.IsFixed())
                {
                    componentsToUnfix.Add(comp);
                }
                else
                {
                    logger.Info("Component " + comp.GetID() + " is already fixed");
                }
            }
            CommonSwOperations.SelectComponents(ActiveSWModel, componentsToFix, true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            assy.FixComponent();
            return componentsToUnfix;
        }

        #endregion Joint methods
    }

    public enum MeshExportFormat
    {
        STL,
        THREEDXML,
        OBJ
    }
}