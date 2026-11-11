namespace SW2URDF.URDFExport
{
    public enum ExporterKind { None, Part, Assembly }

    /// <summary>
    /// Picks the exporter for the active document, so a part form is never opened on an
    /// assembly (it has no base link there). Free of SolidWorks types so it can be unit tested.
    /// </summary>
    public static class ExporterRouting
    {
        // swDocumentTypes_e: swDocPART = 1, swDocASSEMBLY = 2
        public static ExporterKind Decide(int documentType)
        {
            switch (documentType)
            {
                case 1: return ExporterKind.Part;
                case 2: return ExporterKind.Assembly;
                default: return ExporterKind.None;
            }
        }
    }
}