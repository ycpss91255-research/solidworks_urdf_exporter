"""Live text-clipping check for the export forms, run against a form that is open in a real SOLIDWORKS.

The offline check (test_text_clipping.ps1) cannot be made DPI aware, so it never sees the control sizes
and theme margins that a DPI aware host produces at 125%/150%/175%/200%. This one looks at the real
window. For every push button of the exporter form it renders the window with PrintWindow and counts the
dark text pixels inside the button face (border excluded). It then draws the same caption, unclipped, with
GDI in the form's own font (taken from a label of the form) and counts those pixels. A button that shows
fewer text pixels than the unclipped caption is cutting its text off (top, bottom or sides).
The Windows display scale, the SOLIDWORKS process and the form are used exactly as they are, so run it at
the scale you want to verify; the form's DPI is printed.

Open the form first (Tools > Export as URDF, then "Preview and export..." for the assembly exporter),
then:   python scripts/test_text_clipping_live.py --pid <SOLIDWORKS pid>
Both pages of the assembly form are checked by pressing the first page's Next button and the second
page's Previous button; pass --next-text / --prev-text if the UI language is not English or Chinese.
--self-test hides the top 3 pixel rows of every button before counting, which the check must report.

Needs pywin32. Only looks at windows of the PID you name.
Exit code 1 when a button is clipped (0 for --self-test when it is detected), 2 when no form was found.
"""
import argparse
import ctypes
import ctypes.wintypes
import sys
import time

import pythoncom
import win32con
import win32gui
import win32process
import win32ui

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # per-monitor v2: real pixels

BS_MASK = 0xF
OWNER_DRAW = 0xB  # WinForms draws buttons, check boxes and radios itself
ROLE_PUSHBUTTON = 0x2B  # MSAA role
WM_GETOBJECT, OBJID_CLIENT = 0x003D, 0xFFFFFFFC
FORM_CLASS_HINT = "WindowsForms10.Window"
DARK = 110
BORDER = 4  # button face border that is not part of the text area
TOLERANCE = 0.97


class LOGFONT(ctypes.Structure):
    _fields_ = [("height", ctypes.c_long), ("width", ctypes.c_long), ("escapement", ctypes.c_long),
                ("orientation", ctypes.c_long), ("weight", ctypes.c_long), ("italic", ctypes.c_byte),
                ("underline", ctypes.c_byte), ("strike", ctypes.c_byte), ("charset", ctypes.c_byte),
                ("out_precision", ctypes.c_byte), ("clip_precision", ctypes.c_byte), ("quality", ctypes.c_byte),
                ("pitch_family", ctypes.c_byte), ("face", ctypes.c_wchar * 32)]


def exporter_form(pid, title_hint):
    found = []

    def visit(hwnd, _):
        if not win32gui.IsWindowVisible(hwnd):
            return
        _, owner = win32process.GetWindowThreadProcessId(hwnd)
        if owner != pid or FORM_CLASS_HINT not in win32gui.GetClassName(hwnd):
            return
        if title_hint.lower() in win32gui.GetWindowText(hwnd).lower():
            found.append(hwnd)

    win32gui.EnumWindows(visit, None)
    return found[0] if found else None


def children(form, wanted):
    result = []

    def visit(hwnd, _):
        if user32.IsWindowVisible(hwnd) and wanted(hwnd):
            result.append(hwnd)

    win32gui.EnumChildWindows(form, visit, None)
    return result


def form_font(form):
    """LOGFONT of the first control that reports one: the form font every caption is drawn with."""
    for label in children(form, lambda h: True):
        handle = win32gui.SendMessage(label, win32con.WM_GETFONT, 0, 0)
        font = LOGFONT()
        if handle and gdi32.GetObjectW(handle, ctypes.sizeof(font), ctypes.byref(font)):
            font.weight = 400
            return font
    return None


def is_push_button(hwnd):
    """MSAA role of the control: only real push buttons (not owner-drawn check boxes or radios)."""
    try:
        lres = win32gui.SendMessage(hwnd, WM_GETOBJECT, 0, OBJID_CLIENT)
        acc = pythoncom.ObjectFromLresult(lres, pythoncom.IID_IDispatch, 0)
        role = acc.Invoke(acc.GetIDsOfNames("accRole"), 0, pythoncom.DISPATCH_PROPERTYGET, True, 0)
        return role == ROLE_PUSHBUTTON
    except pythoncom.com_error:
        return False


def push_buttons(form):
    return children(form, lambda h: "BUTTON" in win32gui.GetClassName(h).upper()
                    and win32gui.GetWindowLong(h, win32con.GWL_STYLE) & BS_MASK == OWNER_DRAW
                    and win32gui.GetWindowText(h) and is_push_button(h))


