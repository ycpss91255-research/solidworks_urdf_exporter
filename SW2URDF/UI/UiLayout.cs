using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    /// <summary>Layout corrections applied once the designer layout is built.</summary>
    internal static class UiLayout
    {
        // Screen DPI of the process: 144 at 150% scaling when the host is DPI aware.
        internal static float ScreenDpi()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return g.DpiY;
            }
        }

        internal const int WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        // DPI the window is actually drawn at. In a per-monitor DPI aware host (SOLIDWORKS) this is the
        // DPI of the monitor the window sits on, which need not equal the primary screen's DPI
        // (ScreenDpi), and it changes when the window is moved to another monitor.
        internal static float WindowDpi(Control control)
        {
            if (control != null && control.IsHandleCreated)
            {
                try
                {
                    uint dpi = GetDpiForWindow(control.Handle);
                    if (dpi > 0)
                    {
                        return dpi;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    // Windows older than 10 1607: fall through to the process-wide value.
                }
            }
            return ScreenDpi();
        }

        // Same fit, using the DPI the (already created) form really has.
        internal static void FitButtonHeightsToWindow(Control root)
        {
            FitButtonHeights(root, WindowDpi(root));
        }

        // Vertical room, at 96 DPI, that the visual theme takes from a push button's height before
        // the caption gets its clip rectangle (button border plus content margin, top and bottom
        // together). Measured by drawing buttons of growing height next to an unclipped label:
        // a 15px PMingLiU line needs a 24px button to show all glyph rows. The theme scales this
        // with the DPI it draws at.
        internal const float ButtonThemeVerticalMargin96 = 9f;

        // The designer gives push buttons a fixed height, but the visual theme clips the caption to
        // a rectangle smaller than the button, by a margin that grows with the DPI (and CJK fonts
        // are taller). A button that is too short shows its caption with the top of the glyphs
        // cut off, even when the button frame itself looks fine. Grow every button that is
        // too short for its text by the missing height, around its centre line.
        internal static void FitButtonHeights(Control root, float dpi)
        {
            int margin = (int)Math.Ceiling(ButtonThemeVerticalMargin96 * dpi / 96f);
            foreach (Control child in root.Controls)
            {
                Button button = child as Button;
                if (button != null && !button.AutoSize)
                {
                    int text = Math.Max(button.Font.Height, TextRenderer.MeasureText(
                        button.Text, button.Font, new Size(Math.Max(1, button.Width - 8), 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
                    int needed = text + margin;
                    if (button.Height < needed)
                    {
                        button.SetBounds(button.Left, button.Top - (needed - button.Height) / 2,
                            button.Width, needed);
                    }
                }
                if (child.HasChildren)
                {
                    FitButtonHeights(child, dpi);
                }
            }
        }
    }
}
