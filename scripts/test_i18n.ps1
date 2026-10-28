param([string]$AssemblyPath = "$PSScriptRoot/../SW2URDF/bin/x64/Debug/SW2URDF.dll")
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}
$AssemblyPath=(Resolve-Path $AssemblyPath).Path
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom($AssemblyPath)
$code=@'
using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using SW2URDF.UI;
public static class I18nCheck {
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    public static void Run() {
        var original=Thread.CurrentThread.CurrentCulture;
        var originalUI=Thread.CurrentThread.CurrentUICulture;
        try {
            Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("fr-FR");
            var zh=new UiText("chinese");
            Require(zh.Culture.Name=="zh-TW","Native Traditional Chinese mapping");
            Require(zh.Get("Cancel")!="Cancel","Traditional Chinese satellite missing");
            Require(zh.Get("URDF name")!="URDF name" && zh.Get("URDF name")==zh.Get("URDF Name"),"Case-equivalent resource keys");
            Require(new UiText("CHINESE").Get("Cancel")==zh.Get("Cancel"),"Case handling");
            foreach(var language in new[]{null,"","english","chinese-simplified","french","unknown"})
                Require(new UiText(language).Get("Cancel")=="Cancel","English fallback: "+language);
            Require(zh.Get("unknown resource")=="unknown resource","Missing key fallback");
            using(var form=new Form {Text="Cancel"}) {
                var panel=new Panel();form.Controls.Add(panel);
                var label=new Label {Text="Cancel"};panel.Controls.Add(label);
                var value=new TextBox {Text="Cancel"};panel.Controls.Add(value);
                var number=new TextBox {Text="1.2345"};panel.Controls.Add(number);
                var combo=new ComboBox();combo.Items.Add("Cancel");combo.SelectedIndex=0;panel.Controls.Add(combo);
                var tree=new TreeView();tree.Nodes.Add("Cancel");panel.Controls.Add(tree);
                zh.Apply(form);
                Require(form.Text==zh.Get("Cancel")&&label.Text==form.Text,"Recursive captions");
                Require(value.Text=="Cancel"&&number.Text=="1.2345"&&combo.Text=="Cancel"&&tree.Nodes[0].Text=="Cancel","Identifiers or numeric values changed");
            }
            Require(Thread.CurrentThread.CurrentCulture.Name=="de-DE","Numeric culture changed");
            Require(Thread.CurrentThread.CurrentUICulture.Name=="fr-FR","Host UI culture changed");
        } finally {
            Thread.CurrentThread.CurrentCulture=original;
            Thread.CurrentThread.CurrentUICulture=originalUI;
        }
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies @($AssemblyPath,'System.Windows.Forms','System.Drawing')
[I18nCheck]::Run()
Write-Output 'PASS: native language mapping, deployed satellite, English fallback, nested captions, identifiers and numeric/host cultures preserved'
