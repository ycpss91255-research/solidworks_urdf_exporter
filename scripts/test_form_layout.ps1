# Offline layout check for the export forms. Does not connect to SOLIDWORKS or register COM.
# Builds each form without its constructor (which needs SOLIDWORKS), runs only the
# designer-generated InitializeComponent, and checks that on every form page
#   - no two sibling controls overlap,
#   - no control extends past its container,
#   - no fixed-size label clips its text,
# for several system fonts and display-scaling factors. Display scaling is simulated by
# enlarging the form font, which is what AutoScaleMode.Font reacts to.
# Pure PowerShell (no Add-Type compilation), so every file that runs is a signed script
# plus signed assemblies.
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$ScreenshotDirectory = '',
    [switch]$ShowAll
)
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)

$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'

function New-Form([Type]$type, [System.Drawing.Font]$font, [single]$dpi) {
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
    [void][System.Windows.Forms.Form].GetConstructor($flags, $null, [Type[]]@(), $null).Invoke($form, @())
    if ($font) { $form.Font = $font }
    try { [void]$type.GetMethod('InitializeComponent', $flags).Invoke($form, @()) } catch { throw $_.Exception.InnerException }
    $events = [System.ComponentModel.Component].GetProperty('Events', $flags).GetValue($form, @())
    $loadKey = [System.Windows.Forms.Form].GetField('EVENT_LOAD', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $events.RemoveHandler($loadKey, $events[$loadKey])
    # The constructors also grow too-short push buttons (UiLayout); check that state at the DPI being simulated.
    $layoutType = $assembly.GetType('SW2URDF.UI.UiLayout')
    $fit = if ($layoutType) { $layoutType.GetMethod('FitButtonHeights', [Reflection.BindingFlags]'Static,NonPublic,Public') }
    if ($fit) { [void]$fit.Invoke($null, @($form, $dpi)) }
    return $form
}

# Control.Visible is false while the form is not shown; read the control's own flag.
$getState = [System.Windows.Forms.Control].GetMethod('GetState', $flags, $null, [Type[]]@([int]), $null)
function Test-Shown([System.Windows.Forms.Control]$c) { return [bool]$getState.Invoke($c, @(2)) }

# Rectangle a control occupies, in its parent's client coordinates.
# A fixed-size, top-left aligned label only occupies the space its text needs; its
# empty remainder may sit under a neighbour without any visible collision.
function Get-Box([System.Windows.Forms.Control]$c) {
    $b = $c.Bounds
    if (($c -is [System.Windows.Forms.Label]) -and -not $c.AutoSize -and $c.TextAlign -eq 'TopLeft' -and -not [string]::IsNullOrEmpty($c.Text)) {
        $format = [System.Windows.Forms.TextFormatFlags]::WordBreak -bor [System.Windows.Forms.TextFormatFlags]::NoPadding
        $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size($c.Width, 0)), $format)
        return New-Object System.Drawing.Rectangle($b.X, $b.Y, [Math]::Min($b.Width, $need.Width), [Math]::Min($b.Height, $need.Height))
    }
    return $b
}

function Test-TextClipped([System.Windows.Forms.Control]$c) {
    if (-not ($c -is [System.Windows.Forms.Label]) -or $c.AutoSize -or [string]::IsNullOrEmpty($c.Text)) { return $false }
    $format = [System.Windows.Forms.TextFormatFlags]::WordBreak -bor [System.Windows.Forms.TextFormatFlags]::NoPadding
    $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size($c.Width, 0)), $format)
    return ($need.Height -gt $c.Height + 1)
}

