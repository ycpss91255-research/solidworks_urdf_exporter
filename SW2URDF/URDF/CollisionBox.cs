using System.Globalization;
using System.Xml;

namespace SW2URDF.URDF
{
    // A <collision> made of one axis-aligned <box>, placed by an origin in the link frame.
    // Kept outside the DataContract/URDFElement graph on purpose: it is produced at export time
    // from SolidWorks bodies and never stored in the model's saved configuration.
    public class CollisionBox
    {
        public string Name;
        public double[] Size;   // x y z in metres
        public double[] XYZ;    // centre in the link frame
        public double[] RPY;    // orientation in the link frame

        public CollisionBox(string name, double[] size, double[] xyz, double[] rpy)
        {
            Name = name;
            Size = size;
            XYZ = xyz;
            RPY = rpy;
        }

        private static string Join(double[] values)
        {
            NumberFormatInfo format = URDFAttribute.URDFNumberFormat;
            return string.Join(" ", new[] { values[0].ToString(format), values[1].ToString(format), values[2].ToString(format) });
        }

        public void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("collision");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            writer.WriteStartElement("origin");
            writer.WriteAttributeString("xyz", Join(XYZ));
            writer.WriteAttributeString("rpy", Join(RPY));
            writer.WriteEndElement();
            writer.WriteStartElement("geometry");
            writer.WriteStartElement("box");
            writer.WriteAttributeString("size", Join(Size));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }
}
