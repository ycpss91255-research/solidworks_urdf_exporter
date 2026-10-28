# Offline serialization checks; does not connect to SOLIDWORKS or register COM.
param([string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll")
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
[void][Reflection.Assembly]::LoadFrom($AssemblyPath)
$code = @'
using System;
using System.IO;
using System.Xml;
using SW2URDF.URDF;
public static class ObjUrdfCheck {
    static string Write(Visual visual) {
        var text = new StringWriter();
        using (var writer = XmlWriter.Create(text)) visual.WriteURDF(writer);
        return text.ToString();
    }
    public static void Run() {
        var visual = new Visual();
        visual.Material.Name = "manual";
        visual.Geometry.Mesh.Filename = "package://robot/meshes/base.obj";
        string obj = Write(visual);
        if (obj.Contains("<material")) throw new Exception("OBJ material overridden");
        if (!obj.Contains("package://robot/meshes/base.obj")) throw new Exception("Mesh reference changed");
        if (Write(visual) != obj) throw new Exception("Serialization not repeatable");
        visual.Geometry.Mesh.Filename = "package://robot/meshes/base.STL";
        if (!Write(visual).Contains("<material")) throw new Exception("STL material lost");
        Console.WriteLine("PASS: OBJ mesh reference, native material, repeat serialization, STL material regression");
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies @($AssemblyPath, 'System.Xml.dll', 'System.Runtime.Serialization.dll')
[ObjUrdfCheck]::Run()
