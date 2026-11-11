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

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    public partial class AssemblyExportForm : Form
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        public ExportHelper Exporter;
        private readonly UiText ui;
        public bool AutoUpdatingForm;
        public AttributeDef saveConfigurationAttributeDef;

        private readonly SldWorks swApp;
        private readonly ModelDoc2 ActiveSWModel;
        private LinkNode previouslySelectedNode;
        private readonly Control[] jointBoxes;
        private readonly Control[] linkBoxes;
        private readonly LinkNode BaseNode;

        private void AlignFieldLabels()
        {
            label63.Left = textBoxJointName.Left;
            label62.Left = comboBoxJointType.Left;
            UiText.AlignLabel(label14, textBoxIxx);
            UiText.AlignLabel(label11, textBoxIxz);
            UiText.AlignLabel(label13, textBoxInertialOriginX);
            UiText.AlignLabel(label46, textBoxInertialOriginYaw);
            UiText.AlignLabel(label49, textBoxIyy);
            UiText.AlignLabel(label45, textBoxInertialOriginPitch);
            UiText.AlignLabel(label16, textBoxInertialOriginRoll);
            UiText.AlignLabel(label17, textBoxInertialOriginY);
            UiText.AlignLabel(label48, textBoxIyz);
            UiText.AlignLabel(label47, textBoxInertialOriginZ);
            UiText.AlignLabel(label50, textBoxIxy);
            UiText.AlignLabel(label18, textBoxIzz);
            UiText.AlignLabel(label12, textBoxMass);
            UiText.AlignLabel(label24, textBoxVisualOriginZ);
            UiText.AlignLabel(label20, textBoxVisualOriginYaw);
            UiText.AlignLabel(label21, textBoxVisualOriginPitch);
            UiText.AlignLabel(label22, textBoxVisualOriginRoll);
            UiText.AlignLabel(label33, domainUpDownAlpha);
            UiText.AlignLabel(label30, domainUpDownRed);
            UiText.AlignLabel(label31, domainUpDownGreen);
            UiText.AlignLabel(label26, textBoxVisualOriginX);
            UiText.AlignLabel(label32, domainUpDownBlue);
            UiText.AlignLabel(label25, textBoxVisualOriginY);
            UiText.AlignLabel(labelKVelocity, textBoxKVelocity);
            UiText.AlignLabel(labelSoftLower, textBoxSoftLower);
            UiText.AlignLabel(labelSoftUpper, textBoxSoftUpper);
            UiText.AlignLabel(labelKPosition, textBoxKPosition);
            UiText.AlignLabel(labelFriction, textBoxFriction);
            UiText.AlignLabel(labelDamping, textBoxDamping);
            UiText.AlignLabel(label7CalibrationRising, textBoxCalibrationRising);
            UiText.AlignLabel(label73, textBoxCalibrationFalling);
            UiText.AlignLabel(labelVelocity, textBoxLimitVelocity);
            UiText.AlignLabel(labelLowerLimit, textBoxLimitLower);
            UiText.AlignLabel(labelLimitUpper, textBoxLimitUpper);
            UiText.AlignLabel(labelEffort, textBoxLimitEffort);
            UiText.AlignLabel(label58, textBoxAxisX);
            UiText.AlignLabel(label59, textBoxAxisY);
            UiText.AlignLabel(label61, textBoxAxisZ);
            UiText.AlignLabel(label52, textBoxJointX);
            UiText.AlignLabel(label56, textBoxJointYaw);
            UiText.AlignLabel(label55, textBoxJointPitch);
            UiText.AlignLabel(label51, textBoxJointRoll);
            UiText.AlignLabel(label53, textBoxJointY);
            UiText.AlignLabel(label57, textBoxJointZ);
            UiText.AlignLabel(label67, comboBoxAxis);
            UiText.AlignLabel(label66, comboBoxOrigin);
            UiText.AlignLabel(MimicMultiplierLabel, textBoxMimicMultiplier);
            UiText.AlignLabel(MimicOffsetLabel, textBoxMimicOffset);
        }

        public AssemblyExportForm(SldWorks SwApp, LinkNode node, ExportHelper exporter)
        {
            Application.ThreadException +=
                new ThreadExceptionEventHandler(ExceptionHandler);
            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(UnhandledException);
            InitializeComponent();
            ui = UiText.FromSolidWorks(SwApp);
            ui.Apply(this);
            // Items show the localized caption; the stored/compared value is mapped back by ui.ComboValue.
            comboBoxJointType.Items.Clear();
            foreach (string type in SW2URDF.URDF.Joint.AvailableTypes)
                comboBoxJointType.Items.Add(ui.ComboDisplay(type));
            // The designer width fits the English identifiers; the longer captions must not be clipped.
            int widest = 0;
            foreach (object item in comboBoxJointType.Items)
                widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), comboBoxJointType.Font).Width);
            int needed = widest + SystemInformation.VerticalScrollBarWidth + 10;
            comboBoxJointType.DropDownWidth = Math.Max(comboBoxJointType.Width, needed);
            if (needed > comboBoxJointType.Width)
            {
                int room = comboBoxJointType.Parent.ClientSize.Width - comboBoxJointType.Left - 8;
                comboBoxJointType.Width = Math.Min(needed, room);
            }
            AlignFieldLabels();
            UiLayout.FitForm(this, UiLayout.ScreenDpi());
            swApp = SwApp;
            BaseNode = node;
            ActiveSWModel = swApp.ActiveDoc;
            RefreshConfigurationStatus();
            Exporter = exporter;
            AutoUpdatingForm = false;

            jointBoxes = new Control[] {
                textBoxJointName, comboBoxAxis, comboBoxJointType,
                textBoxAxisX, textBoxAxisY, textBoxAxisZ,
                textBoxJointX, textBoxJointY, textBoxJointZ,
                textBoxJointPitch, textBoxJointRoll, textBoxJointYaw,
                textBoxLimitLower, textBoxLimitUpper, textBoxLimitEffort, textBoxLimitVelocity,
                textBoxDamping, textBoxFriction,
                textBoxCalibrationFalling, textBoxCalibrationRising,
                textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity
            };
            linkBoxes = new Control[] {
                textBoxInertialOriginX, textBoxInertialOriginY, textBoxInertialOriginZ,
                textBoxInertialOriginRoll, textBoxInertialOriginPitch, textBoxInertialOriginYaw,
                textBoxVisualOriginX, textBoxVisualOriginY, textBoxVisualOriginZ,
                textBoxVisualOriginRoll, textBoxVisualOriginPitch, textBoxVisualOriginYaw,
                textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy, textBoxIyz, textBoxIzz,
                textBoxMass,
                domainUpDownRed, domainUpDownGreen, domainUpDownBlue, domainUpDownAlpha,
                comboBoxMaterials,
                textBoxTexture
            };

            List<TextBox> numericTextBoxes = new List<TextBox>() {
                textBoxAxisX, textBoxAxisY, textBoxAxisZ,
                textBoxJointX, textBoxJointY, textBoxJointZ,
                textBoxJointPitch, textBoxJointRoll, textBoxJointYaw,
                textBoxLimitLower, textBoxLimitUpper, textBoxLimitEffort, textBoxLimitVelocity,
                textBoxDamping, textBoxFriction,
                textBoxCalibrationFalling, textBoxCalibrationRising,
                textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity,
                textBoxInertialOriginX, textBoxInertialOriginY, textBoxInertialOriginZ,
                textBoxInertialOriginRoll, textBoxInertialOriginPitch, textBoxInertialOriginYaw,
                textBoxVisualOriginX, textBoxVisualOriginY, textBoxVisualOriginZ,
                textBoxVisualOriginRoll, textBoxVisualOriginPitch, textBoxVisualOriginYaw,
                textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy, textBoxIyz, textBoxIzz,
                textBoxMass,
                textBoxMimicMultiplier, textBoxMimicOffset,
            };

            foreach (TextBox textBox in numericTextBoxes)
            {
                textBox.KeyPress += NumericalTextBoxKeyPress;
            }

            saveConfigurationAttributeDef = SwApp.DefineAttribute(ConfigurationSerialization.UrdfConfigurationSwAttributeName);
            int Options = 0;

            saveConfigurationAttributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble, 1.0, Options);
            saveConfigurationAttributeDef.Register();
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Error("Exception encountered in Assembly export form", e.Exception);
            MessageBox.Show("There was a problem with the export form: \n\"" +
                e.Exception.Message + "\"\nEmail your maintainer with the log file found at " +
                Logger.GetFileName());
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            logger.Error("Unhandled exception in Assembly Export form", ex);
            MessageBox.Show("There was a problem with the export form: \n\"" +
                ex.Message + "\"\nEmail your maintainer with the log file found at " +
                Logger.GetFileName());
        }

        //Joint form configuration controls
        // The window DPI is only known once the handle exists, and changes when the form is moved to a
        // monitor with another scale: fit the buttons again for the DPI the form is really drawn at.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UiLayout.FitButtonHeightsToWindow(this);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == UiLayout.WM_DPICHANGED)
            {
                UiLayout.FitButtonHeightsToWindow(this);
            }
        }

        private void AssemblyExportFormLoad(object sender, EventArgs e)
        {
            Exporter.UpdateReferenceGeometries();
            FillJointTree();
        }

        private void RadioButtonObjCheckedChanged(object sender, EventArgs e)
        {
            UpdateObjColorControls();
        }

        private void UpdateObjColorControls()
        {
            bool stl = !radioButtonObj.Checked;

            // OBJ writes no URDF <material>; color and texture come from the MTL file.
            Control[] colorControls = {
                label28, label29, label30, label31, label32, label33, label34,
                comboBoxMaterials, domainUpDownRed, domainUpDownGreen, domainUpDownBlue,
                domainUpDownAlpha, textBoxTexture, buttonTextureBrowse };
            foreach (Control control in colorControls)
            {
                control.Enabled = stl;
            }
            panelObjColorNote.Visible = !stl;
        }

        private void ButtonJointNextClick(object sender, EventArgs e)
        {
            if (!(previouslySelectedNode == null || previouslySelectedNode.Link.Joint == null))
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            previouslySelectedNode = null; // Need to clear this for the link properties page

            string errors = CheckJointsForErrors();
            if (!string.IsNullOrWhiteSpace(errors))
            {
                string message = "The following joints are missing required fields, please " +
                    "address them before continuing\r\n\r\n" + errors;
                MessageBox.Show(message, ui.Get("URDF Joint Errors"));
                return;
            }

            while (treeViewJointTree.Nodes.Count > 0)
            {
                LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
                treeViewJointTree.Nodes.Remove(node);
                BaseNode.Nodes.Add(node);
            }
            ChangeAllNodeFont(BaseNode, new Font(treeViewJointTree.Font, FontStyle.Regular));

            FillLinkTree();
            panelLinkProperties.Visible = true;
            Focus();
        }

        private string CheckJointsForErrors()
        {
            StringBuilder builder = new StringBuilder();
            foreach (LinkNode child in treeViewJointTree.Nodes)
            {
                CheckJointsForErrors(child, builder);
            }
            return builder.ToString();
        }

        private StringBuilder CheckJointsForErrors(LinkNode node, StringBuilder builder)
        {
            if (!node.Link.Joint.AreRequiredFieldsSatisfied())
            {
                builder.Append(node.Link.Joint.Name).Append("\r\n");
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckJointsForErrors(child, builder);
            }
            return builder;
        }

        private void Button_Joint_Cancel_Click(object sender, EventArgs e)
        {
            if (previouslySelectedNode != null)
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            while (treeViewJointTree.Nodes.Count > 0)
            {
                LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
                treeViewJointTree.Nodes.Remove(node);
                BaseNode.Nodes.Add(node);
            }
            SaveConfigTree(ActiveSWModel, BaseNode, true);
            Close();
        }

        private void ButtonLinksCancelClick(object sender, EventArgs e)
        {
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
            }
            SaveConfigTree(ActiveSWModel, BaseNode, true);
            Close();
        }

        private void ButtonLinksPreviousClick(object sender, EventArgs e)
        {
            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }
            previouslySelectedNode = null;
            ChangeAllNodeFont(BaseNode, new Font(treeViewJointTree.Font, FontStyle.Regular));
            FillJointTree();
            panelLinkProperties.Visible = false;
        }

        private void ButtonLinksFinishClick(object sender, EventArgs e)
        {
            FinishExport(true);
        }

        private void ButtonLinksExportUrdfOnlyClick(object sender, EventArgs e)
        {
            FinishExport(false);
        }

        private void FinishExport(bool exportSTL)
        {
            logger.Info("Completing URDF export");
            SaveConfigTree(ActiveSWModel, BaseNode, false);

            // Saving selected node
            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }

            Exporter.URDFRobot = CreateRobotFromTreeView(treeViewLinkProperties);

            // The UI should prevent these sorts of errors, but just in case
            string errors = CheckLinksForErrors(Exporter.URDFRobot.BaseLink);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.Info("Link errors encountered:\n " + errors);

                string message = "The following links contained errors in either their link or joint " +
                    "properties. Please address before continuing\r\n\r\n" + errors;
                MessageBox.Show(message, ui.Get("URDF Errors"));
                return;
            }

            string warnings = CheckLinksForWarnings(Exporter.URDFRobot.BaseLink);

            if (!string.IsNullOrWhiteSpace(warnings))
            {
                logger.Info("Link warnings encountered:\r\n" + warnings);

                string message = "The following links contained issues that may cause problems. " +
                "Do you wish to proceed?\r\n\r\n" + warnings;
                DialogResult result =
                    MessageBox.Show(message, ui.Get("URDF Warnings"), MessageBoxButtons.YesNo);

                if (result == DialogResult.No)
                {
                    logger.Info("Export canceled for user to review warnings");
                    return;
                }
            }

            SaveFileDialog saveFileDialog1 = new SaveFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Exporter.SavePath,
                FileName = Exporter.PackageName
            };

            bool saveResult = DialogResult.OK == saveFileDialog1.ShowDialog();
            saveFileDialog1.Dispose();
            if (saveResult)
            {
                Exporter.SavePath = Path.GetDirectoryName(saveFileDialog1.FileName);
                Exporter.PackageName = Path.GetFileName(saveFileDialog1.FileName);

                logger.Info("Saving URDF package to " + saveFileDialog1.FileName);

                MeshExportFormat meshFormat;
                if(radioButtonStl.Checked)
                {
                    meshFormat = MeshExportFormat.STL;
                }
                else if(radioButtonObj.Checked)
                {
                    meshFormat = MeshExportFormat.OBJ;
                }
                else if(radioButton3dxml.Checked)
                {
                    meshFormat = MeshExportFormat.THREEDXML;
                }
                else
                {
                    meshFormat = MeshExportFormat.STL;
                }
                Exporter.ExportRobot(exportSTL, meshFormat);

                Close();
            }
        }

        private string CheckLinksForErrors(Link baseLink)
        {
            StringBuilder builder = new StringBuilder();
            CheckLinkForErrors(baseLink, builder);
            return builder.ToString();
        }

        private StringBuilder CheckLinkForErrors(Link link, StringBuilder builder)
        {
            if (!link.AreRequiredFieldsSatisfied())
            {
                builder.Append(link.Name).Append("\r\n");
            }
            foreach (Link child in link.Children)
            {
                CheckLinkForErrors(child, builder);
            }
            return builder;
        }

        private void TreeViewLinkPropertiesAfterSelect(object sender, TreeViewEventArgs e)
        {
            Font fontRegular = new Font(treeViewJointTree.Font, FontStyle.Regular);
            Font fontBold = new Font(treeViewJointTree.Font, FontStyle.Bold);
            if (e.Node == null)
            {
                return;
            }
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
                previouslySelectedNode.NodeFont = fontRegular;
            }
            LinkNode node = (LinkNode)e.Node;
            node.NodeFont = fontBold;
            node.Text = node.Text;
            ActiveSWModel.ClearSelection2(true);
            SelectionMgr manager = ActiveSWModel.SelectionManager;

            SelectData data = manager.CreateSelectData();
            data.Mark = -1;
            foreach (Component2 component in node.Link.SWComponents)
            {
                component.Select4(true, data, false);
            }
            FillLinkPropertyBoxes(node.Link);
            treeViewLinkProperties.Focus();
            previouslySelectedNode = node;
        }

        /// <summary>
        /// Validates text entry for numerical text boxes to limit improper input. It's not perfect
        /// because you can still copy and paste bad input into the fields, but that's addressed
        /// elsewhere
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NumericalTextBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            // In most cases, if we can't parse what they are trying to type, then it's not
            // valid input.
            TextBox textBox = (TextBox)sender;
            string potentialText = textBox.Text + e.KeyChar;
            bool parseSuccess =
                double.TryParse(potentialText,
                    URDFAttribute.URDFNumberStyle,
                    URDFAttribute.URDFNumberFormat,
                    out _);

            // If the key pressed is not a digit, +/- sign or the decimal separator than ignore it (e.Handled = true)
            e.Handled = (!parseSuccess &&
                         !char.IsControl(e.KeyChar) &&
                         !char.IsDigit(e.KeyChar) &&
                         potentialText != "-" &&
                         potentialText != "+");
        }

        #region Link Properties Controls Handlers

        private void ButtonTextureBrowseClick(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                RestoreDirectory = true
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBoxTexture.Text = openFileDialog1.FileName;
            }
        }

        #endregion Link Properties Controls Handlers

        #region Joint Properties Controls Handlers

        private void TreeViewJointtreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            Font fontRegular = new Font(treeViewJointTree.Font, FontStyle.Regular);
            Font fontBold = new Font(treeViewJointTree.Font, FontStyle.Bold);
            if (e.Node == null)
            {
                return;
            }
            if (previouslySelectedNode != null && !previouslySelectedNode.IsBaseNode)
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.NodeFont = fontRegular;
            }
            LinkNode node = (LinkNode)e.Node;
            ActiveSWModel.ClearSelection2(true);
            SelectionMgr manager = ActiveSWModel.SelectionManager;

            SelectData data = manager.CreateSelectData();
            data.Mark = -1;
            foreach (Component2 component in node.Link.SWComponents)
            {
                component.Select4(true, data, false);
            }
            node.NodeFont = fontBold;
            node.Text = node.Text;
            FillJointPropertyBoxes(node.Link.Joint);
            previouslySelectedNode = node;
        }

        private void ComboBoxAxisSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!AutoUpdatingForm)
            {
                if (!(String.IsNullOrWhiteSpace(comboBoxOrigin.Text) ||
                    String.IsNullOrWhiteSpace(comboBoxAxis.Text)))
                {
                    double[] Axis = Exporter.EstimateAxis(comboBoxAxis.Text);
                    Axis = Exporter.LocalizeAxis(Axis, comboBoxOrigin.Text);
                    textBoxAxisX.Text = Axis[0].ToString("G5");
                    textBoxAxisY.Text = Axis[1].ToString("G5");
                    textBoxAxisZ.Text = Axis[2].ToString("G5");
                }
            }
        }

        #endregion Joint Properties Controls Handlers

        private void ShowMimicControls(bool showControls)
        {
            MimicEquationLabel.Visible = showControls;
            MimicJointComboBox.Visible = showControls;
            MimicJointLabel.Visible = showControls;
            MimicMultiplierLabel.Visible = showControls;
            textBoxMimicMultiplier.Visible = showControls;
            MimicOffsetLabel.Visible = showControls;
            textBoxMimicOffset.Visible = showControls;
        }

        private void MimicCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            bool showControls = (sender as CheckBox).Checked;
            ShowMimicControls(showControls);
            textBoxMimicMultiplier.Text = "1.0";
            textBoxMimicOffset.Text = "0.0";
        }
    }
}