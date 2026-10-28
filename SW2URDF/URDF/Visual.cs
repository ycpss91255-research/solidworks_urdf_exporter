using System.Runtime.Serialization;

namespace SW2URDF.URDF
{
    //The visual element of a link
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Visual : URDFElement
    {
        public override void WriteURDF(System.Xml.XmlWriter writer)
        {
            // OBJ's native per-face MTL colours are authoritative. A link-wide
            // URDF colour can override those in importers.
            bool nativeMaterial = System.String.Equals(
                System.IO.Path.GetExtension(Geometry.Mesh.Filename), ".obj",
                System.StringComparison.OrdinalIgnoreCase);
            if (nativeMaterial) ChildElements.Remove(Material);
            try { base.WriteURDF(writer); }
            finally { if (nativeMaterial) ChildElements.Add(Material); }
        }

        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly Geometry Geometry;

        [DataMember]
        public readonly Material Material;

        public Visual() : base("visual", false)
        {
            Origin = new Origin(false);
            Geometry = new Geometry();
            Material = new Material();

            ChildElements.Add(Origin);
            ChildElements.Add(Geometry);
            ChildElements.Add(Material);
        }
    }
}
