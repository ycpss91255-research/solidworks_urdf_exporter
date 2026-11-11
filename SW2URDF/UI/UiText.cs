using System;
using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SW2URDF.UI
{
    /// <summary>Display text only. Never changes thread culture or URDF identifiers.</summary>
    public sealed class UiText
    {
        private static readonly ResourceManager Resources =
            new ResourceManager("SW2URDF.UI.Strings", typeof(UiText).Assembly);
        public CultureInfo Culture { get; private set; }

        public UiText(string solidWorksLanguage)
        {
            Culture = CultureInfo.GetCultureInfo(
                string.Equals(solidWorksLanguage, "chinese", StringComparison.OrdinalIgnoreCase)
                    ? "zh-TW" : "en");
        }

        public static UiText FromSolidWorks(ISldWorks app)
        {
            try { return new UiText(app == null ? null : app.GetCurrentLanguage()); }
            catch (COMException) { return new UiText(null); }
        }

        // Tools-menu registration. The same string is used to add and to remove the item,
        // because SOLIDWORKS identifies a menu item by its full caption.
        // The parent must equal the caption of SOLIDWORKS' own (localized) Tools menu, otherwise
        // SOLIDWORKS creates a second submenu with that caption instead of using the real one.
        public string ToolsMenuCaption
        {
            get { return Culture.Name == "en" ? "&Tools" : "工具(&T)"; }
        }
        public string ExportMenuPath { get { return Get("Export as URDF") + "@" + ToolsMenuCaption; } }
        public string ExportAssemblyHint { get { return Get("Export assembly as URDF file"); } }
        public string ExportPartHint { get { return Get("Export part as URDF file"); } }

        public string Get(string english)
        {
            if (string.IsNullOrEmpty(english) || Culture.Name == "en") return english;
            // ResGen rejects keys that differ only by case. Canonicalize keys,
            // but preserve the caller's exact English capitalization on fallback.
            return Resources.GetString(english.ToLowerInvariant(), Culture) ?? english;
        }

        // PropertyManager combo boxes show these protocol values, and the selected item text is
        // stored in the configuration and compared by the exporter. Only the shown text is
        // translated; ComboValue maps it back so the stored value is always the English one.
        private static readonly string[] ProtocolValues =
            { "Automatically Generate", "Automatically Detect", "None" };

        public string ComboDisplay(string value)
        {
            return Array.IndexOf(ProtocolValues, value) >= 0 ? Get(value) : value;
        }

        public string ComboValue(string shown)
        {
            foreach (string value in ProtocolValues)
            {
                if (shown == Get(value)) return value;
            }
            return shown;
        }

        // Standard "something went wrong" message box text. detail may be null.
        public string ProblemMessage(string headline, string detail)
        {
            return headline + (detail == null ? "\n" : "\n\"" + detail + "\"\n") +
                string.Format(Get("Email your maintainer with the log file found at {0}"), SW2URDF.Utilities.Logger.GetFileName());
        }
        // Designer coordinates assume English caption widths. Measure the translated
        // label after font scaling and line it up with the field it describes.
        internal static void AlignLabel(Label label, Control field)
        {
            int gap = Math.Max(2, (int)Math.Round(field.Font.Height / 4.0));
            label.Left = field.Left - label.PreferredWidth - gap;
            label.Top = field.Top + (field.Height - label.PreferredHeight) / 2;
        }

        public void Apply(Control control)
        {
            // Editable values, tree node names and protocol-valued ComboBoxes
            // must remain unchanged even if they happen to equal a UI caption.
            if (control is Form || control is Label || control is ButtonBase || control is GroupBox)
                control.Text = Get(control.Text);
            foreach (Control child in control.Controls) Apply(child);
        }
    }
}
