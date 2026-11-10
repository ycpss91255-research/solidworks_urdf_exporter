# Offline resource-catalog check for the interface languages. Needs no SOLIDWORKS and
# no build: it reads the .resx files and the form sources directly.
param([string]$Root = "$PSScriptRoot/..")
$ErrorActionPreference = 'Stop'
$ui = Join-Path $Root 'SW2URDF/UI'
$failures = New-Object System.Collections.ArrayList
function Fail([string]$message) { [void]$failures.Add($message) }

function Read-Catalog([string]$name) {
    $path = Join-Path $ui $name
    if (-not (Test-Path -LiteralPath $path)) { Fail "missing resource file $name"; return $null }
    $values = [ordered]@{}
    foreach ($entry in ([xml](Get-Content -LiteralPath $path -Raw -Encoding UTF8)).root.data) {
        $key = [string]$entry.name
        if ($values.Contains($key)) { Fail "$name duplicate key '$key'" }
        $values[$key] = [string]$entry.value
    }
    # ResGen compares keys case-insensitively, so keys differing only by case are duplicates.
    $values.Keys | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1 |
        ForEach-Object { Fail "$name keys differ only by case: $($_.Group -join ' | ')" }
    $values.Keys | Where-Object { $_ -cne $_.ToLowerInvariant() } |
        ForEach-Object { Fail "$name key '$_' is not lowercase" }
    $values.Keys | Where-Object { [string]::IsNullOrWhiteSpace($values[$_]) } |
        ForEach-Object { Fail "$name empty value for '$_'" }
    return $values
}

$english = Read-Catalog 'Strings.resx'
$chinese = Read-Catalog 'Strings.zh-TW.resx'
if ($english -and $chinese) {
    foreach ($k in $english.Keys) { if (-not $chinese.Contains($k)) { Fail "zh-TW lacks key '$k'" } }
    foreach ($k in $chinese.Keys) { if (-not $english.Contains($k)) { Fail "English lacks key '$k'" } }
    foreach ($k in $english.Keys) {
        if ($chinese.Contains($k)) {
            $a = ([regex]::Matches($english[$k], '\{\d+\}') | ForEach-Object Value | Sort-Object) -join ','
            $b = ([regex]::Matches($chinese[$k], '\{\d+\}') | ForEach-Object Value | Sort-Object) -join ','
            if ($a -ne $b) { Fail "placeholders differ for '$k'" }
        }
    }
    # Static captions of both forms must be translated unless they are symbols or protocol names.
    $literal = '"((?:[^"\\]|\\.)*)"'
    $invariant = @('*', '1', 'x', 'y', 'z', 'ixx', 'ixy', 'ixz', 'iyy', 'iyz', 'izz', 'ROS 1', 'ROS 2', 'pos = multiplier * pos_other + offset')
    foreach ($form in 'AssemblyExportForm', 'PartExportForm') {
        $src = Get-Content -LiteralPath (Join-Path $ui "$form.Designer.cs") -Raw -Encoding UTF8
        foreach ($m in [regex]::Matches($src, '\bText\s*=\s*(' + $literal + '(?:\s*\+\s*' + $literal + ')*)')) {
            $caption = (([regex]::Matches($m.Groups[1].Value, $literal) | ForEach-Object { $_.Groups[1].Value }) -join '').Replace("\'", "'")
            if ($caption -and ($invariant -cnotcontains $caption) -and -not $chinese.Contains($caption.ToLowerInvariant())) {
                Fail "$form caption has no translation: '$caption'"
            }
        }
    }
    # Every caption the code looks up needs a resource, and every resource must be looked
    # up or shown somewhere in this tree (no orphans from strings that live in other branches).
    $used = New-Object System.Collections.Generic.HashSet[string]
    foreach ($file in Get-ChildItem -Path (Join-Path $Root 'SW2URDF') -Recurse -Filter *.cs) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        foreach ($m in [regex]::Matches($text, '(?:\bui\.|(?<![\w.]))Get\(' + $literal + '\)')) {
            $key = $m.Groups[1].Value.Replace('\"', '"').Replace('\n', "`n").Replace('\r', "`r").Replace('\\', '\').ToLowerInvariant()
            [void]$used.Add($key)
            if (-not $chinese.Contains($key)) { Fail "$($file.Name) uses untranslated '$($m.Groups[1].Value)'" }
        }
        if ($file.Name -match '^(Assembly|Part)ExportForm\.Designer\.cs$') {
            foreach ($m in [regex]::Matches($text, '\bText\s*=\s*(' + $literal + '(?:\s*\+\s*' + $literal + ')*)')) {
                $caption = (([regex]::Matches($m.Groups[1].Value, $literal) | ForEach-Object { $_.Groups[1].Value }) -join '').Replace("\'", "'")
                [void]$used.Add($caption.ToLowerInvariant())
            }
        }
    }
    foreach ($k in $chinese.Keys) { if (-not $used.Contains($k)) { Fail "resource '$k' is not used by any caption or ui.Get call" } }
}
if ($failures.Count) { $failures | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
Write-Output "PASS: catalogs match ($($english.Count) keys), no case-only duplicates, placeholders and form captions covered"
