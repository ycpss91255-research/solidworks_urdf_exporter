using System;
using System.Collections.Generic;
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
            FitForm(root, WindowDpi(root));
        }

        // Fits the button heights and gives the form the height the buttons gained. The button row at
        // the bottom sizes itself to its buttons, so taller buttons would otherwise take the room from
        // the content above it and push its last line out of view.
        internal static void FitForm(Control root, float dpi)
        {
            int gained = FitButtonHeights(root, dpi) + SpaceRows(root, dpi);
            Form form = root as Form;
            if (form == null || gained <= 0 || form.WindowState != FormWindowState.Normal)
            {
                return;
            }
            Rectangle area = Screen.FromControl(form).WorkingArea;
            form.Height = Math.Min(form.Height + gained, area.Height);
            if (form.IsHandleCreated && form.Bottom > area.Bottom)
            {
                form.Top = Math.Max(area.Top, area.Bottom - form.Height);
            }
        }

        // Vertical room, at 96 DPI, that the visual theme takes from a push button's height before
        // the caption gets its clip rectangle (button border plus content margin, top and bottom
        // together). Measured by drawing buttons of growing height next to an unclipped label:
        // a 15px PMingLiU line needs a 24px button to show all glyph rows. The theme scales this
        // with the DPI it draws at.
        internal const float ButtonThemeVerticalMargin96 = 9f;

        // Smallest empty space, at 96 DPI, between two stacked rows of controls, and between the last
        // row and the bottom edge of its group box. Both scale with the DPI the form is drawn at.
        internal const float MinRowGap96 = 6f;
        internal const float MinBottomPad96 = 10f;

        internal static int MinRowGap(float dpi)
        {
            return (int)Math.Round(MinRowGap96 * dpi / 96f);
        }

        internal static int MinBottomPad(float dpi)
        {
            return (int)Math.Round(MinBottomPad96 * dpi / 96f);
        }

        // The designer positions rows at a fixed pitch for the font it was drawn with, but edit boxes
        // take their height from the real font (CJK fonts are taller), so the gap between stacked
        // boxes shrinks and group boxes end right below their last row. Push controls down until stacked
        // edit boxes keep the minimum gap, centre captions on the edit box beside them, and grow group
        // boxes so the last row keeps its bottom padding. Only ever adds space, so running it again changes nothing.
        // Returns the height the content of root gained (0 when nothing moved).
        internal static int SpaceRows(Control root, float dpi)
        {
            return SpaceContainer(root, MinRowGap(dpi), MinBottomPad(dpi));
        }

        private static bool IsRowContainer(Control c)
        {
            return c.HasChildren && (c is Form || c is GroupBox ||
                (c is Panel && !(c is FlowLayoutPanel) && !(c is TableLayoutPanel)));
        }

        private static bool GrowsWithContent(Control c)
        {
            Panel panel = c as Panel;
            return c is GroupBox || (panel != null && panel.Dock == DockStyle.None && !panel.AutoScroll);
        }

        private static bool IsField(Control c)
        {
            return c is TextBox || c is ComboBox || c is UpDownBase;
        }

        private static bool IsMovable(Control c)
        {
            return c.Dock == DockStyle.None &&
                !((c.Anchor & AnchorStyles.Bottom) != 0 && (c.Anchor & AnchorStyles.Top) == 0);
        }

        private static int SpaceContainer(Control container, int gap, int pad)
        {
            List<Control> items = new List<Control>();
            foreach (Control child in container.Controls)
            {
                if (IsMovable(child))
                {
                    items.Add(child);
                }
            }
            int oldBottom = 0;
            foreach (Control item in items)
            {
                oldBottom = Math.Max(oldBottom, item.Bottom);
            }

            int bubbled = 0;
            Dictionary<Control, int> grew = new Dictionary<Control, int>();
            foreach (Control child in container.Controls)
            {
                if (!IsRowContainer(child))
                {
                    continue;
                }
                int grown = SpaceContainer(child, gap, pad);
                if (grown > 0)
                {
                    if (GrowsWithContent(child))
                    {
                        child.Height += grown;
                        grew[child] = grown;
                    }
                    else
                    {
                        bubbled = Math.Max(bubbled, grown);
                    }
                }
            }

            // Each control moves down by the largest shift of the controls stacked above it (so the
            // distances inside a column stay as designed), plus whatever an edit box needs to keep the
            // minimum gap to the edit box right above it. Columns do not push each other.
            items.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
            Dictionary<Control, int> shifts = new Dictionary<Control, int>();
            Dictionary<Control, int> tops = new Dictionary<Control, int>();
            foreach (Control item in items)
            {
                tops[item] = item.Top;
            }
            Dictionary<Control, Control> bound = new Dictionary<Control, Control>();
            foreach (Control item in items)
            {
                Control field = IsField(item) ? null : FieldBesides(item, items);
                if (field != null)
                {
                    bound[item] = field;
                }
            }
            foreach (Control item in items)
            {
                if (bound.ContainsKey(item))
                {
                    continue;
                }
                int shift = 0;
                foreach (Control above in items)
                {
                    int aboveShift;
                    if (above == item || bound.ContainsKey(above) || tops[above] >= tops[item] ||
                        above.Left >= item.Right || item.Left >= above.Right ||
                        !shifts.TryGetValue(above, out aboveShift))
                    {
                        continue;
                    }
                    int extra = IsField(above) && IsField(item)
                        ? Math.Max(0, gap - (tops[item] - (tops[above] + above.Height))) : 0;
                    int growth;
                    grew.TryGetValue(above, out growth);
                    shift = Math.Max(shift, aboveShift + growth + extra);
                }
                shifts[item] = shift;
                item.Top += shift;
            }
            // A caption or button beside an edit box follows it; a caption is centred on its box.
            foreach (KeyValuePair<Control, Control> pair in bound)
            {
                Control item = pair.Key;
                Control field = pair.Value;
                if (item is Label)
                {
                    item.Top = field.Top + (field.Height - item.Height) / 2;
                }
                else
                {
                    item.Top += shifts[field];
                }
            }
            int newBottom = 0;
            foreach (Control item in items)
            {
                newBottom = Math.Max(newBottom, item.Bottom);
            }

            if (GrowsWithContent(container))
            {
                return Math.Max(0, newBottom + pad - container.ClientSize.Height);
            }
            return Math.Max(bubbled, newBottom - oldBottom);
        }

        private static int HorizontalDistance(Control a, Control b)
        {
            return Math.Max(a.Left - b.Right, b.Left - a.Right);
        }

        // The edit box whose vertical range holds the middle of item and that sits beside it
        // (no horizontal overlap), nearest middle first; null when item is not part of a row of
        // an edit box.
        private static Control FieldBesides(Control item, List<Control> items)
        {
            Control best = null;
            int middle = item.Top + item.Height / 2;
            foreach (Control field in items)
            {
                if (!IsField(field) || field.Left < item.Right && item.Left < field.Right ||
                    middle < field.Top || middle >= field.Bottom || item.Height > field.Height + 2 ||
                    HorizontalDistance(item, field) > item.Height * 5 / 2)
                {
                    continue;
                }
                if (best == null || Math.Abs(field.Top + field.Height / 2 - middle) <
                    Math.Abs(best.Top + best.Height / 2 - middle))
                {
                    best = field;
                }
            }
            return best;
        }

        // The designer gives push buttons a fixed height, but the visual theme clips the caption to
        // a rectangle smaller than the button, by a margin that grows with the DPI (and CJK fonts
        // are taller). A button that is too short shows its caption with the top of the glyphs
        // cut off, even when the button frame itself looks fine. Grow every button that is
        // too short for its text by the missing height, around its centre line.
        // Returns the largest height any single button gained (0 when nothing had to grow).
        internal static int FitButtonHeights(Control root, float dpi)
        {
            int gained = 0;
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
                        gained = Math.Max(gained, needed - button.Height);
                        button.SetBounds(button.Left, button.Top - (needed - button.Height) / 2,
                            button.Width, needed);
                    }
                }
                if (child.HasChildren)
                {
                    gained = Math.Max(gained, FitButtonHeights(child, dpi));
                }
            }
            return gained;
        }
    }
}
