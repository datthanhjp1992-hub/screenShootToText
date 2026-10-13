using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ScreenOcr.Tests
{
    public class TextLayoutTests
    {
        private const double CharW = 10, LineH = 20, Pitch = 30;

        /// <summary>
        /// Giả lập kết quả OCR của 1 dòng font monospace: mỗi từ (tách bởi dấu cách) thành 1 WordBox
        /// đúng vị trí cột của nó. <paramref name="row"/> là số thứ tự dòng trên màn hình.
        /// </summary>
        internal static List<WordBox> Line(string text, int row, double x0 = 0)
        {
            var words = new List<WordBox>();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == ' ') { i++; continue; }
                int start = i;
                while (i < text.Length && text[i] != ' ') i++;
                words.Add(new WordBox(text.Substring(start, i - start),
                    x0 + start * CharW, row * Pitch, (i - start) * CharW, LineH));
            }
            return words;
        }

        private static AppSettings Opt(bool fix = false) => new AppSettings { FixCodeChars = fix };

        [Fact]
        public void RestoresIndentAndSpacing()
        {
            var lines = new[]
            {
                Line("public void run() {", 0),
                Line("    int x = 1;", 1),
                Line("        if (x > 0) {", 2),
                Line("}", 3),
            };
            var text = TextLayout.Build(lines, Opt());
            Assert.Equal("public void run() {\r\n    int x = 1;\r\n        if (x > 0) {\r\n}", text);
        }

        [Fact]
        public void RestoresBlankLines()
        {
            var lines = new[] { Line("int a = 1;", 0), Line("int b = 2;", 1), Line("int c = 3;", 3) };
            var text = TextLayout.Build(lines, Opt());
            Assert.Equal("int a = 1;\r\nint b = 2;\r\n\r\nint c = 3;", text);
        }

        [Fact]
        public void MergesOcrLinesOnSameRow()
        {
            // engine tách comment cuối dòng thành 1 dòng OCR riêng
            var code = Line("int x = 1;", 0);
            var comment = Line("           // note", 0).ToList();
            var text = TextLayout.Build(new[] { comment, code }, Opt());
            Assert.Equal("int x = 1; // note", text);
        }

        [Fact]
        public void StripsSeparateLineNumbers()
        {
            var lines = new[]
            {
                Line("10  public class A {", 0),
                Line("11      int x;", 1),
                Line("12  }", 2),
            };
            var text = TextLayout.Build(lines, Opt());
            Assert.Equal("public class A {\r\n    int x;\r\n}", text);
        }

        [Theory]
        [InlineData("for")]
        [InlineData("final")]
        [InlineData("else")]
        [InlineData("double")]
        [InlineData("break;")]
        [InlineData("boolean")]
        [InlineData("long")]
        [InlineData("default:")]
        public void StripsLineNumberFusedWithKeyword(string keyword)
        {
            // trước đây regex bỏ qua các token bắt đầu bằng e/b/l/f/d/x...
            var lines = new[]
            {
                Line("10 int a;", 0),
                Line("11" + keyword, 1),
                Line("12 int b;", 2),
            };
            var text = TextLayout.Build(lines, Opt());
            Assert.Equal(keyword, text.Split("\r\n")[1].Trim());
        }

        [Fact]
        public void KeepsCodeWhenNoLineNumbers()
        {
            var lines = new[] { Line("int a = 1;", 0), Line("100;", 1), Line("int b;", 2) };
            var text = TextLayout.Build(lines, Opt());
            Assert.Contains("100;", text);
        }

        [Fact]
        public void NormalizesFullWidth()
        {
            Assert.Equal("(a, b); \"x\"", TextLayout.NormalizeChars("（ａ，　ｂ）； “x”"));
        }

        [Fact]
        public void BuildAppliesCodeFixerWhenEnabled()
        {
            var lines = new[] { Line("int x = 1O0:", 0) };
            Assert.Equal("int x = 100;", TextLayout.Build(lines, Opt(fix: true)));
            Assert.Equal("int x = 1O0:", TextLayout.Build(lines, Opt(fix: false)));
        }

        // ---------- MergeHybrid ----------

        [Fact]
        public void HybridTakesJapaneseFromJaAndCodeFromEn()
        {
            // Dòng: int x = 0; // 初期化
            var en = new List<List<WordBox>>
            {
                new List<WordBox>
                {
                    new WordBox("int", 0, 0, 30, 20), new WordBox("x", 40, 0, 10, 20),
                    new WordBox("=", 60, 0, 10, 20), new WordBox("0;", 80, 0, 20, 20),
                    new WordBox("//", 110, 0, 20, 20),
                    new WordBox("ネ刀其月化", 140, 0, 60, 20), // rác: en đọc chữ Nhật
                },
            };
            var ja = new List<List<WordBox>>
            {
                new List<WordBox>
                {
                    new WordBox("int", 0, 0, 30, 20), new WordBox("x=O:", 40, 0, 60, 20), // ja đọc ký hiệu kém
                    new WordBox("//", 110, 0, 20, 20),
                    new WordBox("初", 140, 0, 20, 20), new WordBox("期", 160, 0, 20, 20), new WordBox("化", 180, 0, 20, 20),
                },
            };
            var merged = TextLayout.MergeHybrid(en, ja);
            var text = TextLayout.Build(merged, Opt());
            Assert.Equal("int x = 0; // 初期化", text);
        }

        [Fact]
        public void HybridFillsAsciiThatEnMissed()
        {
            var en = new List<List<WordBox>> { new List<WordBox> { new WordBox("foo();", 0, 0, 60, 20) } };
            var ja = new List<List<WordBox>>
            {
                new List<WordBox> { new WordBox("foo();", 0, 0, 60, 20) },
                new List<WordBox> { new WordBox("bar();", 0, 30, 60, 20) },
            };
            var text = TextLayout.Build(TextLayout.MergeHybrid(en, ja), Opt());
            Assert.Equal("foo();\r\nbar();", text);
        }

        // ---------- Stitch ----------

        [Fact]
        public void StitchRemovesOverlappingLines()
        {
            var a = "class A {\r\n    void f() {\r\n        int x = 1;\r\n        int y = 2;";
            var b = "        int x = 1;\r\n        int y  = 2;\r\n        return;\r\n    }";
            Assert.Equal(
                "class A {\r\n    void f() {\r\n        int x = 1;\r\n        int y = 2;\r\n        return;\r\n    }",
                TextLayout.Stitch(new[] { a, b }));
        }

        [Fact]
        public void StitchDoesNotMergeTrivialBraces()
        {
            Assert.Equal("a();\r\n}\r\n}\r\nb();", TextLayout.Stitch(new[] { "a();\r\n}", "}\r\nb();" }));
        }

        [Fact]
        public void StitchSkipsEmptyTexts()
        {
            Assert.Equal("a();\r\nb();", TextLayout.Stitch(new[] { "a();", "", null, "b();" }));
        }
    }
}
