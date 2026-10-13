using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ScreenOcr
{
    /// <summary>
    /// Cài đặt lưu ở file ScreenOcr.ini cạnh file exe (portable).
    /// Nếu thư mục không ghi được thì lưu vào %APPDATA%\ScreenOcr.
    /// </summary>
    public sealed class AppSettings
    {
        public string LanguageTag = "en-US";
        public int Scale = 2;                    // phóng to ảnh trước khi OCR
        public bool StripLineNumbers = true;     // bỏ số dòng của Eclipse
        public bool KeepIndent = true;           // dựng lại thụt lề từ vị trí chữ
        public bool SnapIndent = true;           // làm tròn thụt lề theo bội số TabSize
        public bool UseTabs = false;             // thụt lề bằng ký tự Tab
        public int TabSize = 4;
        public bool NormalizeWidth = true;       // Ｆｕｌｌ-width -> half-width, “ ” -> "
        public bool AutoInvertDark = true;       // tự đảo màu khi nền tối (Eclipse dark theme)
        public bool AutoOcrAfterCapture = true;
        public bool AutoCopyAfterOcr = true;
        public bool AlwaysOnTop = true;
        public string Hotkey = "Ctrl+Shift+Q";
        public string LastSaveDir = "";

        private static readonly string ExeDirPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenOcr.ini");

        private static readonly string AppDataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenOcr", "ScreenOcr.ini");

        public static AppSettings Load()
        {
            var s = new AppSettings();
            var path = File.Exists(ExeDirPath) ? ExeDirPath : (File.Exists(AppDataPath) ? AppDataPath : null);
            if (path == null) return s;

            try
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var i = line.IndexOf('=');
                    if (i <= 0 || line.TrimStart().StartsWith(";")) continue;
                    d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }

                s.LanguageTag = Str(d, nameof(LanguageTag), s.LanguageTag);
                s.Scale = Clamp(Int(d, nameof(Scale), s.Scale), 1, 4);
                s.StripLineNumbers = Bool(d, nameof(StripLineNumbers), s.StripLineNumbers);
                s.KeepIndent = Bool(d, nameof(KeepIndent), s.KeepIndent);
                s.SnapIndent = Bool(d, nameof(SnapIndent), s.SnapIndent);
                s.UseTabs = Bool(d, nameof(UseTabs), s.UseTabs);
                s.TabSize = Clamp(Int(d, nameof(TabSize), s.TabSize), 1, 16);
                s.NormalizeWidth = Bool(d, nameof(NormalizeWidth), s.NormalizeWidth);
                s.AutoInvertDark = Bool(d, nameof(AutoInvertDark), s.AutoInvertDark);
                s.AutoOcrAfterCapture = Bool(d, nameof(AutoOcrAfterCapture), s.AutoOcrAfterCapture);
                s.AutoCopyAfterOcr = Bool(d, nameof(AutoCopyAfterOcr), s.AutoCopyAfterOcr);
                s.AlwaysOnTop = Bool(d, nameof(AlwaysOnTop), s.AlwaysOnTop);
                s.Hotkey = Str(d, nameof(Hotkey), s.Hotkey);
                s.LastSaveDir = Str(d, nameof(LastSaveDir), s.LastSaveDir);
            }
            catch
            {
                // file hỏng -> dùng mặc định
            }
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; ScreenOcr settings");
            sb.AppendLine($"{nameof(LanguageTag)}={LanguageTag}");
            sb.AppendLine($"{nameof(Scale)}={Scale}");
            sb.AppendLine($"{nameof(StripLineNumbers)}={StripLineNumbers}");
            sb.AppendLine($"{nameof(KeepIndent)}={KeepIndent}");
            sb.AppendLine($"{nameof(SnapIndent)}={SnapIndent}");
            sb.AppendLine($"{nameof(UseTabs)}={UseTabs}");
            sb.AppendLine($"{nameof(TabSize)}={TabSize}");
            sb.AppendLine($"{nameof(NormalizeWidth)}={NormalizeWidth}");
            sb.AppendLine($"{nameof(AutoInvertDark)}={AutoInvertDark}");
            sb.AppendLine($"{nameof(AutoOcrAfterCapture)}={AutoOcrAfterCapture}");
            sb.AppendLine($"{nameof(AutoCopyAfterOcr)}={AutoCopyAfterOcr}");
            sb.AppendLine($"{nameof(AlwaysOnTop)}={AlwaysOnTop}");
            sb.AppendLine($"{nameof(Hotkey)}={Hotkey}");
            sb.AppendLine($"{nameof(LastSaveDir)}={LastSaveDir}");

            try
            {
                File.WriteAllText(ExeDirPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath));
                    File.WriteAllText(AppDataPath, sb.ToString(), Encoding.UTF8);
                }
                catch { /* bỏ qua */ }
            }
        }

        private static string Str(Dictionary<string, string> d, string k, string def) =>
            d.TryGetValue(k, out var v) && v.Length > 0 ? v : def;

        private static int Int(Dictionary<string, string> d, string k, int def) =>
            d.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : def;

        private static bool Bool(Dictionary<string, string> d, string k, bool def) =>
            d.TryGetValue(k, out var v) && bool.TryParse(v, out var r) ? r : def;

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
    }
}