def capture(hwnd):
    left, top, right, bottom = win32gui.GetWindowRect(hwnd)
    width, height = right - left, bottom - top
    window_dc = win32gui.GetWindowDC(hwnd)
    dc = win32ui.CreateDCFromHandle(window_dc)
    memory = dc.CreateCompatibleDC()
    bitmap = win32ui.CreateBitmap()
    bitmap.CreateCompatibleBitmap(dc, width, height)
    memory.SelectObject(bitmap)
    user32.PrintWindow(hwnd, memory.GetSafeHdc(), 2)  # PW_RENDERFULLCONTENT
    bits = bitmap.GetBitmapBits(True)
    win32gui.DeleteObject(bitmap.GetHandle())
    memory.DeleteDC()
    dc.DeleteDC()
    win32gui.ReleaseDC(hwnd, window_dc)
    return width, bits


def dark_pixels(bits, width, x0, y0, w, h, skip_top=0):
    count = 0
    for y in range(y0 + BORDER + skip_top, y0 + h - BORDER):
        for x in range(x0 + BORDER, x0 + w - BORDER):
            i = (y * width + x) * 4
            if bits[i] < DARK and bits[i + 1] < DARK and bits[i + 2] < DARK:
                count += 1
    return count


def reference_pixels(text, font):
    """Dark pixels of `text` drawn without any clipping in `font`."""
    size = ctypes.wintypes.SIZE()
    screen = win32gui.GetDC(0)
    dc = win32ui.CreateDCFromHandle(screen)
    memory = dc.CreateCompatibleDC()
    handle = gdi32.CreateFontIndirectW(ctypes.byref(font))
    old = gdi32.SelectObject(memory.GetSafeHdc(), handle)
    gdi32.GetTextExtentPoint32W(memory.GetSafeHdc(), text, len(text), ctypes.byref(size))
    width, height = size.cx + 2 * BORDER, size.cy + 2 * BORDER
    bitmap = win32ui.CreateBitmap()
    bitmap.CreateCompatibleBitmap(dc, width, height)
    memory.SelectObject(bitmap)
    gdi32.SelectObject(memory.GetSafeHdc(), handle)
    memory.FillSolidRect((0, 0, width, height), 0xFFFFFF)
    gdi32.SetBkMode(memory.GetSafeHdc(), 1)  # TRANSPARENT
    gdi32.TextOutW(memory.GetSafeHdc(), BORDER, BORDER, text, len(text))
    bits = bitmap.GetBitmapBits(True)
    gdi32.SelectObject(memory.GetSafeHdc(), old)
    gdi32.DeleteObject(handle)
    win32gui.DeleteObject(bitmap.GetHandle())
    memory.DeleteDC()
    dc.DeleteDC()
    win32gui.ReleaseDC(0, screen)
    count = 0
    for i in range(0, len(bits), 4):
        if bits[i] < DARK and bits[i + 1] < DARK and bits[i + 2] < DARK:
            count += 1
    return count


def check_page(form, label, font, self_test):
    problems = 0
    width, bits = capture(form)
    form_left, form_top, _, _ = win32gui.GetWindowRect(form)
    for button in push_buttons(form):
        text = win32gui.GetWindowText(button).replace("&", "")
        left, top, right, bottom = win32gui.GetWindowRect(button)
        shown = dark_pixels(bits, width, left - form_left, top - form_top, right - left, bottom - top,
                            3 if self_test else 0)
        expected = reference_pixels(text, font)
        clipped = expected > 0 and shown < expected * TOLERANCE
        problems += clipped
        print("%-3s %-8s button %dx%d  text px shown %-4d expected %-4d  %r" % (
            label, "CLIPPED" if clipped else "ok", right - left, bottom - top, shown, expected, text))
    return problems


def click(form, texts):
    for button in push_buttons(form):
        if any(t in win32gui.GetWindowText(button) for t in texts):
            win32gui.PostMessage(button, win32con.BM_CLICK, 0, 0)
            time.sleep(1.5)
            return True
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pid", type=int, required=True, help="SOLIDWORKS process that shows the form")
    parser.add_argument("--title", default="URDF", help="part of the form's title bar text")
    parser.add_argument("--next-text", nargs="*", default=["Next", "下一步"])
    parser.add_argument("--prev-text", nargs="*", default=["Previous", "上一步"])
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    form = exporter_form(args.pid, args.title)
    if not form:
        print("no exporter form found for pid %d" % args.pid)
        return 2
    font = form_font(form)
    if not font:
        print("could not read the form font")
        return 2
    print("form dpi %d, font %s %dpx" % (user32.GetDpiForWindow(form), font.face, -font.height))
    problems = check_page(form, "p1", font, args.self_test)
    if click(form, args.next_text):
        problems += check_page(form, "p2", font, args.self_test)
        click(form, args.prev_text)
    print("clipped buttons: %d" % problems)
    if args.self_test:
        return 0 if problems else 1
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
