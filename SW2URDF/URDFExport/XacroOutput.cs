namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Names and constants of the optional xacro output. A xacro file is the plain URDF with the xacro
    /// namespace declared on the robot element, written as .urdf.xacro instead of .urdf.
    /// </summary>
    public static class XacroOutput
    {
        public const string NamespacePrefix = "xacro";
        public const string NamespaceUri = "http://www.ros.org/wiki/xacro";
        public const string PlainExtension = ".urdf";
        public const string XacroExtension = ".urdf.xacro";

        public static string RobotFileExtension(bool xacro)
        {
            return xacro ? XacroExtension : PlainExtension;
        }

        public static string RobotFileName(string robotName, bool xacro)
        {
            return robotName + RobotFileExtension(xacro);
        }
    }
}
