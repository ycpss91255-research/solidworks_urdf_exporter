# PropertyManager text check (issue #23). Needs no SOLIDWORKS.
#  1. Static: every user-visible string in the PropertyManager sources goes through ui.Get.
#     Only the identifiers below may stay as raw English literals, each for a stated reason.
#  2. Runtime (unless -StaticOnly): the page language comes from SolidWorks, fallback is English,
#     the combo-box protocol values survive a display/value round trip untranslated, and
#     the thread culture is untouched.
# Pure PowerShell: only signed scripts and signed assemblies run.
param(
    [string]$Root = "$PSScriptRoot/..",
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [switch]$StaticOnly
)
$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.ArrayList
function Fail([string]$message) { [void]$failures.Add($message) }

# Literals that are NOT display text. Anything else containing letters must be ui.Get(...).
$identifiers = @{
    'URDF Export'            = 'SolidWorks configuration name'
    'URDF Export Items'      = 'feature-tree folder name written into the model'
    'URDF Reference'         = 'sketch feature name looked up in the model'
    'Origin_global'          = 'coordinate system feature name looked up in the model'
    'COORDSYS'               = 'SelectByID2 type string'
    'SKETCH'                 = 'SelectByID2 type string'
    'ATTRIBUTE'              = 'SelectByID2 type string'
    'AXIS'                   = 'SelectByID2 type string'
    'base_link'              = 'URDF link name'
    'Empty_Link'             = 'URDF link name'
    'Automatically Generate' = 'protocol value stored in the configuration and compared by the exporter'
    'Automatically Detect'   = 'protocol value stored in the configuration and compared by the exporter'
    'None'                   = 'protocol value (no axis) stored in the configuration'
    'continuous'             = 'URDF joint type'
    'revolute'               = 'URDF joint type'
    'prismatic'              = 'URDF joint type'
    'fixed'                  = 'URDF joint type'
    'nothing'                = 'sentinel for the item search loop, never displayed'
}
# Static COM (un)registration runs under regasm, outside SolidWorks: no SolidWorks language exists
# there, so those two messages stay English (raw source text, backslashes included).
$registrationOnly = @('There was a problem unregistering this dll: \n\"', '\"\nEmail your maintainer with the log file found at ')
$literal = '"((?:[^"\\]|\\.)*)"'
$files = 'SW2URDF/URDFExport/ExportPropertyManager.cs', 'SW2URDF/URDFExport/ExportPropertyManagerExtension.cs', 'SW2URDF/SW/SwAddin.cs'
foreach ($rel in $files) {
    $text = Get-Content -LiteralPath (Join-Path $Root $rel) -Raw -Encoding UTF8
    $text = [regex]::Replace($text, '(?s)/\*.*?\*/', { param($m) ' ' * 0 })
    $text = [regex]::Replace($text, '(?m)^\s*//.*$', '')
    foreach ($m in [regex]::Matches($text, $literal)) {
        $value = $m.Groups[1].Value
        if ($value -notmatch '[A-Za-z]{2,}') { continue }
        $start = $text.LastIndexOfAny([char[]]';{}', $m.Index)
        $statement = $text.Substring($start + 1, $m.Index - $start - 1)
        # Developer-facing log output is not user-visible.
        if ($statement -match '\blogger\.|Debug\.Print|\[Guid|\[ComRegister|\[ProgId|\bkeyname\b|\bGetSubKey|OpenSubKey|CreateSubKey|DeleteSubKey|\bCommandItem|\[Display') { continue }
        if ($rel -like '*SwAddin.cs' -and $statement -notmatch 'MessageBox') { continue }
        if ($statement -match 'ui\.Get\(\s*$') { continue }
        if ($identifiers.ContainsKey($value)) { continue }
        if ($rel -like '*SwAddin.cs' -and $registrationOnly -contains $value) { continue }
        Fail "$rel has an untranslated user-visible literal: `"$value`""
    }
}

# Resource keys for the strings that used to be hard-coded.
$required = @('preview and export...', 'preview the generated urdf and export to a urdf package',
    'add child link', 'remove', 'automatically generate', 'automatically detect', 'none')
$zhPath = Join-Path $Root 'SW2URDF/UI/Strings.zh-TW.resx'
$zh = @{}
foreach ($entry in ([xml](Get-Content -LiteralPath $zhPath -Raw -Encoding UTF8)).root.data) { $zh[[string]$entry.name] = [string]$entry.value }
foreach ($k in $required) { if (-not $zh.ContainsKey($k)) { Fail "zh-TW lacks '$k'" } }

if (-not $StaticOnly) {
    $AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
    [void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path -Parent $AssemblyPath) 'log4net.dll'))
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $uiType = $assembly.GetType('SW2URDF.UI.UiText', $true)
    function New-Ui($language) { [Activator]::CreateInstance($uiType, @(, [string]$language)) }
    $en = New-Ui 'english'
    $tw = New-Ui 'chinese'
    $cultureBefore = [Threading.Thread]::CurrentThread.CurrentCulture.Name
    foreach ($k in 'Preview and Export...', 'Preview the generated URDF and export to a URDF package', 'Add Child Link', 'Remove') {
        if ($en.Get($k) -cne $k) { Fail "English changed '$k'" }
        if ($tw.Get($k) -ceq $k) { Fail "zh-TW does not translate '$k'" }
        if ((New-Ui 'french').Get($k) -cne $k) { Fail "French does not fall back to English for '$k'" }
    }
    foreach ($v in 'Automatically Generate', 'Automatically Detect', 'None') {
        if ($en.ComboDisplay($v) -cne $v) { Fail "English display of '$v' changed" }
        if ($tw.ComboDisplay($v) -ceq $v) { Fail "zh-TW display of '$v' is not translated" }
        if ($tw.ComboValue($tw.ComboDisplay($v)) -cne $v) { Fail "zh-TW display of '$v' does not map back to the protocol value" }
        if ($en.ComboValue($v) -cne $v) { Fail "English value of '$v' changed" }
    }
    foreach ($v in 'continuous', 'revolute', 'prismatic', 'fixed', 'Origin_global', 'Link 1', 'base_link') {
        if ($tw.ComboDisplay($v) -cne $v -or $tw.ComboValue($v) -cne $v) { Fail "'$v' must never be translated or remapped" }
    }
    if ([Threading.Thread]::CurrentThread.CurrentCulture.Name -ne $cultureBefore) { Fail 'thread culture changed' }
}

if ($failures.Count) { $failures | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
Write-Output 'PASS: PropertyManager text goes through the catalog; protocol values stay English'
