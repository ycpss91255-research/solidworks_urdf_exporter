# Offline vertical-spacing check for the export forms. Does not connect to SOLIDWORKS or register COM.
# test_form_layout.ps1 finds overlaps; this one finds controls that do not overlap but sit too close:
#   - two edit boxes stacked in one column must keep a gap of round(6 * dpi / 96) pixels,
#   - the last row inside a group box must keep round(10 * dpi / 96) pixels above the box edge,
#   - a caption that shares a row with an edit box is centred on it (within 2 pixels).
# Each form is built with only the designer-generated InitializeComponent, in English and in the
# translated language, for several fonts and display scales (scaled: font and controls enlarged
# together; unscaled: 100% layout with the DPI raised, as in a DPI-aware host), then the same layout
# calls the form constructors make are applied. -NoSpacing skips the row spacing step, which shows the
# problems the step removes. Pure PowerShell: only signed scripts and signed assemblies run.
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$Language = 'chinese',
    [string]$ScreenshotDirectory = '',
    [switch]$NoSpacing
)
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$uiType = $assembly.GetType('SW2URDF.UI.UiText', $true)
$layoutType = $assembly.GetType('SW2URDF.UI.UiLayout', $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$static = [Reflection.BindingFlags]'Static,NonPublic,Public'

function New-Form([Type]$type, [System.Drawing.Font]$font, $ui, [double]$dpi) {
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
    [void]$layoutType.GetMethod('FitButtonHeights', $static).Invoke($null, @($form, [single]$dpi))
    if (-not $NoSpacing) {
        $space = $layoutType.GetMethod('SpaceRows', $static)
        if (-not $space) { throw 'UiLayout.SpaceRows does not exist.' }
        [void]$space.Invoke($null, @($form, [single]$dpi))
    }
    return $form
}

function Test-Field([System.Windows.Forms.Control]$c) {
    return ($c -is [System.Windows.Forms.TextBox]) -or ($c -is [System.Windows.Forms.ComboBox]) -or ($c -is [System.Windows.Forms.UpDownBase])
}

function Test-Container([System.Windows.Forms.Control]$parent, [string]$path, [double]$dpi, $problems) {
    $gap = [int][Math]::Round(6 * $dpi / 96)
    $pad = [int][Math]::Round(10 * $dpi / 96)
    $kids = @($parent.Controls | Where-Object { $_.Dock -eq 'None' })
    $fields = @($kids | Where-Object { Test-Field $_ })
    foreach ($a in $fields) {
        foreach ($b in $fields) {
            if ($a -eq $b -or $b.Top -lt $a.Top) { continue }
            $sharesColumn = ($a.Left -lt $b.Right) -and ($b.Left -lt $a.Right)
            $below = $b.Top - $a.Bottom
            if ($sharesColumn -and $b.Top -gt $a.Top -and $below -lt $gap -and $below -ge -1) {
                [void]$problems.Add("gap ${path}: $($a.Name) to $($b.Name) is ${below}px, needs ${gap}px")
            }
        }
    }
    foreach ($l in @($kids | Where-Object { $_ -is [System.Windows.Forms.Label] })) {
        # The edit box beside the caption: the one whose centre is nearest to the caption's centre.
        $mid = $l.Top + $l.Height / 2
        $beside = $fields | Where-Object { $l.Height -le $_.Height + 2 -and $mid -ge $_.Top -and $mid -lt $_.Bottom -and [Math]::Max($l.Left - $_.Right, $_.Left - $l.Right) -ge 0 -and [Math]::Max($l.Left - $_.Right, $_.Left - $l.Right) -le $l.Height * 2.5 } |
            Sort-Object { [Math]::Abs(($_.Top + $_.Height / 2) - $mid) } | Select-Object -First 1
        if ($beside) {
            $off = [Math]::Abs($mid - ($beside.Top + $beside.Height / 2))
            if ($off -gt 1) { [void]$problems.Add("label ${path}: $($l.Name) is ${off}px off the centre of $($beside.Name)") }
        }
    }
    if ($parent -is [System.Windows.Forms.GroupBox] -and $kids.Count -gt 0) {
        $bottom = ($kids | ForEach-Object { $_.Bottom } | Measure-Object -Maximum).Maximum
        $room = $parent.ClientSize.Height - $bottom
        if ($room -lt $pad) { [void]$problems.Add("bottom ${path}: $($parent.Name) leaves ${room}px under its last row, needs ${pad}px") }
    }
    foreach ($c in $parent.Controls) {
        if ($c.Controls.Count -gt 0 -and -not ($c -is [System.Windows.Forms.TreeView]) -and -not (Test-Field $c) -and -not ($c -is [System.Windows.Forms.FlowLayoutPanel])) {
            Test-Container $c "$path/$($c.Name)" $dpi $problems
        }
    }
}

$formTypes = @('SW2URDF.UI.AssemblyExportForm', 'SW2URDF.UI.PartExportForm')
$fonts = @(@('Microsoft Sans Serif', 8.25), @('Microsoft JhengHei UI', 9), @('PMingLiU', 9))
$scenarios = @(@(1.0, 'scaled'), @(1.25, 'scaled'), @(1.5, 'scaled'), @(1.75, 'scaled'), @(2.0, 'scaled'),
    @(1.0, 'unscaled'), @(1.25, 'unscaled'), @(1.5, 'unscaled'), @(1.75, 'unscaled'), @(2.0, 'unscaled'))
$translated = [Activator]::CreateInstance($uiType, @(, [string]$Language))
$report = New-Object System.Collections.ArrayList
foreach ($typeName in $formTypes) {
    $type = $assembly.GetType($typeName, $true)
    foreach ($f in $fonts) {
        foreach ($sc in $scenarios) {
            $scale = $sc[0]; $mode = $sc[1]; $dpi = 96 * $scale
            foreach ($lang in @('en', $Language)) {
                $fontScale = if ($mode -eq 'scaled') { $scale } else { 1.0 }
                $font = New-Object System.Drawing.Font($f[0], [single]($f[1] * $fontScale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
                $form = New-Form $type $font $(if ($lang -eq 'en') { $null } else { $translated }) $dpi
                try {
                    $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-32000, -32000)
                    $form.ShowInTaskbar = $false; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
                    $problems = New-Object System.Collections.ArrayList
                    Test-Container $form $type.Name $dpi $problems
                    foreach ($p in $problems) { [void]$report.Add("$($type.Name) [$($f[0]) $($f[1])pt x$scale $mode $lang] : $p") }
                    if ($ScreenshotDirectory) {
                        $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
                        $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)))
                        $bmp.Save((Join-Path $ScreenshotDirectory ("{0}_{1}_{2}{3}_{4}.png" -f $type.Name, ($f[0] -replace ' ', ''), $scale, $mode, $lang)), [System.Drawing.Imaging.ImageFormat]::Png)
                        $bmp.Dispose()
                    }
                } finally { $form.Dispose(); $font.Dispose() }
            }
        }
    }
}
$report | Select-Object -Unique | ForEach-Object { Write-Output $_ }
Write-Output "Spacing problems: $($report.Count)"
if ($report.Count -gt 0) { exit 1 }
