# Offline layout check for translated forms. Does not connect to SOLIDWORKS or register COM.
# Each export form is built without its constructor (which needs SOLIDWORKS): only the
# designer-generated InitializeComponent runs. The form is then translated exactly as the
# constructor does (UiText.Apply followed by AlignFieldLabels when it exists) and compared
# with the untranslated English form at the same font and display scaling. Translation must
# not introduce any new
#   - overlap between sibling controls,
#   - control extending past its container,
#   - fixed-size label whose text no longer fits.
# Problems that exist in English already belong to the form layout, not to the translation,
# and are reported as a count only. Scaling is simulated by enlarging the form font, which is
# what AutoScaleMode.Font reacts to (100%, 125%, 150%).
# Pure PowerShell: only signed scripts and signed assemblies run.
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$Language = 'chinese',
    [string]$ScreenshotDirectory = ''
)
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$uiType = $assembly.GetType('SW2URDF.UI.UiText', $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'

function New-Form([Type]$type, [System.Drawing.Font]$font, $ui) {
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
    [void][System.Windows.Forms.Form].GetConstructor($flags, $null, [Type[]]@(), $null).Invoke($form, @())
    if ($font) { $form.Font = $font }
    try { [void]$type.GetMethod('InitializeComponent', $flags).Invoke($form, @()) } catch { throw $_.Exception.InnerException }
    $events = [System.ComponentModel.Component].GetProperty('Events', $flags).GetValue($form, @())
    $loadKey = [System.Windows.Forms.Form].GetField('EVENT_LOAD', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $events.RemoveHandler($loadKey, $events[$loadKey])
    if ($ui) {
        $field = $type.GetField('ui', $flags)
        if ($field) { $field.SetValue($form, $ui) }
        $ui.Apply($form)
        $align = $type.GetMethod('AlignFieldLabels', $flags)
        if ($align) { [void]$align.Invoke($form, @()) }
    }
    return $form
}

# Control.Visible is false while the form is not shown; read the control's own flag.
$getState = [System.Windows.Forms.Control].GetMethod('GetState', $flags, $null, [Type[]]@([int]), $null)
function Test-Shown([System.Windows.Forms.Control]$c) { return [bool]$getState.Invoke($c, @(2)) }

$measureFormat = [System.Windows.Forms.TextFormatFlags]::WordBreak -bor [System.Windows.Forms.TextFormatFlags]::NoPadding
# A fixed-size, top-left aligned label only occupies the space its text needs.
function Get-Box([System.Windows.Forms.Control]$c) {
    $b = $c.Bounds
    if (($c -is [System.Windows.Forms.Label]) -and -not $c.AutoSize -and $c.TextAlign -eq 'TopLeft' -and -not [string]::IsNullOrEmpty($c.Text)) {
        $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size($c.Width, 0)), $measureFormat)
        return New-Object System.Drawing.Rectangle($b.X, $b.Y, [Math]::Min($b.Width, $need.Width), [Math]::Min($b.Height, $need.Height))
    }
    return $b
}
function Test-TextClipped([System.Windows.Forms.Control]$c) {
    if (-not ($c -is [System.Windows.Forms.Label]) -or $c.AutoSize -or [string]::IsNullOrEmpty($c.Text)) { return $false }
    $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size($c.Width, 0)), $measureFormat)
    return ($need.Height -gt $c.Height + 1)
}

# Problems are signatures made of control names only, so English and translated forms compare.
function Test-Container([System.Windows.Forms.Control]$parent, [string]$path, $problems) {
    $kids = @($parent.Controls | Where-Object { Test-Shown $_ })
    $client = $parent.ClientSize
    foreach ($c in $kids) {
        $b = Get-Box $c
        if (-not $parent.AutoScroll -and ($b.Right -gt $client.Width + 1 -or $b.Bottom -gt $client.Height + 1 -or $b.Left -lt -1 -or $b.Top -lt -1)) {
            [void]$problems.Add("outside|$path|$($c.Name)")
        }
        if (Test-TextClipped $c) { [void]$problems.Add("clipped|$path|$($c.Name)") }
    }
    for ($i = 0; $i -lt $kids.Count; $i++) {
        for ($j = $i + 1; $j -lt $kids.Count; $j++) {
            $a = Get-Box $kids[$i]; $b = Get-Box $kids[$j]
            $x1 = [System.Drawing.Rectangle]::Intersect($a, $kids[$j].Bounds)
            $x2 = [System.Drawing.Rectangle]::Intersect($kids[$i].Bounds, $b)
            if (($x1.Width -gt 1 -and $x1.Height -gt 1) -or ($x2.Width -gt 1 -and $x2.Height -gt 1)) {
                [void]$problems.Add("overlap|$path|$($kids[$i].Name)|$($kids[$j].Name)")
            }
        }
    }
    foreach ($c in $kids) {
        if ($c.Controls.Count -gt 0 -and -not ($c -is [System.Windows.Forms.TreeView]) -and -not ($c -is [System.Windows.Forms.NumericUpDown]) -and -not ($c -is [System.Windows.Forms.DomainUpDown]) -and -not ($c -is [System.Windows.Forms.ComboBox])) {
            Test-Container $c "$path/$($c.Name)" $problems
        }
    }
}

