# Tools-menu text check. Needs no SOLIDWORKS.
#  1. The menu path and hint strings follow the language (UiText).
#  2. SwAddin registers and removes the menu items with the same localized path; the old
#     code removed "...@&File" which never matched the "...@&Tools" item it had added.
# Pure PowerShell: only signed scripts and signed assemblies run.
param(
    [string]$Root = "$PSScriptRoot/..",
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll"
)
$ErrorActionPreference = 'Stop'
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw "FAIL: $message" } }

$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path -Parent $AssemblyPath) 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$uiType = $assembly.GetType('SW2URDF.UI.UiText', $true)
function New-Ui($language) { [Activator]::CreateInstance($uiType, @(, [string]$language)) }

$en = New-Ui 'english'
$zh = New-Ui 'chinese'
Require ($en.ExportMenuPath -ceq 'Export as URDF@&Tools') 'English menu path'
Require ($zh.ExportMenuPath -cne $en.ExportMenuPath) 'Traditional Chinese menu path is translated'
Require ($zh.ExportMenuPath.EndsWith('@&Tools', [StringComparison]::Ordinal)) 'menu parent stays the Tools menu'
Require ($zh.ExportMenuPath.Split('@').Count -eq 2) 'menu caption contains no extra @'
Require ($en.ExportAssemblyHint -ceq 'Export assembly as URDF file' -and $en.ExportPartHint -ceq 'Export part as URDF file') 'English hints'
Require ($zh.ExportAssemblyHint -cne $en.ExportAssemblyHint -and $zh.ExportPartHint -cne $en.ExportPartHint) 'hints are translated'
foreach ($language in @($null, '', 'french', 'chinese-simplified')) {
    Require ((New-Ui $language).ExportMenuPath -ceq $en.ExportMenuPath) "menu falls back to English for '$language'"
}

$source = Get-Content -LiteralPath (Join-Path $Root 'SW2URDF/SW/SwAddin.cs') -Raw -Encoding UTF8
Require ($source -notmatch '"Export as URDF@') 'SwAddin hard-codes the menu caption'
Require ($source -notmatch '"Export (assembly|part) as URDF file"') 'SwAddin hard-codes a menu hint'
$adds = [regex]::Matches($source, 'AddMenuItem5\([^;]*;')
$removes = [regex]::Matches($source, 'RemoveMenu\([^;]*;')
Require ($adds.Count -eq 2 -and $removes.Count -eq 2) "expected two AddMenuItem5 and two RemoveMenu calls, found $($adds.Count)/$($removes.Count)"
foreach ($call in @($adds) + @($removes)) {
    Require ($call.Value -match 'ui\.ExportMenuPath') "menu call does not use ui.ExportMenuPath: $($call.Value)"
}
$connect = $source.IndexOf('ui = UiText.FromSolidWorks(SwApp)')
Require ($connect -ge 0 -and $connect -lt $source.IndexOf('AddMenuItem5(')) 'language is read before the menu is registered'
Write-Output 'PASS: menu path and hints follow the language; add and remove use the same string'