function Test-Container([System.Windows.Forms.Control]$parent, [string]$path, [bool]$all, $problems) {
    $kids = @($parent.Controls | Where-Object { $all -or (Test-Shown $_) })
    $client = $parent.ClientSize
    foreach ($c in $kids) {
        $b = Get-Box $c
        if (-not $parent.AutoScroll -and ($b.Right -gt $client.Width + 1 -or $b.Bottom -gt $client.Height + 1 -or $b.Left -lt -1 -or $b.Top -lt -1)) {
            [void]$problems.Add("outside ${path}: $($c.Name) $b in $client")
        }
        # A scrolling container may extend past the window, but its navigation buttons must not scroll away
        # with the content: they belong in a fixed (non-scrolling) panel that is always visible.
        if ($parent.AutoScroll -and ($c -is [System.Windows.Forms.Button]) -and $c.Name -match 'Cancel|Finish|Next|Previous|ExportUrdfOnly') {
            [void]$problems.Add("button scrolls away ${path}: $($c.Name) is inside the scrolling container $($parent.Name)")
        }
        if (Test-TextClipped $c) { [void]$problems.Add("text clipped ${path}: $($c.Name) '$($c.Text)' $b") }
        # Fonts must follow the form font (system UI font, display scaling) so text sizes stay
        # proportional to the scaled layout: same family, size within 0.9x-1.3x (bold headings).
        if ($script:baseFont -and -not [string]::IsNullOrEmpty($c.Text) -and ($c -is [System.Windows.Forms.Label] -or $c -is [System.Windows.Forms.ButtonBase] -or $c -is [System.Windows.Forms.GroupBox])) {
            $ratio = $c.Font.SizeInPoints / $script:baseFont.SizeInPoints
            if ($c.Font.FontFamily.Name -ne $script:baseFont.FontFamily.Name -or $ratio -lt 0.9 -or $ratio -gt 1.3) {
                [void]$problems.Add("font ${path}: $($c.Name) $($c.Font.Name) $($c.Font.SizeInPoints)pt vs form $($script:baseFont.Name) $($script:baseFont.SizeInPoints)pt")
            }
        }
    }
    for ($i = 0; $i -lt $kids.Count; $i++) {
        for ($j = $i + 1; $j -lt $kids.Count; $j++) {
            # A label paints an opaque background over its whole bounds, so its empty remainder
            # may touch a neighbour but must not cover the neighbour's text or box.
            $a = Get-Box $kids[$i]; $b = Get-Box $kids[$j]
            $x1 = [System.Drawing.Rectangle]::Intersect($a, $kids[$j].Bounds)
            $x2 = [System.Drawing.Rectangle]::Intersect($kids[$i].Bounds, $b)
            if (($x1.Width -gt 1 -and $x1.Height -gt 1) -or ($x2.Width -gt 1 -and $x2.Height -gt 1)) {
                [void]$problems.Add("overlap ${path}: $($kids[$i].Name) $($kids[$i].Bounds) x $($kids[$j].Name) $($kids[$j].Bounds)")
            }
        }
    }
    foreach ($c in $kids) {
        if ($c.Controls.Count -gt 0 -and -not ($c -is [System.Windows.Forms.TreeView]) -and -not ($c -is [System.Windows.Forms.NumericUpDown]) -and -not ($c -is [System.Windows.Forms.DomainUpDown]) -and -not ($c -is [System.Windows.Forms.ComboBox])) {
            Test-Container $c "$path/$($c.Name)" $all $problems
        }
    }
}

# Each entry is a form page: the controls that are on screen together.
function Get-Pages($form) {
    $pages = @()
    $panel = $form.Controls.Find('panelLinkProperties', $false)
    if ($panel.Count -eq 1) {
        $pages += ,@('joint page', $form, $panel[0])
        $pages += ,@('link page', $panel[0], $panel[0])
    } else {
        $pages += ,@('form', $form, $null)
    }
    return ,$pages
}

$formTypes = @('SW2URDF.UI.AssemblyExportForm', 'SW2URDF.UI.PartExportForm')
$fonts = @(
    @('Microsoft Sans Serif', 8.25), @('Microsoft JhengHei UI', 9), @('PMingLiU', 9)
)
$scales = @(1.0, 1.25, 1.5, 1.75, 2.0)
$total = 0
$report = New-Object System.Collections.ArrayList
foreach ($typeName in $formTypes) {
    $type = $assembly.GetType($typeName, $true)
    foreach ($f in $fonts) {
        foreach ($scale in $scales) {
            $font = New-Object System.Drawing.Font($f[0], [single]($f[1] * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
            $script:baseFont = $font
            $form = New-Form $type $font ([single](96 * $scale))
            try {
                $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-32000, -32000)
                $wasVisible = @{}; foreach ($c in $form.Controls) { $wasVisible[$c.Name] = (Test-Shown $c) }
                $form.ShowInTaskbar = $false; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
                foreach ($page in (Get-Pages $form)) {
                    $problems = New-Object System.Collections.ArrayList
                    $panel = $page[2]
                    # The link page panel covers the joint page; show one page at a time.
                    if ($page[0] -eq 'joint page') { $panel.Visible = $false; foreach ($c in $form.Controls) { if ($c -ne $panel -and $wasVisible[$c.Name]) { $c.Visible = $true } } }
                    if ($page[0] -eq 'link page') { $panel.Visible = $true; foreach ($c in $form.Controls) { if ($c -ne $panel) { $c.Visible = $false } } }
                    [System.Windows.Forms.Application]::DoEvents()
                    Test-Container $page[1] ($page[0]) $ShowAll.IsPresent $problems
                    $label = "$($type.Name) [$($f[0]) $($f[1])pt x$scale] $($page[0])"
                    foreach ($p in $problems) { [void]$report.Add("$label : $p") }
                    $total += $problems.Count
                    if ($ScreenshotDirectory) {
                        $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
                        $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)))
                        $name = ("{0}_{1}_{2}_{3}" -f $type.Name, ($f[0] -replace ' ', ''), $scale, ($page[0] -replace ' ', ''))
                        $bmp.Save((Join-Path $ScreenshotDirectory "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
                        $bmp.Dispose()
                    }
                }
            } finally { $form.Dispose(); $font.Dispose() }
        }
    }
}
$report | ForEach-Object { Write-Output $_ }
Write-Output "Layout problems: $total"
if ($total -gt 0) { exit 1 }
