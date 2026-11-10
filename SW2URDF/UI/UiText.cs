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
        public string ExportMenuPath { get { return Get("Export as URDF") + "@&Tools"; } }
        public string ExportAssemblyHint { get { return Get("Export assembly as URDF file"); } }
        public string ExportPartHint { get { return Get("Export part as URDF file"); } }

        public string Get(string english)
        {
            if (string.IsNullOrEmpty(english) || Culture.Name == "en") return english;
            // ResGen rejects keys that differ only by case. Canonicalize keys,
            // but preserve the caller's exact English capitalization on fallback.
            return Resources.GetString(english.ToLowerInvariant(), Culture) ?? english;
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
