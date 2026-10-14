using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenOcr
{
    /// <summary>
    /// Sửa các lỗi OCR hay gặp với code (Java/C#/JS...) bằng quy tắc, rất thận trọng:
    ///  - số bị đọc nhầm chữ: "1O0" -> "100", "2l5" -> "215" (giữ hậu tố long "10l")
    ///  - chữ l bị đọc thành số 1 trong tên: "equa1s" -> "equals", "StringUti1" -> "StringUtil"
    ///  - hằng số mất dấu gạch dưới: ".DOCUMENT TYPE Ie3" -> ".DOCUMENT_TYPE_103"
    ///  - khoảng trắng thừa: "foo() .bar()" -> "foo().bar()", "foo() ;" -> "foo();"
    ///  - dấu ";" cuối câu lệnh bị đọc thành ":"  ("int x = 0:" -> "int x = 0;")
    ///  - thiếu ";" / thiếu ")" cuối câu lệnh (chỉ khi đa số dòng trong đoạn code kết thúc bằng ";")
    ///  - lỗi riêng của Tesseract: số 0 có chấm (Consolas) -> "@", "//" -> "/f", "buff" -> "buf f"
    /// Không động vào chuỗi "...", ký tự '...' và comment.
    /// Không phụ thuộc Windows API để có thể test độc lập.
    /// </summary>
    public static class CodeFixer
    {
        // token bắt đầu bằng chữ số (identifier không thể bắt đầu bằng số) và chỉ chứa số + O/o/I/l
        private static readonly Regex NumberToken = new Regex(@"(?<![\w$.])\d[0-9OoIl@]*(?![\w$])", RegexOptions.Compiled);
        private static readonly Regex Identifier = new Regex(@"(?<![\w$])[A-Za-z_$][\w$]*", RegexOptions.Compiled);
        // "1" kẹp giữa 2 chữ thường trong tên gần như chắc chắn là "l": equa1s, Dead1ine, p1us
        private static readonly Regex OneBetweenLower = new Regex(@"(?<=[a-z])1(?=[a-z])", RegexOptions.Compiled);
        // ".DOCUMENT TYPE Ie3" : hằng số viết hoa sau dấu chấm, các phần bị tách bởi dấu cách
        private static readonly Regex SpacedConstant = new Regex(@"(?<=\.)[A-Z][A-Z0-9_]+(?:[ _][A-Za-z0-9|@]+)+", RegexOptions.Compiled);
        private static readonly Regex EmptyParens = new Regex(@"\([ \t]+\)", RegexOptions.Compiled);
        // Tesseract đọc "//" của comment thành "/f", "f/", "{f/f", "Jf", "ff"... (chỉ xét token đầu dòng)
        private static readonly Regex BadCommentStart = new Regex(@"^(\s*)([/fJl|{]{2,4})(?=\s|$)(.*)$", RegexOptions.Compiled);
        private static readonly Regex CodeChars = new Regex(@"[=(;.]", RegexOptions.Compiled);
        private static readonly Regex BadElseIf = new Regex(@"\belse [a-z]{1,2}if(?=\s*\()", RegexOptions.Compiled);
        private static readonly Regex BadOpenBrace = new Regex(@"(?<=\)\s*)£\s*$", RegexOptions.Compiled);
        private static readonly Regex SplitIdentifier = new Regex(@"(?<![\w$])([A-Za-z_$][\w$]*) ([A-Za-z_$][\w$]*)(?![\w$])", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforeDot = new Regex(@"(?<=[\w)\]])[ \t]+(?=\.[A-Za-z_$])", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforeSemicolon = new Regex(@"(?<=[\w)\]])[ \t]+(?=;)", RegexOptions.Compiled);
        private static readonly Regex NoSemicolonStart = new Regex(
            @"^(if|for|while|switch|catch|synchronized|else|try|do|finally|case|default|@|\}|\)|public|private|protected|abstract|class|interface|enum)\b|^[@})]",
            RegexOptions.Compiled);
        private static readonly Regex ContinuationStart = new Regex(@"^(\.|\+|-|\*|/[^/*]|&&|\|\||\?|:|\{)", RegexOptions.Compiled);

        // từ hay gặp ở cuối tên (camelCase) mà chữ "l" cuối bị đọc thành "1": StringUti1 -> StringUtil
        private static readonly HashSet<string> LWords = new HashSet<string>(new[]
        {
            "util", "impl", "model", "label", "panel", "level", "detail", "total", "final", "local", "global",
            "control", "pool", "tool", "cell", "mail", "email", "html", "xml", "sql", "url", "signal", "channel",
            "interval", "original", "optional", "real", "small", "equal", "fail", "null", "all", "full", "kill",
            "fill", "pull", "call", "val", "col", "del", "sel", "tel", "excel", "cancel", "normal", "terminal",
            "internal", "external", "approval", "rental", "capital", "manual", "actual", "annual", "partial",
            "decimal", "retrieval", "removal", "arrival", "renewal", "journal", "portal", "modal", "symbol",
            "bool", "ctrl", "tbl", "dtl", "lbl", "pnl", "mdl",
        });
        private static readonly Regex Label = new Regex(@"^[A-Za-z_]\w*\s*:$", RegexOptions.Compiled);
        private static readonly Regex StatementStart = new Regex(@"^(return|break|continue|throw|import|package)\b", RegexOptions.Compiled);
        // dòng kiểu Python / switch: dấu ":" ở cuối là hợp lệ
        private static readonly Regex ColonBlockStart = new Regex(
            @"^(case|default|def|if|elif|else|for|while|class|try|except|finally|with|lambda|async|match)\b", RegexOptions.Compiled);

        public static string Fix(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) lines[i] = FixLine(FixTesseractNoise(lines[i]));
            JoinSplitIdentifiers(lines);
            FixMissingSemicolons(lines);
            return string.Join("\r\n", lines);
        }

        /// <summary>Lỗi đặc trưng của Tesseract ở ký hiệu đứng riêng: "//", "}", "{", "else if".</summary>
        private static string FixTesseractNoise(string line)
        {
            if (line.Trim() == "t") return line.Replace('t', '}'); // "}" đứng một mình bị đọc thành "t"

            var m = BadCommentStart.Match(line);
            if (m.Success && m.Groups[2].Value != "//")
            {
                string token = m.Groups[2].Value, rest = m.Groups[3].Value;
                // token có "/" thì chắc chắn là comment (code không bắt đầu bằng "/x");
                // "ff" / "Jf" thì chỉ sửa khi phần sau không giống code (tránh biến tên "ff = 1")
                if (token.IndexOf('/') >= 0 || ((token == "ff" || token == "Jf") && !CodeChars.IsMatch(rest)))
                    line = m.Groups[1].Value + "//" + rest;
            }

            line = BadElseIf.Replace(line, "else if");
            return BadOpenBrace.Replace(line, "{");
        }

        // ---------- tên bị tách đôi: "buf f.add" -> "buff.add" ----------

        /// <summary>
        /// Ghép 2 từ cạnh nhau khi một phần rất ngắn (1–2 ký tự) và tên ghép lại xuất hiện nguyên vẹn
        /// ở chỗ khác trong cùng đoạn code (VD: "buff" đúng ở dòng khác).
        /// </summary>
        private static void JoinSplitIdentifiers(string[] lines)
        {
            var known = new HashSet<string>();
            foreach (var l in lines)
            {
                var code = SplitComment(l).code;
                foreach (Match m in Identifier.Matches(code)) known.Add(m.Value);
            }
            for (int i = 0; i < lines.Length; i++)
            {
                var (code, comment) = SplitComment(lines[i]);
                if (code.Length == 0) continue;
                var fixedCode = SplitIdentifier.Replace(code, m =>
                {
                    string a = m.Groups[1].Value, b = m.Groups[2].Value;
                    return Math.Min(a.Length, b.Length) <= 2 && known.Contains(a + b) ? a + b : m.Value;
                });
                if (fixedCode != code) lines[i] = fixedCode + (comment.Length > 0 ? " " + comment : "");
            }
        }

        // ---------- thiếu ";" / ")" cuối câu lệnh ----------

        private static void FixMissingSemicolons(string[] lines)
        {
            var parts = lines.Select(SplitComment).ToArray();
            var code = parts.Select(p => p.code.Trim()).Where(c => c.Length > 0).ToList();
            // chỉ sửa khi đoạn text rõ ràng là code kiểu Java/C#/JS (đa số câu lệnh kết thúc bằng ";")
            int stmt = code.Count(c => !c.EndsWith("{") && !c.EndsWith("}") && !NoSemicolonStart.IsMatch(c));
            int semi = code.Count(c => c.EndsWith(";"));
            if (stmt < 3 || semi < stmt * 0.5) return;

            for (int i = 0; i < lines.Length; i++)
            {
                var (c, comment) = parts[i];
                var t = c.Trim();
                if (t.Length == 0 || NoSemicolonStart.IsMatch(t)) continue;

                string next = "";
                for (int j = i + 1; j < lines.Length && next.Length == 0; j++) next = parts[j].code.Trim();
                if (ContinuationStart.IsMatch(next)) continue;

                int open = CountOutsideStrings(t, '('), close = CountOutsideStrings(t, ')');
                string fixedCode = null;
                if (t.EndsWith(")") && open == close)
                    fixedCode = t + ";";                                    // buff.add(x())   -> buff.add(x());
                else if (t.EndsWith(");") && open == close + 1)
                    fixedCode = t.Substring(0, t.Length - 1) + ");";        // buff.add(x();   -> buff.add(x());
                if (fixedCode == null) continue;

                var indent = c.Substring(0, c.Length - c.TrimStart().Length);
                parts[i] = (indent + fixedCode + (comment.Length > 0 ? " " : ""), comment);
                lines[i] = parts[i].code + comment;
            }
        }

        /// <summary>Tách phần code và comment "//" cuối dòng (bỏ qua "//" nằm trong chuỗi).</summary>
        private static (string code, string comment) SplitComment(string line)
        {
            var t = line.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*")) return ("", line);
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'')
                {
                    int j = i + 1;
                    while (j < line.Length && line[j] != c) j += line[j] == '\\' ? 2 : 1;
                    i = j;
                }
                else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    return (line.Substring(0, i).TrimEnd(), line.Substring(i));
            }
            return (line, "");
        }

        private static int CountOutsideStrings(string s, char target)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\'')
                {
                    int j = i + 1;
                    while (j < s.Length && s[j] != c) j += s[j] == '\\' ? 2 : 1;
                    i = j;
                }
                else if (c == target) n++;
            }
            return n;
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
                    sb.Append(FixCode(seg.ToString())); seg.Clear();
                    int j = i + 1;
                    while (j < line.Length && line[j] != c) j += line[j] == '\\' ? 2 : 1;
                    j = Math.Min(j, line.Length - 1);
                    sb.Append(line, i, j - i + 1);
                    i = j;
                }
                else if (c == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'))
                {
                    sb.Append(FixCode(seg.ToString())); seg.Clear();
                    commentAt = sb.Length;
                    sb.Append(line, i, line.Length - i);
                    break;
                }
                else seg.Append(c);
            }
            sb.Append(FixCode(seg.ToString()));

            var fixedLine = sb.ToString();
            var code = commentAt >= 0 ? fixedLine.Substring(0, commentAt) : fixedLine;
            var rest = commentAt >= 0 ? fixedLine.Substring(commentAt) : "";
            return FixTrailingColon(code) + rest;
        }

        /// <summary>Sửa một đoạn code (không chứa chuỗi / comment).</summary>
        private static string FixCode(string s)
        {
            if (s.Length == 0) return s;
            s = FixNumbers(s);
            s = Identifier.Replace(s, m => FixIdentifier(m.Value));
            s = SpacedConstant.Replace(s, m => JoinConstant(m.Value));
            s = SpaceBeforeDot.Replace(s, "");
            s = SpaceBeforeSemicolon.Replace(s, "");
            s = EmptyParens.Replace(s, "()");
            return s;
        }

        private static string FixIdentifier(string id)
        {
            if (id.IndexOf('1') < 0) return id;
            id = OneBetweenLower.Replace(id, "l");

            // "1" ở cuối một từ camelCase: chỉ sửa khi ra từ quen thuộc (Uti1 -> Util, nhưng giữ Name1)
            var chars = id.ToCharArray();
            for (int i = 1; i < chars.Length; i++)
            {
                if (chars[i] != '1' || !char.IsLower(chars[i - 1])) continue;
                if (i + 1 < chars.Length && char.IsLower(chars[i + 1])) continue;
                int start = i - 1;
                while (start > 0 && char.IsLower(chars[start - 1])) start--;
                if (start > 0 && char.IsUpper(chars[start - 1])) start--;
                var word = (new string(chars, start, i - start) + "l").ToLowerInvariant();
                if (LWords.Contains(word)) chars[i] = 'l';
            }
            return new string(chars);
        }

        private static readonly Dictionary<char, char> DigitLookalike = new Dictionary<char, char>
        {
            ['I'] = '1', ['l'] = '1', ['i'] = '1', ['|'] = '1', ['@'] = '0',
            ['O'] = '0', ['o'] = '0', ['D'] = '0', ['Q'] = '0', ['e'] = '0',
            ['z'] = '2', ['Z'] = '2', ['S'] = '5', ['s'] = '5', ['B'] = '8',
        };

        private static string JoinConstant(string m)
        {
            // tách giữ lại dấu phân cách: "DOCUMENT TYPE Ie3" -> DOCUMENT, " ", TYPE, " ", Ie3
            var tokens = Regex.Split(m, "([ _])");
            var sb = new StringBuilder(tokens[0]);
            int i = 1;
            for (; i + 1 < tokens.Length; i += 2)
            {
                var part = tokens[i + 1];
                if (part.Length == 0) { sb.Append('_'); continue; }
                if (part.All(c => (c >= 'A' && c <= 'Z') || char.IsDigit(c)))
                    sb.Append('_').Append(part);
                else if (part.Any(char.IsDigit) && part.All(c => char.IsDigit(c) || DigitLookalike.ContainsKey(c)))
                    sb.Append('_').Append(new string(part.Select(c => char.IsDigit(c) ? c : DigitLookalike[c]).ToArray()));
                else break;
            }
            for (; i < tokens.Length; i++) sb.Append(tokens[i]); // phần không phải hằng số: giữ nguyên
            return sb.ToString();
        }

        private static string FixNumbers(string s)
        {
            return NumberToken.Replace(s, m =>
            {
                var v = m.Value;
                if (!v.Any(c => char.IsLetter(c) || c == '@')) return v;
                string suffix = "";
                if (v.Length > 1 && v[v.Length - 1] == 'l') { suffix = "l"; v = v.Substring(0, v.Length - 1); } // 10l = long
                return v.Replace('O', '0').Replace('o', '0').Replace('@', '0').Replace('I', '1').Replace('l', '1') + suffix;
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
