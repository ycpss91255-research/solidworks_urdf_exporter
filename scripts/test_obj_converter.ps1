<#
.SYNOPSIS
    Offline tests for the C# 3DXML -> OBJ/MTL converter.
.DESCRIPTION
    Compiles the converter sources in memory together with scripts/ObjConverterTests.cs, so no
    build output, SOLIDWORKS session or COM registration is needed. Writes only below -WorkDirectory.
    With -PythonExecutable (Python with trimesh and numpy) it also compares every triangle with the
    independent trimesh reading of the same synthetic 3DXML through scripts/verify_obj.py.
#>
[CmdletBinding()]
param(
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('obj-converter-tests-' + [guid]::NewGuid().ToString('N'))),
    [string]$PythonExecutable = '',
    [string]$NodeExecutable = '',
    [string]$ThreeDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -File $PSCommandPath -WorkDirectory $WorkDirectory -PythonExecutable $PythonExecutable `
        -NodeExecutable $NodeExecutable -ThreeDirectory $ThreeDirectory
    exit $LASTEXITCODE
}
$repo = Split-Path -Parent $PSScriptRoot
$sources = @(
    (Join-Path $repo 'SW2URDF\URDFExport\ThreeDxmlReader.cs'),
    (Join-Path $repo 'SW2URDF\URDFExport\ObjConverter.cs'),
    (Join-Path $PSScriptRoot 'ObjConverterTests.cs')
)
$code = ($sources | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
Add-Type -Path $sources -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')
[ObjConverterTests]::Run($WorkDirectory)

if ($PythonExecutable) {
    $reference = Join-Path $WorkDirectory 'reference'
    $report = Join-Path $reference 'validation.json'
    & $PythonExecutable -I (Join-Path $PSScriptRoot 'verify_obj.py') `
        (Join-Path $reference 'reference.3dxml') (Join-Path $reference 'reference.obj') $report
    if ($LASTEXITCODE -ne 0) { throw 'verify_obj.py reported a mismatch with the trimesh reference.' }
    Write-Host 'PASS: OBJ triangles match the independent trimesh reading of the 3DXML'
}
else {
    Write-Host 'SKIPPED: trimesh comparison (pass -PythonExecutable to run scripts/verify_obj.py)'
}

if ($NodeExecutable -and $ThreeDirectory) {
    $reference = Join-Path $WorkDirectory 'reference'
    $geometry = Join-Path $reference 'three-geometry.json'
    [IO.File]::WriteAllText($geometry, '{"triangles":36,"materials":2}')
    & $NodeExecutable (Join-Path $PSScriptRoot 'verify_obj_three.mjs') $ThreeDirectory `
        (Join-Path $reference 'reference.obj') $geometry (Join-Path $reference 'three.json')
    if ($LASTEXITCODE -ne 0) { throw 'Three.js OBJLoader/MTLLoader result differs from the MTL.' }
    Write-Host 'PASS: Three.js OBJLoader + MTLLoader read every material field as written'
}
else {
    Write-Host 'SKIPPED: Three.js comparison (pass -NodeExecutable and -ThreeDirectory)'
}
