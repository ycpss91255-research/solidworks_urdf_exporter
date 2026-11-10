# The interface language must not change exported URDF content: link, joint and robot
# names, and all numeric values, are identical whichever language is active. Needs no
# SOLIDWORKS. Pure PowerShell: only signed scripts and signed assemblies run.
#  1. The same robot is written while the English and the Traditional Chinese UiText are in
#     use (under a non-English numeric culture) and the two documents are compared.
#  2. Non-ASCII names a user typed are written verbatim, never translated.
#  3. The URDF model and writer sources never touch UiText, and the package writer only
#     uses it for the message box.
param(
    [string]$Root = "$PSScriptRoot/..",
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll"
)
$ErrorActionPreference = 'Stop'
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw "FAIL: $message" } }
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$uiType = $assembly.GetType('SW2URDF.UI.UiText', $true)

function New-Robot([string]$robotName, [string]$baseName, [string]$linkName, [string]$jointName) {
    $robot = $assembly.GetType('SW2URDF.URDF.Robot', $true)::new()
    $robot.Name = $robotName
    $robot.BaseLink.Name = $baseName
    $link = $assembly.GetType('SW2URDF.URDF.Link', $true)::new()
    $link.Name = $linkName
    $link.Inertial.Mass.Value = 1.25
    $joint = $link.Joint
    $joint.Name = $jointName
    $joint.Type = 'revolute'
    $joint.Parent.Name = $baseName
    $joint.Child.Name = $linkName
    $joint.Axis.X = 0; $joint.Axis.Y = 0; $joint.Axis.Z = 1
    $joint.Limit.Lower = -1.5; $joint.Limit.Upper = 1.5; $joint.Limit.Effort = 10.25; $joint.Limit.Velocity = 2.5
    $link.Parent = $robot.BaseLink
    $robot.BaseLink.Children.Add($link)
    return $robot
}

function Write-Urdf($robot) {
    $path = [IO.Path]::GetTempFileName()
    try {
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Indent = $true
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $writer = [System.Xml.XmlWriter]::Create($path, $settings)
        $robot.WriteURDF($writer)   # also closes the writer
        return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    } finally { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
}

$thread = [Threading.Thread]::CurrentThread
$originalCulture = $thread.CurrentCulture
try {
    $thread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    $documents = @{}
    foreach ($language in 'english', 'chinese') {
        $ui = [Activator]::CreateInstance($uiType, @(, $language))
        # Use the language the way the forms do before the robot is written.
        $form = New-Object System.Windows.Forms.Form; $form.Text = 'Cancel'; $ui.Apply($form); $form.Dispose()
        $documents[$language] = Write-Urdf (New-Robot 'demo_robot' 'base_link' 'arm_link' 'shoulder')
    }
    Require ($documents['english'] -ceq $documents['chinese']) 'URDF differs between English and Traditional Chinese'
    $xml = [xml]$documents['chinese']
    Require ($xml.robot.name -ceq 'demo_robot') 'robot name'
    Require ($xml.robot.link[0].name -ceq 'base_link' -and $xml.robot.link[1].name -ceq 'arm_link') 'base link name'
    $joint = $xml.robot.joint
    Require ($joint.name -ceq 'shoulder' -and $joint.type -ceq 'revolute') 'joint name or type'
    Require ($joint.limit.lower -ceq '-1.5' -and $joint.limit.upper -ceq '1.5' -and $joint.limit.effort -ceq '10.25') 'numbers use the invariant culture'
    Require ($joint.axis.xyz -ceq '0 0 1') 'axis'

    # Names typed by the user are data. They are written verbatim in either language.
    $u = (-join [char[]](0x6a5f, 0x5668, 0x4eba)) + '_'; $b = (-join [char[]](0x5e95, 0x5ea7)) + '_'; $a = (-join [char[]](0x624b, 0x81c2)) + '_'; $j = [string][char]0x80a9 + '_'
    $custom = Write-Urdf (New-Robot ($u + 'robot') ($b + 'base') ($a + 'arm') ($j + 'joint'))
    $parsed = [xml]$custom
    Require ($parsed.robot.name -ceq ($u + 'robot') -and $parsed.robot.link[0].name -ceq ($b + 'base') -and $parsed.robot.joint.name -ceq ($j + 'joint')) 'user-typed names were altered'
} finally { $thread.CurrentCulture = $originalCulture }

foreach ($file in Get-ChildItem -Path (Join-Path $Root 'SW2URDF/URDF') -Filter *.cs) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    Require ($text -notmatch '\bUiText\b|\bui\.Get\(') "$($file.Name) in the URDF model uses interface text"
}
$package = Get-Content -LiteralPath (Join-Path $Root 'SW2URDF/URDFExport/URDFPackage.cs') -Encoding UTF8
foreach ($line in @($package | Where-Object { $_ -match '\bui\.Get\(' })) {
    Require ($line -match 'MessageBox') "URDFPackage uses interface text outside a message box: $line"
}
Write-Output 'PASS: English and Traditional Chinese write identical URDF; user names verbatim; model code independent of UiText'
