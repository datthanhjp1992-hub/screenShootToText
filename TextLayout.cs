using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenOcr
{
    /// <summary>Một từ OCR và khung bao của nó (toạ độ pixel).</summary>
    public sealed class WordBox
    {
        public string Text;
        public double X, Y, Width, Height;
        /// <summary>Độ tin cậy 0–100 (Tesseract). -1 = engine không cung cấp (Windows OCR).</summary>
        public double Confidence = -1;
        public double R => X + Width;
        public double B => Y + Height;

        public WordBox(string text, double x, double y, double w, double h)
        {
            Text = text; X = x; Y = y; Width = w; Height = h;
        }
    }

    /// <summary>
    /// Dựng lại văn bản dạng code từ vị trí các từ:
    ///  - gộp các dòng cùng hàng, sắp xếp trên→dưới, trái→phải
    ///  - bỏ số dòng Eclipse
    ///  - khôi phục thụt lề + dòng trống + khoảng trắng giữa các từ
    ///  - chuẩn hoá ký tự full-width
    /// Không phụ thuộc Windows API để có thể test độc lập.
    /// </summary>
    public static class TextLayout
    {
        private sealed class Row
        {
            public List<WordBox> Words = new List<WordBox>();
            public double Cy, H;
        }

        private static readonly Regex PureNumber = new Regex(@"^\d+$", RegexOptions.Compiled);
        // số dòng bị dính vào token đầu: "12public" -> "public", "12for" -> "for".
        // Chỉ chạy khi đã xác định ảnh có cột số dòng, nên chỉ chừa lại số thập phân "12.5" / "1,000".
        private static readonly Regex FusedNumber = new Regex(@"^\d+(?=[^\d.,])", RegexOptions.Compiled);

        public static string Build(IEnumerable<IEnumerable<WordBox>> ocrLines, AppSettings opt)
        {
            // 1. Tạo các hàng
            var rows = new List<Row>();
            foreach (var l in ocrLines)
            {
                var ws = l.Where(w => !string.IsNullOrEmpty(w.Text)).OrderBy(w => w.X).ToList();
                if (ws.Count == 0) continue;
                rows.Add(MakeRow(ws));
            }
            if (rows.Count == 0) return string.Empty;

            // 2. Gộp các dòng OCR nằm trên cùng một hàng (VD: comment ở cuối dòng bị tách riêng)
            rows = rows.OrderBy(r => r.Cy).ToList();
            var merged = new List<Row>();
            foreach (var r in rows)
            {
                var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && Math.Abs(last.Cy - r.Cy) < Math.Min(last.H, r.H) * 0.5)
                {
                    var all = last.Words.Concat(r.Words).OrderBy(w => w.X).ToList();
                    merged[merged.Count - 1] = MakeRow(all);
                }
                else merged.Add(r);
            }
            rows = merged;

            // 3. Bỏ số dòng (chỉ khi đa số dòng bắt đầu bằng số)
            if (opt.StripLineNumbers)
            {
                int numbered = rows.Count(r => char.IsDigit(r.Words[0].Text[0]));
                if (rows.Count >= 2 && numbered >= rows.Count * 0.6)
                {
                    foreach (var r in rows)
                    {
                        var first = r.Words[0];
                        if (PureNumber.IsMatch(first.Text))
                        {
                            r.Words.RemoveAt(0);
                        }
                        else
                        {
                            var m = FusedNumber.Match(first.Text);
                            if (m.Success)
                            {
                                // ước lượng vị trí phần code còn lại
                                double perChar = first.Width / first.Text.Length;
                                var rest = first.Text.Substring(m.Length);
                                r.Words[0] = new WordBox(rest, first.X + perChar * m.Length, first.Y,
                                    first.Width - perChar * m.Length, first.Height);
                            }
                        }
                    }
                }
            }

            // 4. Ước lượng độ rộng 1 ký tự (font code là monospace)
            double cw = EstimateCharWidth(rows);

            // 5. Lề trái chung
            var nonEmpty = rows.Where(r => r.Words.Count > 0).ToList();
            double minLeft = nonEmpty.Count > 0 ? nonEmpty.Min(r => r.Words[0].X) : 0;

            // 6. Khoảng cách giữa các dòng -> phát hiện dòng trống
            double pitch = 0;
            if (rows.Count >= 3)
            {
                var diffs = new List<double>();
                for (int i = 1; i < rows.Count; i++) diffs.Add(rows[i].Cy - rows[i - 1].Cy);
                // trung vị dưới: khi có nhiều dòng trống (khoảng cách gấp đôi) vẫn ra đúng khoảng cách 1 dòng
                pitch = LowerMedian(diffs.Where(d => d > 0).ToList());
            }

            // 7. Ghép text
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (i > 0 && pitch > 0)
                {
                    int blanks = (int)Math.Round((r.Cy - rows[i - 1].Cy) / pitch) - 1;
                    for (int b = 0; b < Math.Min(blanks, 5); b++) sb.Append("\r\n");
                }

                if (r.Words.Count > 0)
                {
                    if (opt.KeepIndent)
                    {
                        int cols = (int)Math.Round((r.Words[0].X - minLeft) / cw);
                        if (opt.SnapIndent && opt.TabSize > 1)
                            cols = (int)Math.Round(cols / (double)opt.TabSize) * opt.TabSize;
                        sb.Append(MakeIndent(Math.Max(0, cols), opt));
                    }

                    for (int w = 0; w < r.Words.Count; w++)
                    {
                        var word = r.Words[w];
                        if (w > 0)
                        {
                            var prev = r.Words[w - 1];
                            double gap = word.X - prev.R;
                            bool wide = IsWide(prev.Text[prev.Text.Length - 1]) || IsWide(word.Text[0]);
                            double threshold = wide ? cw * 0.9 : cw * 0.45;
                            if (gap >= threshold)
                            {
                                int spaces = Math.Max(1, (int)Math.Round(gap / cw - 0.2));
                                sb.Append(' ', Math.Min(spaces, 40));
                            }
                        }
                        sb.Append(word.Text);
                    }
                }
                sb.Append("\r\n");
            }

            var text = sb.ToString();
            if (opt.NormalizeWidth) text = NormalizeChars(text);
            if (opt.FixCodeChars) text = CodeFixer.Fix(text);

            // bỏ khoảng trắng cuối dòng và dòng trống ở cuối
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None).Select(l => l.TrimEnd());
            return string.Join("\r\n", lines).TrimEnd('\r', '\n');
        }

        private static Row MakeRow(List<WordBox> ws)
        {
            double top = ws.Min(w => w.Y), bottom = ws.Max(w => w.B);
            return new Row { Words = ws, Cy = (top + bottom) / 2, H = Math.Max(1, bottom - top) };
        }

        private static double EstimateCharWidth(List<Row> rows)
        {
            var samples = new List<double>();
            foreach (var w in rows.SelectMany(r => r.Words))
            {
                if (w.Text.Length >= 3 && w.Text.All(c => c < 0x80))
                    samples.Add(w.Width / (w.Text.Length - 0.2)); // trừ khoảng hở 2 bên glyph
            }
            if (samples.Count > 0) return Math.Max(1, Median(samples));

            var heights = rows.Select(r => r.H).ToList();
            return Math.Max(1, Median(heights) * 0.5);
        }

        private static string MakeIndent(int cols, AppSettings opt)
        {
            if (!opt.UseTabs || opt.TabSize < 1) return new string(' ', cols);
            return new string('\t', cols / opt.TabSize) + new string(' ', cols % opt.TabSize);
        }

        /// <summary>Chiều cao trung vị của các dòng OCR (px), 0 nếu không có chữ.</summary>
        public static double MedianLineHeight(IEnumerable<IEnumerable<WordBox>> lines)
        {
            var hs = lines.Select(l => l.ToList()).Where(l => l.Count > 0)
                          .Select(l => l.Max(w => w.B) - l.Min(w => w.Y)).ToList();
            return Median(hs);
        }

        /// <summary>Tổng số ký tự nhận dạng được (để so sánh 2 lần OCR).</summary>
        public static int CharCount(IEnumerable<IEnumerable<WordBox>> lines) =>
            lines.Sum(l => l.Sum(w => w.Text?.Length ?? 0));

        // ---------- OCR kết hợp en + ja ----------

        /// <summary>
        /// Ghép kết quả của 2 engine trên cùng một ảnh:
        ///  - chữ Nhật (CJK/kana) lấy từ engine ja
        ///  - phần còn lại (code, ký hiệu) lấy từ engine en, bỏ các từ "rác" en đọc đè lên chữ Nhật
        ///  - từ ASCII mà en bỏ sót thì lấy bù từ ja
        /// Kết quả vẫn là danh sách dòng; Build() sẽ gộp các dòng nằm trên cùng một hàng.
        /// </summary>
        public static List<List<WordBox>> MergeHybrid(
            IEnumerable<IEnumerable<WordBox>> enLines, IEnumerable<IEnumerable<WordBox>> jaLines)
        {
            var ja = jaLines.Select(l => l.Where(w => !string.IsNullOrEmpty(w.Text)).ToList()).ToList();
            var cjkWords = ja.SelectMany(l => l).Where(w => w.Text.Any(IsWide)).ToList();

            var result = new List<List<WordBox>>();
            var enKept = new List<WordBox>();
            foreach (var l in enLines)
            {
                var keep = l.Where(w => !string.IsNullOrEmpty(w.Text) &&
                                        !cjkWords.Any(c => OverlapRatio(w, c) > 0.3)).ToList();
                if (keep.Count == 0) continue;
                enKept.AddRange(keep);
                result.Add(keep);
            }

            foreach (var l in ja)
            {
                var keep = l.Where(w => w.Text.Any(IsWide) ||
                                        !enKept.Any(e => OverlapRatio(w, e) > 0.3)).ToList();
                if (keep.Count > 0) result.Add(keep);
            }
            return result;
        }

        // ---------- Tesseract (code) + Windows OCR ja (chữ Nhật) ----------

        private static readonly Regex CodeLike = new Regex(@"[.(_""\[=;]", RegexOptions.Compiled);

        /// <summary>
        /// Ghép kết quả Tesseract (đọc code tốt) với Windows OCR tiếng Nhật (đọc chữ Nhật tốt):
        ///  - chữ Nhật (CJK/kana) lấy từ ja, TRỪ khi vùng đó Tesseract đọc ra code chắc chắn
        ///    (engine ja hay "bịa" ra chữ Nhật trên code in nghiêng, VD: DOCUMENT_TYPE_103 -> DOCu胸EIV…)
        ///  - phần còn lại lấy từ Tesseract, bỏ các từ rác Tesseract đọc đè lên chữ Nhật
        /// </summary>
        public static List<List<WordBox>> MergeCodeWithJapanese(
            IEnumerable<IEnumerable<WordBox>> codeLines, IEnumerable<IEnumerable<WordBox>> jaLines)
        {
            var code = codeLines.Select(l => l.Where(w => !string.IsNullOrEmpty(w.Text)).ToList()).ToList();
            var trusted = code.SelectMany(l => l).Where(IsTrustedCode).ToList();

            var cjk = jaLines
                .Select(l => l.Where(w => !string.IsNullOrEmpty(w.Text) && w.Text.Any(IsWide) &&
                                          !trusted.Any(t => OverlapRatio(w, t) > 0.3)).ToList())
                .Where(l => l.Count > 0).ToList();
            var cjkWords = cjk.SelectMany(l => l).ToList();

            var result = new List<List<WordBox>>();
            foreach (var l in code)
            {
                var keep = l.Where(w => !cjkWords.Any(c => OverlapRatio(w, c) > 0.3)).ToList();
                if (keep.Count > 0) result.Add(keep);
            }
            result.AddRange(cjk);
            return result;
        }

        /// <summary>Từ Tesseract đọc ra chắc chắn là code (không phải rác đè lên chữ Nhật).</summary>
        private static bool IsTrustedCode(WordBox w)
        {
            if (w.Text.Any(c => c > 0x7F)) return false;
            if (w.Confidence >= 60) return true;
            return w.Confidence >= 30 && w.Text.Length >= 6 && CodeLike.IsMatch(w.Text);
        }

        /// <summary>Tỉ lệ diện tích của <paramref name="a"/> bị <paramref name="b"/> che.</summary>
        private static double OverlapRatio(WordBox a, WordBox b)
        {
            double w = Math.Min(a.R, b.R) - Math.Max(a.X, b.X);
            double h = Math.Min(a.B, b.B) - Math.Max(a.Y, b.Y);
            if (w <= 0 || h <= 0) return 0;
            double area = Math.Max(1e-6, a.Width * a.Height);
            return w * h / area;
        }

        // ---------- Ghép text của nhiều ảnh chụp cuộn ----------

        /// <summary>
        /// Nối text của nhiều ảnh chụp liên tiếp (cuộn trang), bỏ các dòng bị trùng ở chỗ nối:
        /// tìm k lớn nhất sao cho k dòng cuối của phần trước = k dòng đầu của ảnh sau
        /// (so sánh bỏ khoảng trắng). Phần trùng phải có ít nhất 1 dòng "có nội dung"
        /// để tránh ghép nhầm các dòng chỉ có "}".
        /// </summary>
        public static string Stitch(IEnumerable<string> texts)
        {
            var result = new List<string>();
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                var lines = t.Replace("\r\n", "\n").Split('\n').ToList();
                int k = FindOverlap(result, lines);
                result.AddRange(lines.Skip(k));
            }
            return string.Join("\r\n", result);
        }

        private static int FindOverlap(List<string> a, List<string> b)
        {
            for (int k = Math.Min(a.Count, b.Count); k >= 1; k--)
            {
                bool same = true, meaningful = false;
                for (int i = 0; i < k && same; i++)
                {
                    var x = StitchKey(a[a.Count - k + i]);
                    same = x == StitchKey(b[i]);
                    if (x.Length >= 4) meaningful = true;
                }
                if (same && meaningful) return k;
            }
            return 0;
        }

        private static string StitchKey(string line) =>
            new string(line.Where(c => !char.IsWhiteSpace(c)).ToArray());

        /// <summary>Ký tự rộng (CJK, kana, full-width).</summary>
        internal static bool IsWide(char c) =>
            (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) ||
            (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) ||
            (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);

        /// <summary>Ｆｕｌｌ-width ASCII -> half-width, dấu nháy cong -> nháy thẳng.</summary>
        public static string NormalizeChars(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= '！' && c <= '～') sb.Append((char)(c - 0xFEE0));
                else if (c == '　') sb.Append(' ');
                else if (c == '“' || c == '”' || c == '„' || c == '″') sb.Append('"');
                else if (c == '‘' || c == '’' || c == '′' || c == '´') sb.Append('\'');
                else if (c == '—' || c == '–' || c == '−') sb.Append('-');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static double LowerMedian(List<double> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[(v.Count - 1) / 2];
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }
    }
}
