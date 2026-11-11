# Offline text-clipping check for the export forms. Does not connect to SOLIDWORKS or register COM.
# test_form_layout.ps1 and test_i18n_layout.ps1 look for overlaps and controls that leave their
# container. They cannot see a control that is inside its container but too short for its own
# text, which cuts the top and bottom off the characters (typical for the taller CJK fonts).
# This script builds each form with only the designer-generated InitializeComponent, in English
# and in the translated language, for several fonts and display-scaling factors (simulated by
# enlarging the form font, which is what AutoScaleMode.Font reacts to), and for every control
# that shows text compares the height the text needs with the height the control offers.
# A form inside SOLIDWORKS is not necessarily laid out at the screen scaling it is shown at: on a
# 150% display the process is DPI aware, the form keeps its 100% control sizes and the visual
# theme draws button faces with margins that grow with the DPI. Each combination is therefore run
# twice: 'scaled' (font and controls enlarged together) and 'unscaled' (100% layout, theme DPI
# raised). A push button needs its text height plus a theme margin of ceil(3 * dpi / 96) pixels
# on each side (measured on a real 150% SOLIDWORKS session: 16px text needs 25px, 21px clips).
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
    # Same call the form constructors make once the text is set (absent before the fix).
    $layoutType = $assembly.GetType('SW2URDF.UI.UiLayout')
    $fit = if ($layoutType) { $layoutType.GetMethod('FitButtonHeights', [Reflection.BindingFlags]'Static,NonPublic,Public') }
    if ($fit) { [void]$fit.Invoke($null, @($form, [single]$dpi)) }
    return $form
}

# Control.Visible is false while the form is not shown; read the control's own flag.
$getState = [System.Windows.Forms.Control].GetMethod('GetState', $flags, $null, [Type[]]@([int]), $null)
function Test-Shown([System.Windows.Forms.Control]$c) { return [bool]$getState.Invoke($c, @(2)) }

$textFormat = [System.Windows.Forms.TextFormatFlags]::WordBreak -bor [System.Windows.Forms.TextFormatFlags]::NoPadding
# Vertical room an edit box needs around its text (border).
$editBorder = 4

# Height of the ink extent (first to last pixel row with dark ink) in x range [x0, x1), skipping the
# outer border rows. Rows without ink inside a glyph do not matter, only where the text starts and ends.
function Get-InkRows([System.Drawing.Bitmap]$bmp, [int]$x0, [int]$x1) {
    $first = -1; $last = -1
    for ($y = 1; $y -lt $bmp.Height - 1; $y++) {
        for ($x = $x0; $x -lt $x1; $x++) {
            if ($bmp.GetPixel($x, $y).R -lt 140) { if ($first -lt 0) { $first = $y }; $last = $y; break }
        }
    }
    if ($first -lt 0) { return 0 }
    return $last - $first + 1
}

# Draws the caption of a push button and the same caption in an unclipped label of the same font
# next to it, and compares how many pixel rows of ink each shows. Returns @(label rows, button rows)
# when the button loses glyph rows (the top of the characters is cut off), otherwise $null.
# The theme clips a button caption to a rectangle smaller than the button, which neither the height
# model nor a comparison with the same text drawn by GDI can see: both lose the same rows.
# The label is the reference because a label draws the whole line box without a clip rectangle.
function Get-GlyphLoss([System.Windows.Forms.Button]$b) {
    if ([string]::IsNullOrEmpty($b.Text) -or $b.Width -lt 24 -or $b.Height -lt 8) { return $null }
    $w = [System.Windows.Forms.TextRenderer]::MeasureText($b.Text, $b.Font, (New-Object System.Drawing.Size(0, 0)), $textFormat)
    if ($w.Width -gt $b.Width - 12) { return $null }   # wraps or is cut at the sides: not a vertical question
    $label = New-Object System.Windows.Forms.Label
    $label.AutoSize = $false; $label.UseCompatibleTextRendering = $false
    $label.Font = $b.Font; $label.Text = $b.Text; $label.TextAlign = 'MiddleCenter'
    $label.Size = New-Object System.Drawing.Size($b.Width, ($b.Font.Height * 2 + 8))
    $label.Location = New-Object System.Drawing.Point(0, 0)
    $b.Parent.Controls.Add($label)
    try {
        $rect = New-Object System.Drawing.Rectangle(0, 0, $label.Width, $label.Height)
        $lb = New-Object System.Drawing.Bitmap($label.Width, $label.Height); $label.DrawToBitmap($lb, $rect)
        $bb = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
        $b.DrawToBitmap($bb, (New-Object System.Drawing.Rectangle(0, 0, $b.Width, $b.Height)))
        $x0 = 6; $x1 = $b.Width - 6
        $ref = Get-InkRows $lb $x0 $x1
        $got = Get-InkRows $bb $x0 $x1
        $lb.Dispose(); $bb.Dispose()
    } finally { $b.Parent.Controls.Remove($label); $label.Dispose() }
    # One row of difference is anti-aliasing and rounding between the two draw paths; the bug cut 2+ rows.
    if ($got -lt $ref - 1) { return ,@($ref, $got) }
    return $null
}

