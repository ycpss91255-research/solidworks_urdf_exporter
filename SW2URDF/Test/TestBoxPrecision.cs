using System.Windows.Forms;
using SW2URDF.URDF;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Does not need SolidWorks. The dialogs show a value with its full precision (format R). Pressing
    /// Next or Export reads the text boxes back into the model; a value the user did not touch must keep
    /// its full precision, and a value the user typed must be taken exactly as typed.
    /// </summary>
    public class TestBoxPrecision
    {
        private const double Roll = -1.5707963267949;
        private const double Offset = 0.00249115384615384;
        private const double Long = 1.2345678901234;
        private const string Format = "R";

        private static TextBox[] Boxes(int count)
        {
            TextBox[] boxes = new TextBox[count];
            for (int i = 0; i < count; i++)
            {
                boxes[i] = new TextBox();
            }
            return boxes;
        }

        [Fact]
        public void OriginKeepsFullPrecisionWhenBoxesAreUntouched()
        {
            Origin origin = new Origin(true);
            origin.SetXYZ(new double[] { Offset, Long, -Long });
            origin.SetRPY(new double[] { Roll, 0, Roll });
            TextBox[] b = Boxes(6);
            origin.FillBoxes(b[0], b[1], b[2], b[3], b[4], b[5], Format);
            Assert.Equal("-1.5707963267949", b[3].Text);
            Assert.Equal("0.00249115384615384", b[0].Text);
            Assert.Equal(Roll, double.Parse(b[3].Text, System.Globalization.CultureInfo.InvariantCulture));
            origin.Update(b[0], b[1], b[2], b[3], b[4], b[5]);
            Assert.Equal(new double[] { Offset, Long, -Long }, origin.GetXYZ());
            Assert.Equal(new double[] { Roll, 0, Roll }, origin.GetRPY());
        }

        [Fact]
        public void OriginTakesTypedValuesExactly()
        {
            Origin origin = new Origin(true);
            origin.SetXYZ(new double[] { Offset, Long, -Long });
            origin.SetRPY(new double[] { Roll, 0, Roll });
            TextBox[] b = Boxes(6);
            origin.FillBoxes(b[0], b[1], b[2], b[3], b[4], b[5], Format);
            b[1].Text = "0.123456789012";
            b[3].Text = "-1.57";
            origin.Update(b[0], b[1], b[2], b[3], b[4], b[5]);
            Assert.Equal(Offset, origin.GetXYZ()[0]);
            Assert.Equal(0.123456789012, origin.GetXYZ()[1]);
            Assert.Equal(-Long, origin.GetXYZ()[2]);
            Assert.Equal(-1.57, origin.GetRPY()[0]);
            Assert.Equal(Roll, origin.GetRPY()[2]);
        }

        [Fact]
        public void ValueChangedAfterFillIsStillReadFromTheBox()
        {
            Origin origin = new Origin(true);
            origin.SetXYZ(new double[] { Long, 0, 0 });
            TextBox[] b = Boxes(6);
            origin.FillBoxes(b[0], b[1], b[2], b[3], b[4], b[5], Format);
            origin.SetXYZ(new double[] { 5.5, 0, 0 });
            b[0].Text = "7.25";
            origin.Update(b[0], b[1], b[2], b[3], b[4], b[5]);
            Assert.Equal(7.25, origin.GetXYZ()[0]);
        }

        [Fact]
        public void AxisAndMassKeepFullPrecision()
        {
            Axis axis = new Axis();
            axis.SetXYZ(new double[] { 0.7071067811865, 0.7071067811865, 0 });
            TextBox[] b = Boxes(3);
            axis.FillBoxes(b[0], b[1], b[2], Format);
            axis.Update(b[0], b[1], b[2]);
            Assert.Equal(0.7071067811865, axis.GetXYZ()[0]);

            Mass mass = new Mass { Value = Long };
            TextBox m = new TextBox();
            mass.FillBoxes(m, Format);
            mass.Update(m);
            Assert.Equal(Long, mass.Value);
            m.Text = "2.5";
            mass.Update(m);
            Assert.Equal(2.5, mass.Value);
        }

        [Fact]
        public void InertiaKeepsFullPrecision()
        {
            Inertia inertia = new Inertia
            {
                Ixx = 1.2345678901234e-5,
                Ixy = 0,
                Ixz = -3.3333333333333e-7,
                Iyy = 2.2222222222222e-5,
                Iyz = 0,
                Izz = 9.8765432109876e-6
            };
            TextBox[] b = Boxes(6);
            inertia.FillBoxes(b[0], b[1], b[2], b[3], b[4], b[5], Format);
            inertia.Update(b[0], b[1], b[2], b[3], b[4], b[5]);
            Assert.Equal(1.2345678901234e-5, inertia.Ixx);
            Assert.Equal(-3.3333333333333e-7, inertia.Ixz);
            Assert.Equal(9.8765432109876e-6, inertia.Izz);
        }

        [Fact]
        public void JointElementsKeepFullPrecision()
        {
            Limit limit = new Limit { Lower = -Long, Upper = Long, Effort = 12.3456789, Velocity = 0.98765432 };
            TextBox[] b = Boxes(4);
            limit.FillBoxes(b[0], b[1], b[2], b[3], Format);
            limit.SetValues(b[0], b[1], b[2], b[3]);
            Assert.Equal(-Long, limit.Lower);
            Assert.Equal(12.3456789, limit.Effort);
            Assert.Equal(0.98765432, limit.Velocity);

            Dynamics dynamics = new Dynamics { Damping = Long, Friction = 0.123456789 };
            dynamics.FillBoxes(b[0], b[1], Format);
            dynamics.SetValues(b[0], b[1]);
            Assert.Equal(Long, dynamics.Damping);
            Assert.Equal(0.123456789, dynamics.Friction);

            Calibration calibration = new Calibration { Rising = Long, Falling = -Long };
            calibration.FillBoxes(b[0], b[1], Format);
            calibration.SetValues(b[0], b[1]);
            Assert.Equal(Long, calibration.Rising);
            Assert.Equal(-Long, calibration.Falling);

            SafetyController safety = new SafetyController
            {
                SoftLower = -Long,
                SoftUpper = Long,
                KPosition = 1.23456789,
                KVelocity = 9.87654321
            };
            safety.FillBoxes(b[0], b[1], b[2], b[3], Format);
            safety.SetValues(b[0], b[1], b[2], b[3]);
            Assert.Equal(-Long, safety.SoftLower);
            Assert.Equal(1.23456789, safety.KPosition);
            Assert.Equal(9.87654321, safety.KVelocity);
        }

        [Fact]
        public void ColorKeepsFullPrecision()
        {
            URDF.Color color = new URDF.Color();
            color.SetColor(new double[] { 0.1234567890123, 0.5, 0.7654321098765, 1 });
            DomainUpDown[] b = { new DomainUpDown(), new DomainUpDown(), new DomainUpDown(), new DomainUpDown() };
            color.FillBoxes(b[0], b[1], b[2], b[3], Format);
            color.Update(b[0], b[1], b[2], b[3]);
            Assert.Equal(0.1234567890123, color.Red);
            Assert.Equal(0.7654321098765, color.Blue);
        }

        [Fact]
        public void ShownTextIsTheFullPrecisionAndEditedTextIsTakenAsTyped()
        {
            double[] values = { 1.0 / 3.0, 0.1, 2.34558007901714E-18, -123456.789012345678, 1e-300, 0.0 };
            foreach (double value in values)
            {
                Mass mass = new Mass { Value = value };
                TextBox box = new TextBox();
                mass.FillBoxes(box, Format);
                Assert.Equal(value, double.Parse(box.Text, System.Globalization.CultureInfo.InvariantCulture));
                mass.Update(box);
                Assert.Equal(value, mass.Value);
                box.Text = "4.5";
                mass.Update(box);
                Assert.Equal(4.5, mass.Value);
            }
        }

        [Fact]
        public void MimicKeepsFullPrecision()
        {
            Mimic mimic = new Mimic();
            mimic.Update("j", "1.2345678901234567", "0.1");
            TextBox[] b = Boxes(2);
            mimic.FillBoxes(b[0], b[1]);
            mimic.Update("j", b[0].Text, b[1].Text);
            Assert.Equal(1.2345678901234567, mimic.Multiplier);
        }
    }
}
