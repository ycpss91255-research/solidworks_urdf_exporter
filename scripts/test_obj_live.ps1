# Native CLI integration test. Requires an explicitly owned, already running PID.
param(
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [Parameter(Mandatory=$true)][string]$ModelsDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('none','part','assembly')][string]$Form='none'
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$build=Join-Path $repo 'SW2URDF/bin/x64/Debug'
$swdir=Join-Path $env:ProgramFiles 'SOLIDWORKS Corp/SOLIDWORKS'
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$models=(Resolve-Path -LiteralPath $ModelsDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
foreach($name in @('SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll')) {
    Copy-Item -LiteralPath (Join-Path $swdir $name) -Destination $build
}
$references=@('System.Windows.Forms.dll','System.Drawing.dll','System.Runtime.Serialization.dll',
    (Join-Path $build 'SW2URDF.dll'),(Join-Path $swdir 'SolidWorks.Interop.sldworks.dll'),
    (Join-Path $swdir 'SolidWorks.Interop.swconst.dll'),(Join-Path $framework 'WPF/WindowsBase.dll'),
    (Join-Path $framework 'WPF/PresentationFramework.dll'))
$program=if($Form -eq 'none'){'ObjExportCli'}else{'ObjLiveProbe'}
$arguments=@('/nologo','/target:exe','/platform:x64',"/out:$build/$program.exe")
$arguments+=@($references|ForEach-Object {"/r:$_"})
$arguments+=(Join-Path $PSScriptRoot "$program.cs")
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'CLI compilation failed'}
if($Form -eq 'none') { & (Join-Path $build "$program.exe") $TargetPid $models $output }
else { & (Join-Path $build "$program.exe") $TargetPid $Form $models $output }
exit $LASTEXITCODE
