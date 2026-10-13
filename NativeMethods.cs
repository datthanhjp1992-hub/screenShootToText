using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenOcr
{
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    /// <summary>Chuyển đổi chuỗi hotkey "Ctrl+Shift+Q" &lt;-&gt; modifiers + Keys.</summary>
    internal static class HotkeyParser
    {
        public static bool TryParse(string text, out uint modifiers, out Keys key)
        {
            modifiers = 0;
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;

            foreach (var raw in text.Split('+'))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                switch (t.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                    case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                    case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                    case "win": modifiers |= NativeMethods.MOD_WIN; break;
                    default:
                        if (t.Length == 1 && char.IsDigit(t[0])) t = "D" + t;
                        if (!Enum.TryParse(t, true, out Keys k)) return false;
                        key = k;
                        break;
                }
            }
            return key != Keys.None;
        }

        public static string Format(Keys modifiers, Keys key)
        {
            var parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            var k = key.ToString();
            if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) k = k.Substring(1);
            parts.Add(k);
            return string.Join("+", parts);
        }

        public static bool IsModifierKey(Keys key) =>
            key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
            key == Keys.LWin || key == Keys.RWin || key == Keys.None;
    }
}
