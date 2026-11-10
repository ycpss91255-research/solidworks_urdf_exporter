<#
.SYNOPSIS
    Build the exporter without registering it or changing SolidWorks documents.
.DESCRIPTION
    Uses the repository location rather than the current working directory.
    Stamps the local Git revision, then disables COM registration and post-build commands.
    Does not run the integration tests, which open and close SolidWorks documents.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64', 'AnyCPU')]
    [string]$Platform = 'x64',
    [string]$MSBuildPath = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$revision = & git -C $repoRoot rev-parse --short HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision.' }
& git -C $repoRoot diff --quiet HEAD
if ($LASTEXITCODE -eq 1) { $revision += '-dirty' }
elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source state.' }
$versionFile = Join-Path $repoRoot 'SW2URDF/Versioning/VersionInfo.cs'
$versionSource = 'using System.Reflection;' + "`r`n" + '[assembly: AssemblyInformationalVersion("' + $revision + '")]'
[IO.File]::WriteAllText($versionFile, $versionSource, [Text.UTF8Encoding]::new($false))
if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'vswhere.exe was not found. Specify -MSBuildPath explicitly.'
    }
    $MSBuildPath = & $vswhere -all -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) {
    throw 'MSBuild.exe was not found. Install the Visual Studio .NET desktop development workload.'
}

$buildArguments = @(
    (Join-Path $repoRoot 'SW2URDF\SW2URDF.csproj'),
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:SolutionDir=$repoRoot\",
    '/p:PreBuildEvent=',
    '/p:PostBuildEvent=',
    '/p:RegisterForComInterop=false',
    '/verbosity:minimal',
    '/nologo'
)
if ($OutputDirectory) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
    $buildArguments += "/p:OutputPath=$resolvedOutput\"
}
& $MSBuildPath @buildArguments
exit $LASTEXITCODE
