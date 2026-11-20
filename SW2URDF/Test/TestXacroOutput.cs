using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using SW2URDF.ROS;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Optional xacro output (issue 144). Does not need SolidWorks, so it has no fixture.
    /// </summary>
    public class TestXacroOutput
    {
        private const string XacroNamespaceDeclaration = "xmlns:xacro=\"http://www.ros.org/wiki/xacro\"";

        private static object InvokeSerialization(string method, object argument)
        {
            return typeof(ConfigurationSerialization)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new[] { argument });
        }

        private static Robot BuildRobot(bool xacro)
        {
            Robot robot = new Robot { Name = "demo" };
            robot.BaseLink.Name = "base_link";
            Link arm = new Link(robot.BaseLink) { Name = "arm_link" };
            arm.Joint.Name = "arm_joint";
            arm.Joint.Type = "fixed";
            arm.Inertial.Mass.Value = 1.5;
            arm.Visual.Geometry.Mesh.Filename = "package://demo/meshes/arm_link.STL";
            arm.Collision.Geometry.Mesh.Filename = "package://demo/meshes/arm_link.STL";
            robot.BaseLink.Children.Add(arm);
            robot.WriteXacro = xacro;
            return robot;
        }

        private static string WriteRobot(Robot robot)
        {
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true,
                OmitXmlDeclaration = true
            };
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                robot.WriteURDF(writer);
            }
            return sb.ToString();
        }

        // The creation comment carries the exporter version; the comparisons skip it.
        private static string WithoutComment(string text)
        {
            int start = text.IndexOf("<!--", StringComparison.Ordinal);
            int end = text.IndexOf("-->", StringComparison.Ordinal);
            return start >= 0 && end > start ? text.Remove(start, end + 3 - start) : text;
        }

        private static string WriteLaunch(Action<string> write, string fileName)
        {
            string dir = Path.Combine(Path.GetTempPath(), "xacro_launch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                write(dir + Path.DirectorySeparatorChar);
                return File.ReadAllText(Path.Combine(dir, fileName));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FileNamesFollowTheChoice()
        {
            Assert.Equal("demo.urdf", XacroOutput.RobotFileName("demo", false));
            Assert.Equal("demo.urdf.xacro", XacroOutput.RobotFileName("demo", true));
        }

        [Fact]
        public void XacroIsOffByDefault()
        {
            Assert.False(new Link().WriteXacro);
            Assert.False(new Robot().WriteXacro);
        }

        [Fact]
        public void TheChoiceSurvivesSavingAndLoading()
        {
            Link root = new Link { Name = "base_link", WriteXacro = true };
            string saved = (string)InvokeSerialization("SerializeToString", new LinkNode(root));
            Assert.Contains("WriteXacro", saved);

            LinkNode loaded = (LinkNode)InvokeSerialization("DeserializeFromString", saved);
            Assert.True(loaded.Link.WriteXacro);
            Assert.Equal(saved, (string)InvokeSerialization("SerializeToString", loaded));
        }

        [Fact]
        public void DataWrittenWithoutTheMemberLoadsAsUnchecked()
        {
            // What an exporter without the member wrote: unchecked data is identical to it
            // (the member is not emitted when false).
            Link root = new Link { Name = "base_link" };
            string saved = (string)InvokeSerialization("SerializeToString", new LinkNode(root));
            Assert.DoesNotContain("WriteXacro", saved);

            LinkNode loaded = (LinkNode)InvokeSerialization("DeserializeFromString", saved);
            Assert.NotNull(loaded);
            Assert.False(loaded.Link.WriteXacro);
        }

        [Fact]
        public void TheChoiceIsKeptWhenSavedSettingsAreApplied()
        {
            Link computed = new Link { Name = "computed" };
            Link saved = new Link { Name = "saved", WriteXacro = true };
            ExportHelper.ApplySavedSettings(computed, saved);
            Assert.True(computed.WriteXacro);

            ExportHelper.ApplySavedSettings(computed, new Link { Name = "other" });
            Assert.False(computed.WriteXacro);
        }

        [Fact]
        public void CloningKeepsTheChoice()
        {
            Link root = new Link { Name = "base_link", WriteXacro = true };
            Assert.True(root.Clone().WriteXacro);
        }

        [Fact]
        public void PlainUrdfHasNoXacroNamespace()
        {
            string text = WriteRobot(BuildRobot(false));
            Assert.DoesNotContain("xacro", text);
            Assert.Contains("<robot", text);
        }

        [Fact]
        public void XacroOutputDeclaresTheNamespaceAndIsOtherwiseIdentical()
        {
            string plain = WithoutComment(WriteRobot(BuildRobot(false)));
            string xacro = WithoutComment(WriteRobot(BuildRobot(true)));

            Assert.Contains(XacroNamespaceDeclaration, xacro);
            Assert.Contains("package://demo/meshes/arm_link.STL", xacro);

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xacro);
            Assert.Equal("robot", doc.DocumentElement.Name);
            Assert.Equal("demo", doc.DocumentElement.GetAttribute("name"));
            Assert.Equal(XacroOutput.NamespaceUri, doc.DocumentElement.GetAttribute("xmlns:xacro"));

            // Removing the declaration gives back exactly the plain URDF
            string stripped = xacro.Replace("\r\n  " + XacroNamespaceDeclaration, "")
                .Replace(" " + XacroNamespaceDeclaration, "");
            Assert.Equal(plain, stripped);
        }

        [Fact]
        public void LaunchFilesAreUnchangedWhenXacroIsNotChosen()
        {
            string display = WriteLaunch(dir => new Rviz("demo", "demo.urdf").WriteFiles(dir), "display.launch");
            Assert.Contains("textfile=\"$(find demo)/urdf/demo.urdf\"", display);
            Assert.DoesNotContain("xacro", display);
            Assert.Equal(display,
                WriteLaunch(dir => new Rviz("demo", "demo.urdf", false).WriteFiles(dir), "display.launch"));

            string gazebo = WriteLaunch(dir => new Gazebo("demo", "demo", "demo.urdf").WriteFile(dir), "gazebo.launch");
            Assert.Contains("-file $(find demo)/urdf/demo.urdf -urdf -model demo", gazebo);
            Assert.DoesNotContain("xacro", gazebo);
        }

        [Fact]
        public void LaunchFilesExpandTheXacroWhenChosen()
        {
            string command = "command=\"$(find xacro)/xacro $(find demo)/urdf/demo.urdf.xacro\"";

            string display = WriteLaunch(
                dir => new Rviz("demo", "demo.urdf.xacro", true).WriteFiles(dir), "display.launch");
            Assert.Contains("name=\"robot_description\"", display);
            Assert.Contains(command, display);
            Assert.DoesNotContain("textfile", display);

            string gazebo = WriteLaunch(
                dir => new Gazebo("demo", "demo", "demo.urdf.xacro", true).WriteFile(dir), "gazebo.launch");
            Assert.Contains(command, gazebo);
            Assert.Contains("-param robot_description -urdf -model demo", gazebo);
            Assert.DoesNotContain("-file", gazebo);
        }
    }
}
