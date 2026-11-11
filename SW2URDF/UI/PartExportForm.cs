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
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    public partial class PartExportForm : Form
    {
        public ExportHelper Exporter;
        private readonly UiText ui;
        private static readonly log4net.ILog logger = SW2URDF.Utilities.Logger.GetLogger();
        private string loadedConfiguration;

        private void AlignFieldLabels()
        {
            UiText.AlignLabel(label9, textBox_inertial_origin_x);
            UiText.AlignLabel(label10, textBox_inertial_origin_y);
            UiText.AlignLabel(label11, textBox_inertial_origin_z);
            UiText.AlignLabel(label2, textBox_mass);
            UiText.AlignLabel(label3, textBox_ixx);
            UiText.AlignLabel(label4, textBox_ixy);
            UiText.AlignLabel(label5, textBox_ixz);
            UiText.AlignLabel(label6, textBox_iyy);
            UiText.AlignLabel(label7, textBox_iyz);
            UiText.AlignLabel(label8, textBox_izz);
            UiText.AlignLabel(label18, textBox_inertial_origin_yaw);
            UiText.AlignLabel(label17, textBox_inertial_origin_pitch);
            UiText.AlignLabel(label16, textBox_inertial_origin_roll);
            UiText.AlignLabel(label33, domainUpDown_alpha);
            UiText.AlignLabel(label24, textBox_visual_origin_z);
            UiText.AlignLabel(label25, textBox_visual_origin_y);
            UiText.AlignLabel(label32, domainUpDown_blue);
            UiText.AlignLabel(label26, textBox_visual_origin_x);
            UiText.AlignLabel(label31, domainUpDown_green);
            UiText.AlignLabel(label30, domainUpDown_red);
            UiText.AlignLabel(label28, comboBox_materials);
            UiText.AlignLabel(label22, textBox_visual_origin_roll);
            UiText.AlignLabel(label21, textBox_visual_origin_pitch);
            UiText.AlignLabel(label20, textBox_visual_origin_yaw);
            UiText.AlignLabel(label39, textBox_collision_origin_z);
            UiText.AlignLabel(label38, textBox_collision_origin_y);
            UiText.AlignLabel(label37, textBox_collision_origin_x);
            UiText.AlignLabel(label41, textBox_collision_origin_roll);
            UiText.AlignLabel(label42, textBox_collision_origin_pitch);
            UiText.AlignLabel(label43, textBox_collision_origin_yaw);
            UiText.AlignLabel(label45, textBox_save_as);
        }

        public PartExportForm(SldWorks iSwApp)
        {
            InitializeComponent();
            ui = UiText.FromSolidWorks(iSwApp);
            ui.Apply(this);
            AlignFieldLabels();
            UiLayout.FitForm(this, UiLayout.ScreenDpi());
            Exporter = new ExportHelper(iSwApp);
        }

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

        #region Basic event handelers

        private void ButtonSaveNameBrowseClick(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Path.GetDirectoryName(textBox_save_as.Text),
                FileName = Path.GetFileName(textBox_save_as.Text)
            };

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox_save_as.Text = saveFileDialog1.FileName;
            }
            saveFileDialog1.Dispose();
        }

        #endregion Basic event handelers

        private void ButtonTextureBrowseClick(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Path.GetDirectoryName(textBox_save_as.Text)
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox_texture.Text = openFileDialog1.FileName;
            }
        }

        #region Form event handlers

        private void ButtonFinishClick(object sender, EventArgs e)
        {
            Exporter.PackageName = Path.GetFileName(textBox_save_as.Text);
            Exporter.SavePath = Path.GetDirectoryName(textBox_save_as.Text);
            Exporter.URDFRobot.BaseLink.Name = Exporter.PackageName;

            Exporter.URDFRobot.BaseLink.Inertial.Origin.Update(textBox_inertial_origin_x,
                                                            textBox_inertial_origin_y,
                                                            textBox_inertial_origin_z,
                                                            textBox_inertial_origin_roll,
                                                            textBox_inertial_origin_pitch,
                                                            textBox_inertial_origin_yaw);

            Exporter.URDFRobot.BaseLink.Inertial.Inertia.Update(textBox_ixx,
                                                             textBox_ixy,
                                                             textBox_ixz,
                                                             textBox_iyy,
                                                             textBox_iyz,
                                                             textBox_izz);

            Exporter.URDFRobot.BaseLink.Inertial.Mass.Update(textBox_mass);

            Exporter.URDFRobot.BaseLink.Visual.Origin.Update(textBox_visual_origin_x,
                                                          textBox_visual_origin_y,
                                                          textBox_visual_origin_z,
                                                          textBox_visual_origin_roll,
                                                          textBox_visual_origin_pitch,
                                                          textBox_visual_origin_yaw);

            Exporter.URDFRobot.BaseLink.Visual.Material.Name = comboBox_materials.Text;
            Exporter.URDFRobot.BaseLink.Visual.Material.Texture.wFilename = textBox_texture.Text;

            Exporter.URDFRobot.BaseLink.Visual.Material.Color.Update(domainUpDown_red,
                                                                  domainUpDown_green,
                                                                  domainUpDown_blue,
                                                                  domainUpDown_alpha);

            Exporter.URDFRobot.BaseLink.Collision.Origin.Update(textBox_collision_origin_x,
                                                             textBox_collision_origin_y,
                                                             textBox_collision_origin_z,
                                                             textBox_collision_origin_roll,
                                                             textBox_collision_origin_pitch,
                                                             textBox_collision_origin_yaw);

            // Remember these settings for the active SolidWorks configuration before the link is
            // localized for the export
            Exporter.SavePartSettings();

            Exporter.ExportLink(checkBox_rotate.Checked, objExport.Checked ? MeshExportFormat.OBJ : MeshExportFormat.STL);
            Close();
        }

        private void ObjExportCheckedChanged(object sender, EventArgs e)
        {
            UpdateObjColorControls();
        }

        private void UpdateObjColorControls()
        {
            bool stl = !objExport.Checked;

            // OBJ writes no URDF <material>; color and texture come from the MTL file.
            Control[] colorControls = {
                label28, label29, label30, label31, label32, label33, label34,
                comboBox_materials, domainUpDown_red, domainUpDown_green, domainUpDown_blue,
                domainUpDown_alpha, textBox_texture, button_texturebrowse };
            foreach (Control control in colorControls)
            {
                control.Enabled = stl;
            }
            panelObjColorNote.Visible = !stl;
        }

        private void ButtonCancelClick(object sender, EventArgs e)
        {
            Close();
        }

        private void PartExportFormLoad(object sender, EventArgs e)
        {
            LoadFormForActiveConfiguration();
        }

        // The form is not modal: when another configuration became active meanwhile, show the
        // settings of that configuration.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (loadedConfiguration != null && Exporter.ActiveConfigurationName != loadedConfiguration)
            {
                LoadFormForActiveConfiguration();
            }
        }

        private void LoadFormForActiveConfiguration()
        {
            loadedConfiguration = Exporter.ActiveConfigurationName;
            Exporter.CreateRobotFromActiveModel();
            try
            {
                Exporter.LoadPartSettings();
            }
            catch (Exception ex)
            {
                // Settings that cannot be read leave the values computed from the part.
                logger.Warn("Could not load the saved part settings", ex);
            }
            textBox_save_as.Text = Exporter.SavePath + "\\" + Exporter.PackageName;

            Exporter.URDFRobot.BaseLink.Visual.Origin.FillBoxes(textBox_collision_origin_x,
                                                             textBox_collision_origin_y,
                                                             textBox_collision_origin_z,
                                                             textBox_collision_origin_roll,
                                                             textBox_collision_origin_pitch,
                                                             textBox_collision_origin_yaw,
                                                             "G5");

            Exporter.URDFRobot.BaseLink.Visual.Origin.FillBoxes(textBox_visual_origin_x,
                                                             textBox_visual_origin_y,
                                                             textBox_visual_origin_z,
                                                             textBox_visual_origin_roll,
                                                             textBox_visual_origin_pitch,
                                                             textBox_visual_origin_yaw,
                                                             "G5");

            Exporter.URDFRobot.BaseLink.Visual.Material.Color.FillBoxes(domainUpDown_red,
                                                                     domainUpDown_green,
                                                                     domainUpDown_blue,
                                                                     domainUpDown_alpha,
                                                                     "G5");

            Exporter.URDFRobot.BaseLink.Inertial.Origin.FillBoxes(textBox_inertial_origin_x,
                                                               textBox_inertial_origin_y,
                                                               textBox_inertial_origin_z,
                                                               textBox_inertial_origin_roll,
                                                               textBox_inertial_origin_pitch,
                                                               textBox_inertial_origin_yaw,
                                                               "G5");

            Exporter.URDFRobot.BaseLink.Inertial.Mass.FillBoxes(textBox_mass, "G5");

            Exporter.URDFRobot.BaseLink.Inertial.Inertia.FillBoxes(textBox_ixx,
                                                                textBox_ixy,
                                                                textBox_ixz,
                                                                textBox_iyy,
                                                                textBox_iyz,
                                                                textBox_izz,
                                                                "G5");
        }

        #endregion Form event handlers
    }
}