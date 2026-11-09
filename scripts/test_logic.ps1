<#
.SYNOPSIS
    Runs the xUnit [Fact] tests that need no SolidWorks (classes with a public
    parameterless constructor in namespace SW2URDF.Test) from a built SW2URDF.dll.
.DESCRIPTION
    Never starts, connects to or changes SolidWorks. Tests are invoked by reflection, so no
    xUnit runner is needed. Exit code is the number of failed tests (0 = all passed).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assembly = [Reflection.Assembly]::LoadFrom($resolved)
try { $types = $assembly.GetTypes() }
catch [Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ } }

$passed = 0
$failed = 0
foreach ($type in $types) {
    if ($type.Namespace -ne 'SW2URDF.Test' -or -not $type.IsPublic -or $type.IsAbstract) { continue }
    if ($type.GetConstructor([Type[]]@()) -eq $null) { continue }
    if ($Filter -and $type.Name -notlike "*$Filter*") { continue }
    $facts = $type.GetMethods() | Where-Object {
        $_.GetParameters().Count -eq 0 -and
        ($_.GetCustomAttributes($true) | Where-Object { $_.GetType().FullName -eq 'Xunit.FactAttribute' })
    }
    foreach ($method in $facts) {
        $name = $type.Name + '.' + $method.Name
        try {
            $instance = [Activator]::CreateInstance($type)
            [void]$method.Invoke($instance, @())
            $passed++
            Write-Output "PASS $name"
        }
        catch {
            $failed++
            $inner = $_.Exception
            while ($inner.InnerException) { $inner = $inner.InnerException }
            Write-Output "FAIL $name : $($inner.GetType().Name): $($inner.Message -replace '\s+', ' ')"
        }
    }
}
Write-Output "Logic tests: $passed passed, $failed failed."
exit $failed
