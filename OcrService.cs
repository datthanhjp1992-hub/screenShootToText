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

        /// <summary>Các ngôn ngữ OCR đã cài trên máy: (tag, tên hiển thị).</summary>
        public static List<KeyValuePair<string, string>> GetLanguages()
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => new KeyValuePair<string, string>(l.LanguageTag, l.DisplayName))
                .ToList();
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
            var engine = GetEngine(opt.LanguageTag);

            double scale;
            using (var prepared = Preprocess(source, opt, out scale))
            {
                var sbmp = await ToSoftwareBitmapAsync(prepared);
                try
                {
                    var result = await engine.RecognizeAsync(sbmp);
                    var lines = result.Lines.Select(l => l.Words.Select(w =>
                        new WordBox(w.Text, w.BoundingRect.X, w.BoundingRect.Y,
                                    w.BoundingRect.Width, w.BoundingRect.Height)));
                    return TextLayout.Build(lines, opt);
                }
                finally
                {
                    sbmp.Dispose();
                }
            }
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
