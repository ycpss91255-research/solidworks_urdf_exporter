using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks. CSV import and export must not depend on the culture of the machine, and
    /// text columns must stay text even when they look like numbers (issue 63).
    /// </summary>
    public class TestCsvInvariant
    {
        private static readonly string[] Cultures = { "en-US", "de-DE", "zh-TW", "fr-FR" };

        private static void InCulture(string name, Action action)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(name);
                action();
            }
            finally { Thread.CurrentThread.CurrentCulture = saved; }
        }

        private static List<Link> Load(string csv)
        {
            using (MemoryStream stream = new MemoryStream(new UTF8Encoding(false).GetBytes(csv)))
            {
                return ImportExport.LoadURDFRobotFromCSV(stream);
            }
        }

        [Fact]
        public void NumericLookingTextStaysText()
        {
            foreach (string culture in Cultures)
            {
                InCulture(culture, () =>
                {
                    Link link = Load("Link Name,Mesh Filename,Joint Name\r\n007,1e3,1\r\n")[0];
                    Assert.Equal("007", link.Name);
                    Assert.Equal("1e3", link.Visual.Geometry.Mesh.Filename);
                    Assert.Equal("1", link.Joint.Name);
                });
            }
        }

        [Fact]
        public void NameWithSemicolonStaysText()
        {
            Link link = Load("Link Name,Mesh Filename\r\na;b,x;y.STL\r\n")[0];
            Assert.Equal("a;b", link.Name);
            Assert.Equal("x;y.STL", link.Visual.Geometry.Mesh.Filename);
        }

        [Fact]
        public void NumbersUseThePointInEveryCulture()
        {
            foreach (string culture in Cultures)
            {
                InCulture(culture, () =>
                {
                    Link link = Load("Link Name,Mass,Moment Ixx\r\nl,0.5,1e3\r\n")[0];
                    Assert.Equal(0.5, link.Inertial.Mass.Value);
                    Assert.Equal(1000.0, link.Inertial.Inertia.Ixx);
                });
            }
        }

        [Fact]
        public void DecimalCommaAndGroupSeparatorsAreRejectedNotMisread()
        {
            foreach (string culture in Cultures)
            {
                InCulture(culture, () =>
                {
                    Link link = Load("Link Name,Mass,Moment Ixx\r\nl,\"1,5\",\"1,500\"\r\n")[0];
                    Assert.Equal(0.0, link.Inertial.Mass.Value);
                    Assert.Equal(0.0, link.Inertial.Inertia.Ixx);
                });
            }
        }

        [Fact]
        public void OriginValuesUseThePointInEveryCulture()
        {
            foreach (string culture in Cultures)
            {
                InCulture(culture, () =>
                {
                    Link link = Load("Link Name,Visual X,Visual Y\r\nl,0.25,-1.5\r\n")[0];
                    double[] xyz = link.Visual.Origin.GetXYZ();
                    Assert.Equal(0.25, xyz[0]);
                    Assert.Equal(-1.5, xyz[1]);
                });
            }
        }

        [Fact]
        public void WrittenCsvIsTheSameInEveryCultureAndReadsBack()
        {
            string reference = null;
            foreach (string culture in Cultures)
            {
                InCulture(culture, () =>
                {
                    Link link = new Link { Name = "007" };
                    link.Inertial.Mass.Value = 0.5;
                    link.Visual.Geometry.Mesh.Filename = "1e3";
                    Robot robot = new Robot();
                    robot.SetBaseLink(link);
                    string file = Path.GetTempFileName();
                    try
                    {
                        ImportExport.WriteRobotToCSV(robot, file);
                        string text = File.ReadAllText(file);
                        if (reference == null) reference = text;
                        Assert.Equal(reference, text);
                        Assert.DoesNotContain("0,5", text);
                        using (FileStream stream = File.OpenRead(file))
                        {
                            Link back = ImportExport.LoadURDFRobotFromCSV(stream)[0];
                            Assert.Equal("007", back.Name);
                            Assert.Equal("1e3", back.Visual.Geometry.Mesh.Filename);
                            Assert.Equal(0.5, back.Inertial.Mass.Value);
                        }
                    }
                    finally { File.Delete(file); }
                });
            }
        }

        [Fact]
        public void TextBoxValuesAreNotMisreadAsThousands()
        {
            Mass mass = new Mass();
            Assert.False(URDFAttribute.TryParseDouble("1,5", out _));
            Assert.False(URDFAttribute.TryParseDouble("$1", out _));
            Assert.True(URDFAttribute.TryParseDouble("1.5", out double value));
            Assert.Equal(1.5, value);
            Assert.True(URDFAttribute.TryParseDouble("-2e-3", out value));
            Assert.Equal(-0.002, value);
        }
    }
}
