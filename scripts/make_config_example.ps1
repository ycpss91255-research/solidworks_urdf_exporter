<#
.SYNOPSIS
  Regenerates the per-configuration example in examples/TWO_BOXES (see examples/TWO_BOXES/README.md).

.DESCRIPTION
  1. Copies TWO_BOXES.SLDASM and its parts to a scratch folder (as TWO_BOXES_PER_CONFIG.SLDASM) and, with the
     exporter's own code, stores a different URDF link tree for each of its SolidWorks configurations.
  2. Exports every configuration from that stored tree to a URDF package (STL meshes).
  3. Copies the saved assembly and the two packages to the output folder, minus export.log, and replaces the
     version comment in the URDF header so that the files carry no build information.

  Works on copies only; the examples that are already in the repository are not modified. It binds to the ONE
  SolidWorks process you name with -SolidWorksPid (Running Object Table) and never starts or closes SolidWorks.

  Everything that is compiled or loaded must be Authenticode-signed on machines that require it: pass the
  thumbprint of your code-signing certificate (CurrentUser\My) as -SigningThumbprint and the helper
  executable is signed, and SW2URDF.dll in -BuildDirectory must carry a valid signature from the same
  certificate, or the script stops before running anything. Without -SigningThumbprint nothing is signed or
  checked. This script itself must be signed too if your execution policy asks for it.

.PARAMETER SolidWorksPid
  Process id of the SolidWorks instance to use.
.PARAMETER BuildDirectory
  Folder with a built SW2URDF.dll and its dependencies (log4net.dll, MathNet.Numerics.dll, ...).
.PARAMETER WorkDirectory
  Scratch folder, created and removed by this script. SolidWorks can record the folder path inside the files it
  saves, so use a neutral path (the default is short and contains no user name).
.PARAMETER OutputDirectory
  Where the results go. Default: examples/TWO_BOXES.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$SolidWorksPid,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [string]$SigningThumbprint = '',
    [string]$WorkDirectory = 'C:\sw2urdf_config_example',
    [string]$OutputDirectory = '',
    [string]$SolidWorksDirectory = (Join-Path $env:ProgramW6432 'SOLIDWORKS Corp\SOLIDWORKS')
)
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'examples\TWO_BOXES' }
$source = Join-Path $repoRoot 'examples\TWO_BOXES'
$BuildDirectory = (Resolve-Path -LiteralPath $BuildDirectory).Path
$configurations = 'CFG_BOTH', 'CFG_RED_ONLY'
$assemblyName = 'TWO_BOXES_PER_CONFIG'

function Assert-Signed([string]$path) {
    if (-not $SigningThumbprint) { return }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $SigningThumbprint) {
        throw "$path is not validly signed by the certificate $SigningThumbprint"
    }
}

function Invoke-Tool {
    param([Parameter(Mandatory, Position = 0)][string]$what, [Parameter(ValueFromRemainingArguments)][string[]]$toolArguments)
    # The exporter's logger writes warnings to stderr; they must not abort the script, the exit code decides.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & "$tool\ConfigExampleTool.exe" @toolArguments 2>&1 | Where-Object { $_ -notmatch '^log4net:' } | Out-Host
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "$what failed (exit code $code)" }
}

Assert-Signed (Join-Path $BuildDirectory 'SW2URDF.dll')

if (Test-Path -LiteralPath $WorkDirectory) { throw "$WorkDirectory already exists; remove it or choose another -WorkDirectory" }
$tool = Join-Path $WorkDirectory 'tool'
$save = Join-Path $WorkDirectory 'save'
$out = Join-Path $WorkDirectory 'out'
New-Item -ItemType Directory -Path $tool, $save, $out | Out-Null
try {
    # Helper executable (C# 5 compiler of the .NET Framework, no extra installation needed)
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    foreach ($name in 'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll') {
        Copy-Item -LiteralPath (Join-Path $SolidWorksDirectory $name) -Destination $tool
    }
    $references = @('System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Runtime.Serialization.dll',
        (Join-Path $BuildDirectory 'SW2URDF.dll'), (Join-Path $tool 'SolidWorks.Interop.sldworks.dll'),
        (Join-Path $tool 'SolidWorks.Interop.swconst.dll'), (Join-Path $framework 'WPF\WindowsBase.dll'),
        (Join-Path $framework 'WPF\PresentationFramework.dll'))
    $arguments = @('/nologo', '/target:exe', '/platform:x64', "/out:$tool\ConfigExampleTool.exe") +
        ($references | ForEach-Object { "/r:$_" }) + (Join-Path $PSScriptRoot 'ConfigExampleTool.cs')
    & (Join-Path $framework 'csc.exe') @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Compiling ConfigExampleTool.cs failed' }
    if ($SigningThumbprint) {
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
        $null = Set-AuthenticodeSignature -LiteralPath "$tool\ConfigExampleTool.exe" -Certificate $certificate -HashAlgorithm SHA256
    }
    Assert-Signed "$tool\ConfigExampleTool.exe"
    # The helper loads SW2URDF.dll and its dependencies from its own folder.
    Get-ChildItem -LiteralPath $BuildDirectory -Filter *.dll | Where-Object { $_.Name -notlike 'SolidWorks.Interop*' } |
        Copy-Item -Destination $tool

    # Saved assembly: copies of the parts and of the original assembly under its new name
    Copy-Item -LiteralPath (Join-Path $source 'BOX_RED.SLDPRT'), (Join-Path $source 'BOX_BLUE.SLDPRT') -Destination $save
    Copy-Item -LiteralPath (Join-Path $source 'TWO_BOXES.SLDASM') -Destination (Join-Path $save "$assemblyName.SLDASM")
    Invoke-Tool 'Saving the per-configuration link trees' save --pid $SolidWorksPid --assembly "$save\$assemblyName.SLDASM"

    # One export per configuration, each from a fresh copy (the export adds an Origin_global coordinate system)
    foreach ($configuration in $configurations) {
        $copy = Join-Path $WorkDirectory "export_$configuration"
        New-Item -ItemType Directory -Path $copy | Out-Null
        Copy-Item -Path "$save\*" -Destination $copy
        Invoke-Tool "Exporting $configuration" export --pid $SolidWorksPid --assembly "$copy\$assemblyName.SLDASM" `
            --config $configuration --out $out
    }

    # Publish: assembly + packages without the log, version comment normalised
    Copy-Item -LiteralPath "$save\$assemblyName.SLDASM" -Destination $OutputDirectory -Force
    foreach ($configuration in $configurations) {
        $package = "${assemblyName}_description_$configuration"
        Remove-Item -LiteralPath "$out\$package\export.log" -ErrorAction SilentlyContinue
        $urdf = "$out\$package\urdf\$package.urdf"
        $text = [IO.File]::ReadAllText($urdf)
        $text = $text -replace 'Commit Version: \S+\s+Build Version: \S+', 'Commit Version: n/a  Build Version: n/a'
        [IO.File]::WriteAllText($urdf, $text, (New-Object Text.UTF8Encoding($false)))
        $target = Join-Path $OutputDirectory $package
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Copy-Item -LiteralPath "$out\$package" -Destination $target -Recurse
    }
    Write-Output "Done. Results are in $OutputDirectory"
}
finally {
    Remove-Item -LiteralPath $WorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
