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
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.ROS;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading;
using System.Windows;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    // This class contains a long list of methods that are used throughout the export process.
    // Methods for building links and joints are contained in here.
    // Many of the methods are overloaded, but seek to reduce repeated code as much as possible
    // (i.e. the overloaded methods call eachother).
    // These methods are used by the PartExportForm, the AssemblyExportForm and the PropertyManager Page
    public partial class ExportHelper
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        [XmlIgnore]
        public ISldWorks iSwApp = null;

        [XmlIgnore]
        private bool mBinary;

        private bool mshowInfo;
        private bool mSTLPreview;
        private bool mTranslateToPositive;
        private bool mSaveComponentsIntoOneFile;
        private int mSTLUnits;
        private double mHideTransitionSpeed;

        private UserProgressBar progressBar;

        // File names already used for meshes and textures in the current export. Two links or textures
        // must never end up in the same file (issue 62); the names inside the URDF are not affected.
        private readonly HashSet<string> usedMeshFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> usedTextureFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> textureFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Links whose mesh could not be written in the current export (issue 61).
        private readonly List<string> failedMeshLinks = new List<string>();

        [XmlIgnore]
        public ModelDoc2 ActiveSWModel;

        [XmlIgnore]
        public MathUtility swMath;

        [XmlIgnore]
        public Object SWMathPID
        { get; set; }

        public Robot URDFRobot
        { get; set; }

        public string PackageName
        { get; set; }

        public string SavePath
        { get; set; }

        public readonly List<Link> Links;

        private readonly List<string> ReferenceCoordinateSystemNames;
        private readonly List<string> ReferenceAxesNames;

        private bool ComputeInertialValues;
        private bool ComputeVisualCollision;
        private bool ComputeJointKinematics;
        private bool ComputeJointLimits;

        #endregion class variables

        // Constructor for SW2URDF Exporter class
        public ExportHelper(SldWorks iSldWorksApp)
        {
            ConstructExporter(iSldWorksApp);
            iSwApp.GetUserProgressBar(out progressBar);

            SavePath = System.Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            PackageName = ActiveSWModel.GetTitle();

            ReferenceCoordinateSystemNames = FindRefGeoNames("CoordSys");
            ReferenceAxesNames = FindRefGeoNames("RefAxis");

            ComputeInertialValues = true;
            ComputeVisualCollision = true;
            ComputeJointKinematics = true;
            ComputeJointLimits = true;
        }

        public void SetComputeInertial(bool computeInertial)
        {
            ComputeInertialValues = computeInertial;
        }

        public void SetComputeVisualCollision(bool computeVisual)
        {
            ComputeVisualCollision = computeVisual;
        }

        public void SetComputeJointKinematics(bool computeKinematics)
        {
            ComputeJointKinematics = computeKinematics;
        }

        public void SetComputeJointLimits(bool computeJointLimits)
        {
            ComputeJointLimits = computeJointLimits;
        }

        private void ConstructExporter(SldWorks iSldWorksApp)
        {
            iSwApp = iSldWorksApp;
            ActiveSWModel = (ModelDoc2)iSwApp.ActiveDoc;
            swMath = iSwApp.GetMathUtility();
        }

        #region Export Methods

        // Beginning method for exporting the full package
        public void ExportRobot(bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            //Setting up the progress bar
            logger.Info("Beginning the export process");
            usedMeshFileNames.Clear();
            usedTextureFileNames.Clear();
            textureFileNames.Clear();
            int progressBarBound = CommonSwOperations.GetCount(URDFRobot.BaseLink);
            iSwApp.GetUserProgressBar(out progressBar);
            progressBar.Start(0, progressBarBound, "Creating package directories");

            //Creating package directories
            logger.Info("Creating package directories with name " + PackageName + " and save path " + SavePath);
            URDFPackage package = new URDFPackage(PackageName, SavePath, UiText.FromSolidWorks(iSwApp));
            package.CreateDirectories();
            URDFRobot.Name = package.PackageName;
            string windowsURDFFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".urdf";
            string windowsCSVFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".csv";
            string windowsPackageXMLFileName = package.WindowsPackageDirectory + "package.xml";

            //Create CMakeLists
            logger.Info("Creating CMakeLists.txt at " + package.WindowsCMakeLists);
            package.CreateCMakeLists();

            //Create Config joint names, not sure how this is used...
            logger.Info("Creating joint names config at " + package.WindowsConfigYAML);
            package.CreateConfigYAML(URDFRobot.GetJointNames(false));

            //Creating package.xml file
            logger.Info("Creating package.xml at " + windowsPackageXMLFileName);
            PackageXMLWriter packageXMLWriter = new PackageXMLWriter(windowsPackageXMLFileName);
            PackageXML packageXML = new PackageXML(package.PackageName);
            packageXML.WriteElement(packageXMLWriter);

            //Creating RVIZ launch file
            Rviz rviz = new Rviz(package.PackageName, URDFRobot.Name + ".urdf");
            logger.Info("Creating RVIZ launch file in " + package.WindowsLaunchDirectory);
            rviz.WriteFiles(package.WindowsLaunchDirectory);

            //Creating Gazebo launch file
            Gazebo gazebo = new Gazebo(URDFRobot.Name, package.PackageName, URDFRobot.Name + ".urdf");
            logger.Info("Creating Gazebo launch file in " + package.WindowsLaunchDirectory);

            gazebo.WriteFile(package.WindowsLaunchDirectory);

            //Customizing STL preferences to how I want them
            logger.Info("Saving existing STL preferences");
            SaveUserPreferences();

            logger.Info("Modifying STL preferences");
            SetSTLExportPreferences();

            //Saving part as STL mesh
            AssemblyDoc assyDoc = (AssemblyDoc)ActiveSWModel;
            List<string> hiddenComponents = CommonSwOperations.FindHiddenComponents(assyDoc.GetComponents(false));
            logger.Info("Found " + hiddenComponents.Count + " hidden components " + String.Join(", ", hiddenComponents));
            logger.Info("Hiding all components");
            ActiveSWModel.Extension.SelectAll();
            ActiveSWModel.HideComponent2();

            UiText ui = UiText.FromSolidWorks(iSwApp);
            failedMeshLinks.Clear();
            MeshExportCheck.RemoveFailureMarker(package.WindowsPackageDirectory);
            bool success = false;
            try
            {
                logger.Info("Beginning individual files export");
                ExportFiles(URDFRobot.BaseLink, package, 0, exportSTL, meshFormat);
                success = true;
            }
            catch (Exception e)
            {
                logger.Error("An exception was thrown attempting to export the URDF", e);
            }
            finally
            {
                logger.Info("Showing all components except previously hidden components");
                CommonSwOperations.ShowAllComponents(ActiveSWModel, hiddenComponents);

                logger.Info("Resetting STL preferences");
                ResetUserPreferences();
            }

            if (!success)
            {
                progressBar.End();
                URDFPackage.MessageBox.Show(ui.ProblemMessage(
                    ui.Get("Exporting the URDF failed unexpectedly."), null));
                return;
            }

            if (failedMeshLinks.Count > 0)
            {
                string failed = MeshExportCheck.JoinLinkNames(failedMeshLinks);
                logger.Error("Mesh export failed for: " + failed + ". The URDF was not written.");
                MeshExportCheck.WriteFailureMarker(package.WindowsPackageDirectory, failedMeshLinks);
                progressBar.End();
                URDFPackage.MessageBox.Show(MeshFailureMessage(ui, failed));
                return;
            }

            logger.Info("Writing URDF file to " + windowsURDFFileName);
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            URDFRobot.WriteURDF(uWriter.writer);

            ImportExport.WriteRobotToCSV(URDFRobot, windowsCSVFileName);

            logger.Info("Copying log file");
            CopyLogFile(package);

            logger.Info("Resetting STL preferences");
            ResetUserPreferences();
            progressBar.End();
        }

        public List<string> GetJointNames()
        {
            List<string> jointNames = new List<string>();

            Queue<Link> queue = new Queue<Link>();
            queue.Enqueue(URDFRobot.BaseLink);
            while (queue.Count > 0)
            {
                Link current = queue.Dequeue();
                if (current.Parent != null)
                {
                    jointNames.Add(current.Joint.Name);
                }

                foreach (Link child in current.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return jointNames;
        }

        //Recursive method for exporting each link (and writing it to the URDF)
        private void ExportFiles(Link link, URDFPackage package, int count, bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle("Exporting mesh: " + link.Name);
            logger.Info("Exporting link: " + link.Name);
            // Iterate through each child and export its files
            logger.Info("Link " + link.Name + " has " + link.Children.Count + " children");
            foreach (Link child in link.Children)
            {
                count += 1;
                ExportFiles(child, package, count, exportSTL, meshFormat);
            }

            // A reference frame has no mesh, but its descendants may have geometry.
            if (link.isFixedFrame)
            {
                return;
            }

            // Copy the texture file (if it was specified) to the textures directory
            if (!link.isFixedFrame && !String.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))
            {
                if (File.Exists(link.Visual.Material.Texture.wFilename))
                {
                    string textureSource = link.Visual.Material.Texture.wFilename;
                    string textureName;
                    // The same source file is copied once; different files with the same name get a suffix
                    if (!textureFileNames.TryGetValue(textureSource, out textureName))
                    {
                        textureName = FileNames.UniqueFileNameWithExtension(Path.GetFileName(textureSource), usedTextureFileNames);
                        textureFileNames[textureSource] = textureName;
                        File.Copy(textureSource, package.WindowsTexturesDirectory + textureName, true);
                    }
                    link.Visual.Material.Texture.Filename = package.TexturesDirectory + textureName;
                }
            }

            // Create the mesh filenames. Link names may hold characters that are not allowed in file names
            // (SolidWorks likes to use /), so the file name is derived from the name; the link name itself is kept.
            string linkName = FileNames.UniqueFileName(link.Name, usedMeshFileNames);
            string meshFilename = package.MeshesDirectory + linkName;
            string windowsMeshFileName = package.WindowsMeshesDirectory + linkName;
            switch(meshFormat)
            {
                case MeshExportFormat.STL:
                    meshFilename += ".STL";
                    windowsMeshFileName += ".STL";
                    break;

                case MeshExportFormat.OBJ:
                    meshFilename += ".obj";
                    windowsMeshFileName += ".obj";
                    break;

                case MeshExportFormat.THREEDXML:
                    meshFilename += ".3dxml";
                    windowsMeshFileName += ".3dxml";
                    break;

                default:
                    meshFilename += ".STL";
                    windowsMeshFileName += ".STL";
                    break;
            }
            // Export STL
            if (exportSTL)
            {
                bool exported;
                try
                {
                    switch (meshFormat)
                    {
                        case MeshExportFormat.OBJ:
                            SaveObj(link, windowsMeshFileName);
                            exported = MeshExportCheck.IsUsableMesh(windowsMeshFileName);
                            break;

                        case MeshExportFormat.THREEDXML:
                            exported = Save3dxml(link, windowsMeshFileName);
                            break;

                        default:
                            exported = SaveSTL(link, windowsMeshFileName);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.Error("Exporting the mesh of link " + link.Name + " failed", e);
                    exported = false;
                }
                if (!exported)
                {
                    failedMeshLinks.Add(link.Name);
                }
            }
            link.Visual.Geometry.Mesh.Filename = meshFilename;
            link.Collision.Geometry.Mesh.Filename = meshFilename;
        }

        /// <summary>Cancels an OBJ conversion that is in progress (for example from a progress dialog).</summary>
        public CancellationToken CancellationToken { get; set; }

        private void ConvertObj(string source, string target, string linkName)
        {
            ObjConversionRunner.Run(source, target, linkName, CancellationToken, UiText.FromSolidWorks(iSwApp));
        }

        private void SaveObj(Link link, string target)
        {
            string source = Path.ChangeExtension(target, ".3dxml");
            SaveObjSource(link, source);
            ConvertObj(source, target, link.Name);
        }

        // Native 3DXML export of one link, the input of the OBJ conversion. Unlike STL, the 3DXML
        // keeps the global vertices, so the link frame is applied to the mesh origins instead.
        private void SaveObjSource(Link link, string source)
        {
            string coordsysName = link.Joint.CoordinateSystemName;
            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);

            MathTransform frame = GetCoordinateSystemTransform(coordsysName);
            if (frame == null) throw new InvalidOperationException("Missing OBJ link frame: " + coordsysName);
            Matrix<double> inverse = MathOps.GetTransformation(frame).Inverse();
            foreach (Origin origin in new[] { link.Visual.Origin, link.Collision.Origin })
            {
                Matrix<double> local = MathOps.GetTransformation(origin.GetXYZ(), origin.GetRPY()) * inverse;
                origin.SetXYZ(MathOps.GetXYZ(local));
                origin.SetRPY(MathOps.GetRPY(local));
            }

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);
            try
            {
                SetLinkSpecificSTLPreferences(names["geo"], ActiveSWModel);
                SaveNative3dxml(ActiveSWModel, source);
            }
            finally { CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents); }
        }

        private static void SaveNative3dxml(ModelDoc2 model, string source)
        {
            int errors = 0;
            int warnings = 0;
            if (File.Exists(source)) File.Delete(source);
            bool saved = model.Extension.SaveAs(source, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent | (int)swSaveAsOptions_e.swSaveAsOptions_Copy,
                null, ref errors, ref warnings);
            if (!saved || errors != 0 || !File.Exists(source))
                throw new IOException("Native 3DXML export failed (" + errors + "): " + source);
        }

        private bool Save3dxml(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting 3dxml with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], ActiveDoc);

            logger.Info("Saving 3dxml to " + windowsMeshFilename);

            // === 3dxml Localize Link === //

            // Remove suffix from coordinate-system name.
            // ex. "Joint Origin <Arm_link-1>" -> "Joint Origin"
            // Suffix is included when coordinate is inside sub-assembly.
            string linkModelName = names["component"];
            string linkModelSuffix = " <" + linkModelName + ">";
            if(coordsysName.Contains(linkModelSuffix))
            {
                coordsysName = coordsysName.Replace(linkModelSuffix, "");
                logger.Info($"Suffix of {linkModelName} was removed from coordsysName : {coordsysName}");
            }

            // Get the model document of the link.
            ModelDoc2 linkModel;
            bool isBaseLink = linkModelName == "";
            if (isBaseLink)
            {
                linkModel = ActiveDoc;
            }
            else
            {
                if (link.SWMainComponent != null)
                {
                    linkModel = link.SWMainComponent.GetModelDoc2();
                }
                else
                {
                    logger.Warn("Could not get linkModel because SWMainComponent was null");
                    linkModel = null;
                }
            }

            // Localize the link to the certain place.
            if (linkModel != null)
            {
                MathTransform coordSysTransform =
                    linkModel.Extension.GetCoordinateSystemTransformByName(coordsysName);
                if (coordSysTransform != null)
                {
                    logger.Info("Localizing Link : " + coordsysName);
                    Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);
                    // Assembly inertial properties are already expressed in the link frame.
                    // Only the 3dxml geometry needs this placement correction.
                    LocalizeMeshOrigins(link, GlobalTransform);
                }
                else
                {
                    logger.Warn("coordSysTransform was null : " + coordsysName);
                }
            }
            else
            { 
                logger.Warn("Link model was null.");
            }
            // === 3dxml Localize Link === //

            bool saved = ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);

            if (errors + warnings != 0)
            {
                logger.Warn("Exporting 3dxml for link " + link.Name + " failed with error " + errors +
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);
            return MeshSaveSucceeded(link, saved, errors, windowsMeshFilename);
        }

        private static string MeshFailureMessage(UiText ui, string failedLinks)
        {
            string text = ui.Get("The mesh of these links could not be exported: {0}. The URDF was not written and the package is incomplete (see {1}). Check the log and the output folder, then export again.");
            return string.Format(text, failedLinks, MeshExportCheck.FailureMarkerFileName) + "\n" +
                string.Format(ui.Get("Email your maintainer with the log file found at {0}"), Logger.GetFileName());
        }

        // A mesh counts as exported only when SolidWorks says so, reports no error and the file exists.
        private static bool MeshSaveSucceeded(Link link, bool saved, int errors, string windowsMeshFilename)
        {
            if (saved && errors == 0 && MeshExportCheck.IsUsableMesh(windowsMeshFilename)) return true;
            logger.Error("Exporting the mesh for link " + link.Name + " failed (saved=" + saved + ", error " +
                errors + "): " + windowsMeshFilename);
            return false;
        }

        private bool SaveSTL(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting STL with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            // Choose the coordinate system SOLIDWORKS exports the STL relative to. The export
            // preference only resolves names that exist at the top level of the assembly, so a
            // coordinate system defined inside a sub-assembly cannot be used directly and the
            // mesh falls back to the global frame (issues #87 / #116). When the selection is
            // nested, materialize a top-level reference coordinate system at the same global
            // pose -- the transform the joint origin already uses, which is depth-independent --
            // and export relative to that, so the mesh stays baked at the link origin.
            string exportCoordsysName = names["geo"];
            if (!string.IsNullOrEmpty(names["component"]))
            {
                string nestedExportCoordsys = CreateGlobalMeshCoordinateSystem(link, coordsysName);
                if (!string.IsNullOrEmpty(nestedExportCoordsys))
                {
                    exportCoordsysName = nestedExportCoordsys;
                }
            }

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

            bool success;
            bool saved = false;
            try
            {
                int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                    (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
                SetLinkSpecificSTLPreferences(exportCoordsysName, ActiveDoc);

                logger.Info("Saving STL to " + windowsMeshFilename);
                saved = ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null,
                    ref errors, ref warnings);
                if (errors + warnings != 0)
                {
                    logger.Warn("Exporting STL for link " + link.Name + " failed with error " + errors +
                        " or warnings " + warnings);
                }

                // SaveAs may return before SolidWorks releases the output file. Keep the
                // source geometry visible until the STL can be opened and validated.
                success = CorrectSTLMesh(windowsMeshFilename);
            }
            finally
            {
                CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);
            }

            if (!MeshSaveSucceeded(link, saved, errors, windowsMeshFilename)) return false;
            if (!success)
            {
                logger.Warn("There was an issue exporting the STL for " + link.Name + ". It " +
                    "may not be readable by CAD programs that aren't SolidWorks");
            }
            return success;
        }

        public void ExportLink(bool zIsUp)
        {
            ExportLink(zIsUp, MeshExportFormat.STL);
        }

        public void ExportLink(bool zIsUp, MeshExportFormat meshFormat)
        {
            CreateBaseRefOrigin(zIsUp);
            MathTransform coordSysTransform =
                ActiveSWModel.Extension.GetCoordinateSystemTransformByName("Origin_global");
            Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);

            LocalizeLink(URDFRobot.BaseLink, GlobalTransform);

            //Creating package directories
            URDFPackage package = new URDFPackage(PackageName, SavePath, UiText.FromSolidWorks(iSwApp));
            package.CreateDirectories();
            string meshBaseName = FileNames.SanitizeFileName(URDFRobot.BaseLink.Name);
            string meshFileName = package.MeshesDirectory + meshBaseName + ".STL";
            string windowsMeshFileName = package.WindowsMeshesDirectory + meshBaseName + ".STL";
            string windowsURDFFileName = package.WindowsRobotsDirectory +
                FileNames.SanitizeFileName(URDFRobot.Name, "robot") + ".urdf";
            string windowsManifestFileName = package.WindowsPackageDirectory + "manifest.xml";

            //Creating manifest file
            PackageXMLWriter manifestWriter = new PackageXMLWriter(windowsManifestFileName);
            PackageXML Manifest = new PackageXML(package.PackageName);
            Manifest.WriteElement(manifestWriter);

            //Customizing STL preferences to how I want them
            SaveUserPreferences();
            SetSTLExportPreferences();
            SetLinkSpecificSTLPreferences("", ActiveSWModel);
            int errors = 0;
            int warnings = 0;
            bool meshExported = true;

            try
            {
                if (meshFormat == MeshExportFormat.OBJ)
                {
                    meshFileName = Path.ChangeExtension(meshFileName, ".obj");
                    windowsMeshFileName = Path.ChangeExtension(windowsMeshFileName, ".obj");
                    string source = Path.ChangeExtension(windowsMeshFileName, ".3dxml");
                    SaveNative3dxml(ActiveSWModel, source);
                    ConvertObj(source, windowsMeshFileName, URDFRobot.BaseLink.Name);
                }
                else
                {
                    //Saving part as STL mesh
                    bool saved = ActiveSWModel.Extension.SaveAs(windowsMeshFileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                    meshExported = MeshSaveSucceeded(URDFRobot.BaseLink, saved, errors, windowsMeshFileName);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                logger.Error("Exporting the mesh of link " + URDFRobot.BaseLink.Name + " failed", e);
                meshExported = false;
            }
            finally { ResetUserPreferences(); }

            if (!meshExported)
            {
                UiText ui = UiText.FromSolidWorks(iSwApp);
                MeshExportCheck.WriteFailureMarker(package.WindowsPackageDirectory, new[] { URDFRobot.BaseLink.Name });
                URDFPackage.MessageBox.Show(MeshFailureMessage(ui, URDFRobot.BaseLink.Name));
                return;
            }
            MeshExportCheck.RemoveFailureMarker(package.WindowsPackageDirectory);

            URDFRobot.BaseLink.Visual.Geometry.Mesh.Filename = meshFileName;
            URDFRobot.BaseLink.Collision.Geometry.Mesh.Filename = meshFileName;

            URDFRobot.BaseLink.Visual.Material.Texture.Filename =
                package.TexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            string textureSavePath =
                package.WindowsTexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            if (!String.IsNullOrWhiteSpace(URDFRobot.BaseLink.Visual.Material.Texture.wFilename))
            {
                File.Copy(URDFRobot.BaseLink.Visual.Material.Texture.wFilename, textureSavePath, true);
            }

            //Writing URDF to file
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            //mRobot.addLink(mLink);
            URDFRobot.WriteURDF(uWriter.writer);
        }

        // Waits for SolidWorks to release a binary STL, verifies that its triangle data is
        // complete, and then clears the SolidWorks-specific header.
        public static bool CorrectSTLMesh(string filename)
        {
            const int maxAttempts = 150;
            const int retryDelayMilliseconds = 200;
            Exception lastError = null;

            logger.Info("Waiting for a complete STL file before removing the SW header");
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using (FileStream fileStream = new FileStream(
                        filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (!TryGetBinarySTLTriangleCount(fileStream, out uint triangleCount,
                            out string validationError))
                        {
                            logger.Warn("Correcting the STL " + filename + " failed: " +
                                validationError);
                            return false;
                        }

                        fileStream.Position = 0;
                        fileStream.Write(new byte[80], 0, 80);
                        fileStream.Flush(true);
                        logger.Info("Validated STL with " + triangleCount +
                            " triangles and removed SW header");
                        return true;
                    }
                }
                catch (IOException e)
                {
                    lastError = e;
                }
                catch (UnauthorizedAccessException e)
                {
                    lastError = e;
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
            }

            logger.Warn("Correcting the STL " + filename + " failed after waiting " +
                (maxAttempts * retryDelayMilliseconds / 1000) +
                " seconds for SolidWorks to release it", lastError);
            return false;
        }

        private static bool TryGetBinarySTLTriangleCount(FileStream fileStream,
            out uint triangleCount, out string validationError)
        {
            triangleCount = 0;
            validationError = null;

            if (fileStream.Length < 84)
            {
                validationError = "the binary STL is shorter than its 84-byte header";
                return false;
            }

            fileStream.Position = 80;
            byte[] triangleCountBytes = new byte[4];
            if (fileStream.Read(triangleCountBytes, 0, triangleCountBytes.Length) !=
                triangleCountBytes.Length)
            {
                validationError = "the STL triangle count is incomplete";
                return false;
            }

            triangleCount = BitConverter.ToUInt32(triangleCountBytes, 0);
            if (triangleCount == 0)
            {
                validationError = "the STL contains no triangles";
                return false;
            }

            long expectedLength = 84L + (50L * triangleCount);
            if (fileStream.Length < expectedLength)
            {
                validationError = "the STL triangle data is incomplete; expected at least " +
                    expectedLength + " bytes but found " + fileStream.Length;
                return false;
            }

            return true;
        }

        #endregion Export Methods

        private static void CopyLogFile(URDFPackage package)
        {
            string destination = package.WindowsPackageDirectory + "export.log";
            string log_filename = Logger.GetFileName();

            if (log_filename != null)
            {
                if (!File.Exists(log_filename))
                {
                    System.Windows.Forms.MessageBox.Show("The log file was expected to be located at " + log_filename +
                        ", but it was not found. Please contact your maintainer with this error message.");
                }
                else
                {
                    logger.Info("Copying " + log_filename + " to " + destination);
                    File.Copy(log_filename, destination, true);
                }
            }
        }

        #region STL Preference shuffling

        //Saves the preferences that the user had setup so that I can change them and revert back to their configuration
        private void SaveUserPreferences()
        {
            logger.Info("Saving users preferences");
            mBinary = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            mTranslateToPositive = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive);
            mSTLUnits = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits);
            mshowInfo = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave);
            mSTLPreview = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview);
            mHideTransitionSpeed = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent);
            mSaveComponentsIntoOneFile = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile);
        }

        //This is how the STL export preferences need to be to properly export
        private void SetSTLExportPreferences()
        {
            logger.Info("Setting STL preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, true);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, 2);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, false);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, 0);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, true);
        }

        //This resets the user preferences back to what they were.
        private void ResetUserPreferences()
        {
            logger.Info("Returning STL preferences to user preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, mBinary);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, mTranslateToPositive);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, mSTLUnits);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, mshowInfo);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, mSTLPreview);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, mHideTransitionSpeed);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, mSaveComponentsIntoOneFile);
        }

        //If the user selected something specific for a particular link, that is handled here.
        private void SetLinkSpecificSTLPreferences(string CoordinateSystemName, ModelDoc2 doc)
        {
            doc.Extension.SetUserPreferenceString((int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified, CoordinateSystemName);
        }

        #endregion STL Preference shuffling
    }
}
