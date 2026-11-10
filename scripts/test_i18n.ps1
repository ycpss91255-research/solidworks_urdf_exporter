# Runtime check of language detection and fallback. Needs no SOLIDWORKS: it loads the
# built SW2URDF.dll (and its zh-TW satellite beside it) and exercises UiText directly.
# Pure PowerShell, so the only code that runs is signed scripts plus signed assemblies.
param([string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll")
$ErrorActionPreference = 'Stop'
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$directory = Split-Path -Parent $AssemblyPath
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path $directory 'log4net.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$uiTextType = $assembly.GetType('SW2URDF.UI.UiText', $true)
function New-Ui($language) { [Activator]::CreateInstance($uiTextType, @(, [string]$language)) }
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw "FAIL: $message" } }

$thread = [Threading.Thread]::CurrentThread
$originalCulture = $thread.CurrentCulture
$originalUi = $thread.CurrentUICulture
try {
    $thread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    $thread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
    $zh = New-Ui 'chinese'
    Require ($zh.Culture.Name -eq 'zh-TW') 'chinese maps to zh-TW'
    Require ($zh.Get('Cancel') -ne 'Cancel') 'zh-TW satellite resources are missing or not loaded'
    Require ($zh.Get('cancel') -ne 'cancel' -and $zh.Get('cancel') -ceq $zh.Get('CANCEL') -and $zh.Get('Cancel') -ceq $zh.Get('cancel')) 'case-equivalent captions resolve to one resource'
    Require ((New-Ui 'CHINESE').Get('Cancel') -ceq $zh.Get('Cancel')) 'language name is case-insensitive'
    foreach ($language in @($null, '', 'english', 'chinese-simplified', 'french', 'unknown')) {
        $other = New-Ui $language
        Require ($other.Culture.Name -eq 'en') "fallback culture for '$language'"
        Require ($other.Get('Cancel') -ceq 'Cancel') "English fallback for '$language'"
        Require ($other.Get('CANCEL') -ceq 'CANCEL') "English capitalization preserved for '$language'"
    }
    Require ($zh.Get('no such resource') -ceq 'no such resource') 'missing key falls back to the original text'
    Require ($null -eq $zh.Get([NullString]::Value) -and $zh.Get('') -ceq '') 'null and empty pass through'

    # Apply translates captions only; editable values, tree nodes and protocol-valued
    # combo boxes keep their text even when they equal a caption.
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Cancel'
    $panel = New-Object System.Windows.Forms.Panel; $form.Controls.Add($panel)
    $label = New-Object System.Windows.Forms.Label; $label.Text = 'Cancel'; $panel.Controls.Add($label)
    $value = New-Object System.Windows.Forms.TextBox; $value.Text = 'Cancel'; $panel.Controls.Add($value)
    $number = New-Object System.Windows.Forms.TextBox; $number.Text = '1.2345'; $panel.Controls.Add($number)
    $combo = New-Object System.Windows.Forms.ComboBox; [void]$combo.Items.Add('Cancel'); $combo.SelectedIndex = 0; $panel.Controls.Add($combo)
    $tree = New-Object System.Windows.Forms.TreeView; [void]$tree.Nodes.Add('Cancel'); $panel.Controls.Add($tree)
    $zh.Apply($form)
    Require ($form.Text -ceq $zh.Get('Cancel') -and $label.Text -ceq $form.Text) 'captions are applied recursively'
    Require ($value.Text -ceq 'Cancel' -and $number.Text -ceq '1.2345' -and $combo.Text -ceq 'Cancel' -and $tree.Nodes[0].Text -ceq 'Cancel') 'editable values and identifiers were changed'
    $form.Dispose()

    Require ($thread.CurrentCulture.Name -eq 'de-DE') 'numeric culture was changed'
    Require ($thread.CurrentUICulture.Name -eq 'fr-FR') 'host UI culture was changed'
} finally {
    $thread.CurrentCulture = $originalCulture
    $thread.CurrentUICulture = $originalUi
}
Write-Output 'PASS: language mapping, zh-TW satellite, English fallback, nested captions, identifiers and cultures preserved'
