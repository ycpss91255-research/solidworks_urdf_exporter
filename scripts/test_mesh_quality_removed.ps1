# Offline checks that the STL mesh quality (Fine/Coarse) option is gone.
# Does not connect to SOLIDWORKS or register COM. Pure PowerShell (no Add-Type compilation),
# so every file that runs is a signed script plus signed assemblies.
#   1. Neither export form has a mesh quality control or its OBJ note any more.
#   2. Link no longer carries STLQualityFine, yet data saved by older versions (which contains
#      that member) still loads and keeps every other value.
#   3. The exporter no longer reads or writes SOLIDWORKS' STL quality preference.
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$SourceRoot = "$PSScriptRoot/../SW2URDF"
)
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Runtime.Serialization
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$failures = New-Object System.Collections.ArrayList
function Require([bool]$condition, [string]$message) { if (-not $condition) { [void]$failures.Add($message) } }

function New-Form([Type]$type) {
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
    [void][System.Windows.Forms.Form].GetConstructor($flags, $null, [Type[]]@(), $null).Invoke($form, @())
    try { [void]$type.GetMethod('InitializeComponent', $flags).Invoke($form, @()) } catch { throw $_.Exception.InnerException }
    return $form
}
function Get-AllControls([System.Windows.Forms.Control]$control) {
    foreach ($child in $control.Controls) { $child; Get-AllControls $child }
}

# 1. Forms
$bannedNames = 'radioButton_fine', 'radioButton_course', 'radioButtonFine', 'radioButtonCourse',
    'panelObjQualityNote', 'labelObjQualityNote'
$bannedTexts = 'Fine', 'Course', 'Coarse', 'Mesh Detail', 'Visual Mesh Detail',
    'OBJ mesh density is determined by SolidWorks.'
foreach ($typeName in 'SW2URDF.UI.PartExportForm', 'SW2URDF.UI.AssemblyExportForm') {
    $type = $assembly.GetType($typeName, $true)
    $form = New-Form $type
    try {
        foreach ($control in @(Get-AllControls $form)) {
            Require ($bannedNames -notcontains $control.Name) "$($type.Name): quality control still present: $($control.Name)"
            Require ($bannedTexts -notcontains $control.Text) "$($type.Name): quality text still present: $($control.Name) '$($control.Text)'"
        }
    } finally { $form.Dispose() }
}

# 2. Link serialization
$link = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($assembly.GetType('SW2URDF.URDF.Link', $true))
$link.isFixedFrame = $true
$link.isIncomplete = $true
function Save-Link($value) {
    $serializer = New-Object System.Runtime.Serialization.DataContractSerializer($value.GetType())
    $stream = New-Object System.IO.MemoryStream
    $serializer.WriteObject($stream, $value)
    return [Text.Encoding]::UTF8.GetString($stream.ToArray())
}
function Load-Link([string]$xml) {
    $serializer = New-Object System.Runtime.Serialization.DataContractSerializer($link.GetType())
    $stream = New-Object System.IO.MemoryStream(, [Text.Encoding]::UTF8.GetBytes($xml))
    return $serializer.ReadObject($stream)
}
$xml = Save-Link $link
Require (-not $xml.Contains('STLQualityFine')) "STLQualityFine is still serialized: $xml"
Require ($link.GetType().GetField('STLQualityFine') -eq $null -and $link.GetType().GetProperty('STLQualityFine') -eq $null) 'Link still has STLQualityFine'

# Data written by an older exporter contains STLQualityFine; it must load with everything else intact.
foreach ($value in 'true', 'false') {
    $old = $xml.Replace('<isIncomplete', "<STLQualityFine>$value</STLQualityFine><isIncomplete")
    Require ($old -ne $xml) 'Old-format member injected'
    try {
        $loaded = Load-Link $old
        Require ($loaded.isFixedFrame -and $loaded.isIncomplete) "Other data lost when loading old data (STLQualityFine=$value)"
    } catch { Require $false "Old data with STLQualityFine=$value fails to load: $($_.Exception.Message)" }
}

# 3. Exporter source: no STL quality handling is left.
$helper = Get-Content -LiteralPath (Join-Path $SourceRoot 'URDFExport/ExportHelper.cs') -Raw
Require ($helper -notmatch 'swSTLQuality') 'ExportHelper still touches swSTLQuality'
Require ($helper -notmatch 'STLQualityFine') 'ExportHelper still uses STLQualityFine'

if ($failures.Count -gt 0) { $failures | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
Write-Output 'PASS: mesh quality option is removed; data saved with STLQualityFine still loads'
