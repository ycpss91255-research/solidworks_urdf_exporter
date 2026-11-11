# Offline check that every caption sits vertically centred on the edit box it describes, in the
# dialog as the constructor leaves it: designer layout, then the constructor's label alignment
# (AlignFieldLabels) and UiLayout.FitForm, for several system fonts and display-scaling factors.
# A caption is paired with the nearest edit box to its right whose vertical range reaches the
# caption's middle (within one box height). Does not connect to SOLIDWORKS or register COM.
param(
    [string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll",
    [string]$ScreenshotDirectory = ''
)
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
# An exception inside a form event must fail the test, not open a modal dialog on the desktop.
try { [System.Windows.Forms.Application]::SetUnhandledExceptionMode([System.Windows.Forms.UnhandledExceptionMode]::ThrowException) } catch { }  # refused once this process already created a control; a fresh process is the normal case
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$static = [Reflection.BindingFlags]'Static,NonPublic,Public'

function New-Form([Type]$type, [System.Drawing.Font]$font, [single]$dpi) {
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
    [void][System.Windows.Forms.Form].GetConstructor($flags, $null, [Type[]]@(), $null).Invoke($form, @())
    if ($font) { $form.Font = $font }
    try { [void]$type.GetMethod('InitializeComponent', $flags).Invoke($form, @()) } catch { throw $_.Exception.InnerException }
    $events = [System.ComponentModel.Component].GetProperty('Events', $flags).GetValue($form, @())
    $loadKey = [System.Windows.Forms.Form].GetField('EVENT_LOAD', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $events.RemoveHandler($loadKey, $events[$loadKey])
    # The constructor's order: align captions to their boxes, then fit buttons and space rows.
    $align = $type.GetMethod('AlignFieldLabels', $flags)
    if ($align) { [void]$align.Invoke($form, @()) }
    $layout = $assembly.GetType('SW2URDF.UI.UiLayout')
    [void]$layout.GetMethod('FitForm', $static).Invoke($null, @($form, $dpi))
    return $form
}

function Test-IsField($c) { return ($c -is [System.Windows.Forms.TextBox]) -or ($c -is [System.Windows.Forms.ComboBox]) -or ($c -is [System.Windows.Forms.UpDownBase]) }

function Test-Container([System.Windows.Forms.Control]$parent, [string]$path, [double]$tolerance, $problems, [ref]$pairs) {
    $kids = @($parent.Controls)
    foreach ($label in $kids) {
        if (-not ($label -is [System.Windows.Forms.Label]) -or [string]::IsNullOrWhiteSpace($label.Text) -or $label.Font.Bold -or $label.Dock -ne 'None' -or $label.Text.TrimEnd().EndsWith('.')) { continue }  # sentences are notes, not captions
        $middle = $label.Top + $label.Height / 2.0
        $best = $null; $bestDistance = [int]::MaxValue
        foreach ($field in $kids) {
            if (-not (Test-IsField $field)) { continue }
            $distance = $field.Left - $label.Right
            if ($distance -lt -2 -or $distance -gt $label.Height) { continue }
            if ([Math]::Abs(($field.Top + $field.Height / 2.0) - $middle) -gt $field.Height) { continue }
            if ($distance -lt $bestDistance) { $best = $field; $bestDistance = $distance }
        }
        if (-not $best) { continue }
        $pairs.Value++
        $offset = ($label.Top + $label.Height / 2.0) - ($best.Top + $best.Height / 2.0)
        if ([Math]::Abs($offset) -gt $tolerance) {
            [void]$problems.Add(("{0}: label {1} '{2}' centre is {3:N1}px off its box {4}" -f $path, $label.Name, $label.Text, $offset, $best.Name))
        }
    }
    foreach ($c in $kids) { if ($c.Controls.Count -gt 0 -and -not (Test-IsField $c)) { Test-Container $c "$path/$($c.Name)" $tolerance $problems $pairs } }
}

$fonts = @(@('Microsoft Sans Serif', 8.25), @('Microsoft JhengHei UI', 9), @('PMingLiU', 9))
$scales = @(1.0, 1.25, 1.5, 1.75, 2.0)
$total = 0; $checked = 0
$report = New-Object System.Collections.ArrayList
foreach ($typeName in 'SW2URDF.UI.AssemblyExportForm', 'SW2URDF.UI.PartExportForm') {
    $type = $assembly.GetType($typeName, $true)
    foreach ($f in $fonts) {
        foreach ($scale in $scales) {
            $font = New-Object System.Drawing.Font($f[0], [single]($f[1] * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
            $form = New-Form $type $font ([single](96 * $scale))
            try {
                $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-32000, -32000)
                $form.ShowInTaskbar = $false; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
                $problems = New-Object System.Collections.ArrayList
                $pairs = 0
                # One pixel of rounding per 96 DPI step is allowed; a visible offset is larger.
                Test-Container $form $type.Name ([Math]::Max(1.0, $scale)) $problems ([ref]$pairs)
                $checked += $pairs
                foreach ($p in $problems) { [void]$report.Add("[$($f[0]) $($f[1])pt x$scale] $p") }
                $total += $problems.Count
                if ($ScreenshotDirectory) {
                    $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
                    $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)))
                    $bmp.Save((Join-Path $ScreenshotDirectory ("{0}_{1}_{2}.png" -f $type.Name, ($f[0] -replace ' ', ''), $scale)), [System.Drawing.Imaging.ImageFormat]::Png)
                    $bmp.Dispose()
                }
            } finally { $form.Dispose(); $font.Dispose() }
        }
    }
}
$report | ForEach-Object { Write-Output $_ }
Write-Output "Checked $checked caption/box pairs; misaligned: $total"
if ($checked -eq 0) { Write-Output 'FAIL: no caption/box pair found'; exit 1 }
if ($total -gt 0) { exit 1 }