# Each entry is a form page: the controls that are on screen together.
function Get-Problems($form, [string]$tag) {
    $result = New-Object System.Collections.ArrayList
    $wasVisible = @{}; foreach ($c in $form.Controls) { $wasVisible[$c.Name] = (Test-Shown $c) }
    $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-32000, -32000)
    $form.ShowInTaskbar = $false; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
    $panel = $form.Controls.Find('panelLinkProperties', $false)
    $pages = @()
    if ($panel.Count -eq 1) { $pages += ,@('joint page', $form, $panel[0]); $pages += ,@('link page', $panel[0], $panel[0]) }
    else { $pages += ,@('form', $form, $null) }
    foreach ($page in $pages) {
        if ($page[0] -eq 'joint page') { $page[2].Visible = $false; foreach ($c in $form.Controls) { if ($c -ne $page[2] -and $wasVisible[$c.Name]) { $c.Visible = $true } } }
        if ($page[0] -eq 'link page') { $page[2].Visible = $true; foreach ($c in $form.Controls) { if ($c -ne $page[2]) { $c.Visible = $false } } }
        [System.Windows.Forms.Application]::DoEvents()
        $list = New-Object System.Collections.ArrayList
        Test-Container $page[1] $page[0] $list
        foreach ($p in $list) { [void]$result.Add($p) }
        if ($ScreenshotDirectory) {
            $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
            $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)))
            $bmp.Save((Join-Path $ScreenshotDirectory ("{0}_{1}.png" -f $tag, ($page[0] -replace ' ', ''))), [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
        }
    }
    return ,$result
}

$formTypes = @('SW2URDF.UI.AssemblyExportForm', 'SW2URDF.UI.PartExportForm')
$fonts = @(@('Microsoft Sans Serif', 8.25), @('Microsoft JhengHei UI', 9))
$scales = @(1.0, 1.25, 1.5)
$translated = [Activator]::CreateInstance($uiType, @(, [string]$Language))
$newProblems = New-Object System.Collections.ArrayList
$baselineCount = 0; $checked = 0
foreach ($typeName in $formTypes) {
    $type = $assembly.GetType($typeName, $true)
    foreach ($f in $fonts) {
        foreach ($scale in $scales) {
            $font = New-Object System.Drawing.Font($f[0], [single]($f[1] * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
            $tag = "{0}_{1}_x{2}_{3}" -f $type.Name, ($f[0] -replace ' ', ''), $scale, $Language
            $english = New-Form $type $font $null
            try { $en = Get-Problems $english ($tag -replace "_$Language$", '_en') } finally { $english.Dispose() }
            $zh = New-Form $type $font $translated
            try { $tr = Get-Problems $zh $tag } finally { $zh.Dispose() }
            $font.Dispose()
            $baselineCount += $en.Count; $checked++
            $known = New-Object System.Collections.Generic.HashSet[string]
            foreach ($p in $en) { [void]$known.Add($p) }
            foreach ($p in $tr) { if (-not $known.Contains($p)) { [void]$newProblems.Add("$($type.Name) [$($f[0]) $($f[1])pt x$scale] $p") } }
        }
    }
}
$newProblems | Sort-Object -Unique | ForEach-Object { Write-Output "NEW: $_" }
Write-Output "Checked $checked form/font/scale combinations; layout problems already present in English: $baselineCount; introduced by translation: $($newProblems.Count)"
if ($newProblems.Count -gt 0) { exit 1 }
Write-Output 'PASS: translation introduces no overlap, out-of-container control or clipped label'
