using System;
using System.Collections.Generic;
using System.IO;
using Tesseract;

namespace ScreenOcr
{
    /// <summary>
    /// OCR offline bằng Tesseract 5 (LSTM). Đọc code (ký hiệu { } ( ) ; " _ ...) chính xác hơn
    /// Windows OCR rất nhiều, nhưng đọc tiếng Nhật kém => chữ Nhật vẫn lấy từ Windows OCR (ja).
    /// Dữ liệu ngôn ngữ: tessdata\eng.traineddata cạnh file exe.
    /// </summary>
    public sealed class TesseractOcr : IDisposable
    {
        private TesseractEngine _engine;
        private readonly object _lock = new object();

        public static string DataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

        public static bool IsAvailable => File.Exists(Path.Combine(DataDir, "eng.traineddata"));

        /// <summary>Nhận dạng ảnh PNG, trả về các dòng gồm từ + khung bao + độ tin cậy (0–100).</summary>
        public List<List<WordBox>> Recognize(byte[] png)
        {
            lock (_lock) // TesseractEngine không xử lý song song được
            {
                if (_engine == null)
                {
                    try
                    {
                        _engine = new TesseractEngine(DataDir, "eng", EngineMode.LstmOnly);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            "Không khởi động được Tesseract: " + ex.Message + "\n" +
                            "Kiểm tra thư mục x64\\ và tessdata\\ cạnh ScreenOcr.exe (copy đủ cả thư mục khi chuyển máy), " +
                            "hoặc cài Microsoft Visual C++ Redistributable 2015–2022 (x64).", ex);
                    }
                }

                var lines = new List<List<WordBox>>();
                using (var pix = Pix.LoadFromMemory(png))
                using (var page = _engine.Process(pix, PageSegMode.SingleBlock))
                using (var it = page.GetIterator())
                {
                    it.Begin();
                    List<WordBox> line = null;
                    do
                    {
                        if (line == null || it.IsAtBeginningOf(PageIteratorLevel.TextLine))
                        {
                            line = new List<WordBox>();
                            lines.Add(line);
                        }
                        var text = it.GetText(PageIteratorLevel.Word);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;
                        line.Add(new WordBox(text.Trim(), r.X1, r.Y1, r.Width, r.Height)
                        {
                            Confidence = it.GetConfidence(PageIteratorLevel.Word),
                        });
                    } while (it.Next(PageIteratorLevel.Word));
                }
                lines.RemoveAll(l => l.Count == 0);
                return lines;
            }
        }

        public void Dispose()
        {
            lock (_lock) { _engine?.Dispose(); _engine = null; }
        }
    }
}
