using System;
using System.Drawing;
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

        // The designer gives push buttons a fixed height, but the visual theme draws the
        // button face with a margin that grows with the DPI (and CJK fonts are taller), so at
        // 125%/150% the top and bottom of the caption were cut off. Grow every button that is
        // too short for its text by the missing height, around its centre line.
        internal static void FitButtonHeights(Control root, float dpi)
        {
            int margin = (int)Math.Ceiling(3 * dpi / 96f);
            foreach (Control child in root.Controls)
            {
                Button button = child as Button;
                if (button != null && !button.AutoSize)
                {
                    int text = Math.Max(button.Font.Height, TextRenderer.MeasureText(
                        button.Text, button.Font, new Size(Math.Max(1, button.Width - 8), 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
                    int needed = text + 2 * margin;
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
