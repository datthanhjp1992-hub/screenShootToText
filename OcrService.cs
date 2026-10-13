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
            var engine = hybrid ? null : GetEngine(opt.LanguageTag);

            double scale;
            using (var prepared = Preprocess(source, opt, out scale))
            {
                var sbmp = await ToSoftwareBitmapAsync(prepared);
                try
                {
                    List<List<WordBox>> lines;
                    if (hybrid)
                    {
                        var en = await RecognizeWordsAsync(GetEngine(enTag), sbmp);
                        var ja = await RecognizeWordsAsync(GetEngine(jaTag), sbmp);
                        lines = TextLayout.MergeHybrid(en, ja);
                    }
                    else lines = await RecognizeWordsAsync(engine, sbmp);
                    return TextLayout.Build(lines, opt);
                }
                finally
                {
                    sbmp.Dispose();
                }
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

        private static Bitmap Preprocess(Bitmap src, AppSettings opt, out double scale)
        {
            scale = Math.Max(1, opt.Scale);
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
            return dst;
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