# Returns @(needed, actual) in pixels when the control cannot show its text, otherwise $null.
function Get-Clipping([System.Windows.Forms.Control]$c, [double]$dpi) {
    $line = [System.Windows.Forms.TextRenderer]::MeasureText('Ag', $c.Font, (New-Object System.Drawing.Size(0, 0)), [System.Windows.Forms.TextFormatFlags]::NoPadding).Height
    if ($c -is [System.Windows.Forms.TextBoxBase]) {
        if ($c.Multiline) { return $null }
        $needed = $line + $editBorder; $actual = $c.Height
    } elseif ($c -is [System.Windows.Forms.ComboBox] -or $c -is [System.Windows.Forms.UpDownBase]) {
        $needed = $line + $editBorder; $actual = $c.Height
    } elseif ($c -is [System.Windows.Forms.ButtonBase]) {
        if ([string]::IsNullOrEmpty($c.Text)) { return $null }
        $inner = New-Object System.Drawing.Size([Math]::Max(1, $c.Width - 8), 0)
        $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, $inner, $textFormat)
        if ($c -is [System.Windows.Forms.Button]) {
            $needed = [Math]::Max($need.Height, $c.Font.Height) + [Math]::Ceiling(9 * $dpi / 96)
            # Real rendering at the theme's own DPI (96 here): glyph rows lost to the caption clip rectangle.
            $loss = Get-GlyphLoss $c
            if ($loss) { return ,@("glyph rows $($loss[0])", "$($loss[1])") }
        } else { $needed = $need.Height }
        $actual = $c.Height
    } elseif ($c -is [System.Windows.Forms.Label]) {
        if ($c.AutoSize -or [string]::IsNullOrEmpty($c.Text)) { return $null }
        $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size($c.Width, 0)), $textFormat)
        $needed = $need.Height; $actual = $c.Height
    } elseif ($c -is [System.Windows.Forms.GroupBox]) {
        if ([string]::IsNullOrEmpty($c.Text)) { return $null }
        $needed = $line; $actual = $c.DisplayRectangle.Top + 2
    } elseif ($c -is [System.Windows.Forms.TabControl]) {
        $needed = $line; $actual = $c.ItemSize.Height
    } else {
        return $null
    }
    if ($needed -gt $actual + 1) { return ,@($needed, $actual) }
    return $null
}

function Test-Container([System.Windows.Forms.Control]$parent, [string]$path, $found, [double]$dpi) {
    foreach ($c in @($parent.Controls | Where-Object { Test-Shown $_ })) {
        $r = Get-Clipping $c $dpi
        if ($r) { [void]$found.Add([pscustomobject]@{ Page = $path; Control = $c.Name; Kind = $c.GetType().Name; Needed = $r[0]; Actual = $r[1] }) }
        if ($c.Controls.Count -gt 0 -and -not ($c -is [System.Windows.Forms.UpDownBase]) -and -not ($c -is [System.Windows.Forms.ComboBox]) -and -not ($c -is [System.Windows.Forms.TreeView])) {
            Test-Container $c "$path/$($c.Name)" $found $dpi
        }
    }
}

function Get-Clipped($form, [string]$tag, [double]$dpi) {
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
        Test-Container $page[1] $page[0] $list $dpi
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
$fonts = @(@('Microsoft Sans Serif', 8.25), @('Microsoft JhengHei UI', 9), @('PMingLiU', 9))
# Display scales are only test scenarios: the layout code derives everything from the DPI it is given.
$scenarios = @(@(1.0, 'scaled'), @(1.25, 'scaled'), @(1.5, 'scaled'), @(1.75, 'scaled'), @(2.0, 'scaled'),
    @(1.25, 'unscaled'), @(1.5, 'unscaled'), @(1.75, 'unscaled'), @(2.0, 'unscaled'))
$translated = [Activator]::CreateInstance($uiType, @(, [string]$Language))
$lines = New-Object System.Collections.ArrayList
$checked = 0
foreach ($typeName in $formTypes) {
    $type = $assembly.GetType($typeName, $true)
    foreach ($f in $fonts) {
        foreach ($sc in $scenarios) {
            $scale = $sc[0]; $mode = $sc[1]; $dpi = 96 * $scale
            foreach ($lang in @('en', $Language)) {
                $fontScale = if ($mode -eq 'scaled') { $scale } else { 1.0 }
                $font = New-Object System.Drawing.Font($f[0], [single]($f[1] * $fontScale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
                $tag = "{0}_{1}_x{2}{3}_{4}" -f $type.Name, ($f[0] -replace ' ', ''), $scale, $mode, $lang
                if ($lang -eq 'en') { $form = New-Form $type $font $null $dpi } else { $form = New-Form $type $font $translated $dpi }
                try { $found = Get-Clipped $form $tag $dpi } finally { $form.Dispose() }
                $font.Dispose(); $checked++
                foreach ($r in $found) {
                    [void]$lines.Add(("{0} | {1} | {2} | {3} | {4} {5}pt x{6} {10} | {7} | needs {8}px, has {9}px" -f $type.Name, $r.Page, $r.Control, $r.Kind, $f[0], $f[1], $scale, $lang, $r.Needed, $r.Actual, $mode))
                }
            }
        }
    }
}
$lines | Sort-Object -Unique | ForEach-Object { Write-Output "CLIPPED: $_" }
Write-Output "Checked $checked form/font/scale/language combinations; controls whose text is cut off vertically: $($lines.Count)"
if ($lines.Count -gt 0) { exit 1 }
Write-Output 'PASS: no control is too short for its text'
