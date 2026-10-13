using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenOcr
{
    /// <summary>
    /// Sửa các lỗi OCR hay gặp với code (Java/C#/JS...) bằng quy tắc, rất thận trọng:
    ///  - số bị đọc nhầm chữ: "1O0" -> "100", "2l5" -> "215" (giữ hậu tố long "10l")
    ///  - dấu ";" cuối câu lệnh bị đọc thành ":"  ("int x = 0:" -> "int x = 0;")
    /// Không động vào chuỗi "...", ký tự '...' và comment.
    /// Không phụ thuộc Windows API để có thể test độc lập.
    /// </summary>
    public static class CodeFixer
    {
        // token bắt đầu bằng chữ số (identifier không thể bắt đầu bằng số) và chỉ chứa số + O/o/I/l
        private static readonly Regex NumberToken = new Regex(@"(?<![\w$.])\d[0-9OoIl]*(?![\w$])", RegexOptions.Compiled);
        private static readonly Regex Label = new Regex(@"^[A-Za-z_]\w*\s*:$", RegexOptions.Compiled);
        private static readonly Regex StatementStart = new Regex(@"^(return|break|continue|throw|import|package)\b", RegexOptions.Compiled);
        // dòng kiểu Python / switch: dấu ":" ở cuối là hợp lệ
        private static readonly Regex ColonBlockStart = new Regex(
            @"^(case|default|def|if|elif|else|for|while|class|try|except|finally|with|lambda|async|match)\b", RegexOptions.Compiled);

        public static string Fix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) lines[i] = FixLine(lines[i]);
            return string.Join("\r\n", lines);
        }

        public static string FixLine(string line)
        {
            var t = line.TrimStart();
            if (t.Length == 0 || t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*") || t.StartsWith("#"))
                return line;

            // Tách phần code / chuỗi / comment cuối dòng; chỉ sửa phần code.
            var sb = new StringBuilder(line.Length);
            var seg = new StringBuilder();
            int commentAt = -1;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'')
                {
                    sb.Append(FixNumbers(seg.ToString())); seg.Clear();
                    int j = i + 1;
                    while (j < line.Length && line[j] != c) j += line[j] == '\\' ? 2 : 1;
                    j = Math.Min(j, line.Length - 1);
                    sb.Append(line, i, j - i + 1);
                    i = j;
                }
                else if (c == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'))
                {
                    sb.Append(FixNumbers(seg.ToString())); seg.Clear();
                    commentAt = sb.Length;
                    sb.Append(line, i, line.Length - i);
                    break;
                }
                else seg.Append(c);
            }
            sb.Append(FixNumbers(seg.ToString()));

            var fixedLine = sb.ToString();
            var code = commentAt >= 0 ? fixedLine.Substring(0, commentAt) : fixedLine;
            var rest = commentAt >= 0 ? fixedLine.Substring(commentAt) : "";
            return FixTrailingColon(code) + rest;
        }

        private static string FixNumbers(string s)
        {
            if (s.Length == 0) return s;
            return NumberToken.Replace(s, m =>
            {
                var v = m.Value;
                if (!v.Any(char.IsLetter)) return v;
                string suffix = "";
                if (v.Length > 1 && v[v.Length - 1] == 'l') { suffix = "l"; v = v.Substring(0, v.Length - 1); } // 10l = long
                return v.Replace('O', '0').Replace('o', '0').Replace('I', '1').Replace('l', '1') + suffix;
            });
        }

        private static string FixTrailingColon(string code)
        {
            var trimmed = code.TrimEnd();
            var t = trimmed.TrimStart();
            if (!t.EndsWith(":") || t.EndsWith("::")) return code;
            if (Label.IsMatch(t) || ColonBlockStart.IsMatch(t) || t.IndexOf('?') >= 0) return code;

            bool looksLikeStatement =
                t.IndexOf('=') >= 0 || t.EndsWith("):") || t.EndsWith("]:") ||
                t.Contains("++") || t.Contains("--") || StatementStart.IsMatch(t);
            if (!looksLikeStatement) return code;

            return trimmed.Substring(0, trimmed.Length - 1) + ";" + code.Substring(trimmed.Length);
        }
    }
}
