# Installer check: the Traditional Chinese satellite assembly must be installed in a
# zh-TW subdirectory beside SW2URDF.dll, otherwise the add-in silently shows English.
# Reads the installer script and project file; needs no build and no SOLIDWORKS.
param([string]$Root = "$PSScriptRoot/..")
$ErrorActionPreference = 'Stop'
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw "FAIL: $message" } }

$iss = Get-Content -LiteralPath (Join-Path $Root 'INSTALL/Install.iss') -Encoding UTF8
$files = @($iss | Where-Object { $_ -match '^\s*Source:' })
$main = @($files | Where-Object { $_ -match 'DestDir:\s*\{app\};' })
$satellite = @($files | Where-Object { $_ -match 'Source:\s*x64\\Debug\\zh-TW\\\*\.resources\.dll' -and $_ -match 'DestDir:\s*\{app\}\\zh-TW[;\s]' })
Require ($main.Count -ge 1) 'installer has no main file entry'
Require ($satellite.Count -eq 1) 'installer does not install zh-TW\*.resources.dll into {app}\zh-TW'
Require ($satellite[0] -match 'Check:\s*IsWin64') 'satellite entry must use the same 64-bit check as the main entry'

$project = Get-Content -LiteralPath (Join-Path $Root 'SW2URDF/SW2URDF.csproj') -Raw -Encoding UTF8
Require ($project -match '<EmbeddedResource Include="UI\\Strings\.resx"') 'Strings.resx is not embedded'
Require ($project -match '<EmbeddedResource Include="UI\\Strings\.zh-TW\.resx"') 'Strings.zh-TW.resx is not embedded'
Write-Output 'PASS: installer ships the zh-TW satellite; both resource files are compiled'
