using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinLanguage = Windows.Globalization.Language;

namespace ScreenOcr
{
    /// <summary>
    /// OCR offline bằng engine có sẵn của Windows 10/11 (Windows.Media.Ocr).
    /// </summary>
    public sealed class OcrService
    {
        private readonly Dictionary<string, OcrEngine> _engines = new Dictionary<string, OcrEngine>();

        /// <summary>Tag đặc biệt: chạy cả engine tiếng Anh và tiếng Nhật rồi ghép kết quả.</summary>
        public const string HybridTag = "hybrid";

        /// <summary>Các ngôn ngữ OCR đã cài trên máy: (tag, tên hiển thị). Có thêm mục kết hợp en + ja nếu đủ gói.</summary>
        public static List<KeyValuePair<string, string>> GetLanguages()
        {
            var list = OcrEngine.AvailableRecognizerLanguages
                .Select(l => new KeyValuePair<string, string>(l.LanguageTag, l.DisplayName))
                .ToList();
            if (TryGetHybridTags(out _, out _))
                list.Insert(0, new KeyValuePair<string, string>(HybridTag, "English + 日本語"));
            return list;
        }

        private static bool TryGetHybridTags(out string en, out string ja)
        {
            var tags = OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();
            en = tags.FirstOrDefault(t => t.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                 ?? tags.FirstOrDefault(t => t.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            ja = tags.FirstOrDefault(t => t.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
            return en != null && ja != null;
        }

        private OcrEngine GetEngine(string tag)
        {
            if (_engines.TryGetValue(tag, out var e)) return e;
            e = OcrEngine.TryCreateFromLanguage(new WinLanguage(tag));
            if (e == null)
                throw new InvalidOperationException(
                    $"Chưa cài gói OCR cho ngôn ngữ '{tag}'.\n" +
                    $"Chạy PowerShell (Admin): Add-WindowsCapability -Online -Name \"Language.OCR~~~{tag}~0.0.1.0\"");
            _engines[tag] = e;
            return e;
        }

        public async Task<string> RecognizeAsync(Bitmap source, AppSettings opt)
        {
            bool hybrid = opt.LanguageTag == HybridTag;
            string enTag = null, jaTag = null;
            if (hybrid && !TryGetHybridTags(out enTag, out jaTag))
                throw new InvalidOperationException(
                    "Chế độ English + 日本語 cần cài cả gói OCR tiếng Anh (en-US) và tiếng Nhật (ja-JP).");
            Func<SoftwareBitmap, Task<List<List<WordBox>>>> recognize = async sbmp =>
            {
                if (!hybrid) return await RecognizeWordsAsync(GetEngine(opt.LanguageTag), sbmp);
                var en = await RecognizeWordsAsync(GetEngine(enTag), sbmp);
                var ja = await RecognizeWordsAsync(GetEngine(jaTag), sbmp);
                return TextLayout.MergeHybrid(en, ja);
            };

            double scale = Math.Max(1, opt.Scale);
            var lines = await RecognizeAtScaleAsync(source, opt, scale, recognize);

            // Chữ nhỏ (VD: font Eclipse mặc định ~ 7px) thì Windows OCR đọc sai nhiều.
            // Đo chiều cao dòng ở lần đầu, nếu còn nhỏ thì phóng to thêm và OCR lại.
            if (opt.AutoScale)
            {
                double h = TextLayout.MedianLineHeight(lines);
                double target = h > 0 ? scale * TargetLineHeight / h : scale * 2;
                double next = Math.Min(MaxAutoScale, target);
                if (next > scale * 1.25)
                {
                    var again = await RecognizeAtScaleAsync(source, opt, next, recognize);
                    if (TextLayout.CharCount(again) >= TextLayout.CharCount(lines) * 0.8) lines = again;
                }
            }
            return TextLayout.Build(lines, opt);
        }

        /// <summary>Chiều cao dòng (px, sau khi phóng to) mà Windows OCR đọc tốt nhất.</summary>
        private const double TargetLineHeight = 40;
        private const double MaxAutoScale = 6;

        private static async Task<List<List<WordBox>>> RecognizeAtScaleAsync(
            Bitmap source, AppSettings opt, double scale, Func<SoftwareBitmap, Task<List<List<WordBox>>>> recognize)
        {
            using (var prepared = Preprocess(source, opt, scale))
            {
                var sbmp = await ToSoftwareBitmapAsync(prepared);
                try { return await recognize(sbmp); }
                finally { sbmp.Dispose(); }
            }
        }

        private static async Task<List<List<WordBox>>> RecognizeWordsAsync(OcrEngine engine, SoftwareBitmap sbmp)
        {
            var result = await engine.RecognizeAsync(sbmp);
            return result.Lines.Select(l => l.Words.Select(w =>
                new WordBox(w.Text, w.BoundingRect.X, w.BoundingRect.Y,
                            w.BoundingRect.Width, w.BoundingRect.Height)).ToList()).ToList();
        }

        // ---------- Tiền xử lý ảnh ----------

        private static Bitmap Preprocess(Bitmap src, AppSettings opt, double scale)
        {
            double maxSide = Math.Max(src.Width, src.Height);
            double limit = OcrEngine.MaxImageDimension;
            if (maxSide * scale > limit) scale = limit / maxSide;

            int w = Math.Max(1, (int)(src.Width * scale));
            int h = Math.Max(1, (int)(src.Height * scale));

            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            using (var attr = new ImageAttributes())
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY); // tránh viền mờ ở mép ảnh
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }

            if (opt.AutoInvertDark && IsDark(dst)) Invert(dst);
            if (opt.CleanBackground) CleanBackground(dst);
            return dst;
        }

        /// <summary>
        /// Chuyển sang ảnh xám và kéo giãn tương phản: nền highlight dòng hiện tại, ký hiệu khoảng trắng
        /// mờ của Eclipse (^ » ↵ ·) thành trắng hẳn; chữ màu (keyword tím, chuỗi xanh, comment xanh lá) thành gần đen.
        /// </summary>
        private static void CleanBackground(Bitmap bmp)
        {
            const int black = 60, white = 200;
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int i = 0; i < bytes.Length; i += 4)
                {
                    int lum = (bytes[i] * 114 + bytes[i + 1] * 587 + bytes[i + 2] * 299) / 1000;
                    int v = (lum - black) * 255 / (white - black);
                    byte b = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                    bytes[i] = bytes[i + 1] = bytes[i + 2] = b;
                    bytes[i + 3] = 255;
                }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static bool IsDark(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                long sum = 0, count = 0;
                for (int i = 0; i < bytes.Length; i += 4 * 7) // lấy mẫu thưa cho nhanh
                {
                    sum += (bytes[i] * 114 + bytes[i + 1] * 587 + bytes[i + 2] * 299) / 1000;
                    count++;
                }
                return count > 0 && sum / count < 110;
            }
            finally { bmp.UnlockBits(data); }
        }

        private static void Invert(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int i = 0; i < bytes.Length; i += 4)
                {
                    bytes[i] = (byte)(255 - bytes[i]);
                    bytes[i + 1] = (byte)(255 - bytes[i + 1]);
                    bytes[i + 2] = (byte)(255 - bytes[i + 2]);
                }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
        }
    }
}
