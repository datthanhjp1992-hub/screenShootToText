using Xunit;

namespace ScreenOcr.Tests
{
    public class CodeFixerTests
    {
        [Theory]
        // ":" cuối câu lệnh -> ";"
        [InlineData("int x = 0:", "int x = 0;")]
        [InlineData("    foo(a, b):", "    foo(a, b);")]
        [InlineData("arr[i]:", "arr[i];")]
        [InlineData("i++:", "i++;")]
        [InlineData("return x:", "return x;")]
        [InlineData("import java.util.List:", "import java.util.List;")]
        [InlineData("int x = 0: // comment", "int x = 0; // comment")]
        // giữ nguyên ":" hợp lệ
        [InlineData("case 1:", "case 1:")]
        [InlineData("default:", "default:")]
        [InlineData("outer:", "outer:")]
        [InlineData("if x == 1:", "if x == 1:")]
        [InlineData("def foo(a):", "def foo(a):")]
        [InlineData("Note:", "Note:")]
        [InlineData("a ? b :", "a ? b :")]
        [InlineData("std::", "std::")]
        public void FixesTrailingColon(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Theory]
        [InlineData("int x = 1O0;", "int x = 100;")]
        [InlineData("a[2l5]", "a[215]")]
        [InlineData("x = 1I;", "x = 11;")]
        [InlineData("long v = 10l;", "long v = 10l;")]    // hậu tố long
        [InlineData("long v = 10L;", "long v = 10L;")]
        [InlineData("int l1 = O1;", "int l1 = O1;")]      // identifier: không sửa
        [InlineData("0x1F", "0x1F")]
        [InlineData("1.5f", "1.5f")]
        [InlineData("2つ目", "2つ目")]
        public void FixesDigitsMisreadAsLetters(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Theory]
        [InlineData("s = \"1O0:\";", "s = \"1O0:\";")]            // không sửa trong chuỗi
        [InlineData("c = '1':", "c = '1';")]
        [InlineData("x = 1; // 1O0:", "x = 1; // 1O0:")]           // không sửa trong comment
        [InlineData("// int x = 0:", "// int x = 0:")]
        [InlineData(" * int x = 0:", " * int x = 0:")]
        [InlineData("s = \"a\\\"1O\";", "s = \"a\\\"1O\";")]       // chuỗi có ký tự escape
        public void LeavesStringsAndCommentsAlone(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Fact]
        public void FixWorksOnMultipleLines()
        {
            Assert.Equal("int a = 100;\r\ncase 1:", CodeFixer.Fix("int a = 1O0:\r\ncase 1:"));
        }

        [Theory]
        [InlineData("x.equa1s(y);", "x.equals(y);")]
        [InlineData("getPaymentDead1ine()", "getPaymentDeadline()")]
        [InlineData("a.p1usDays(21)", "a.plusDays(21)")]
        [InlineData("getReceivab1esAssignmentDate()", "getReceivablesAssignmentDate()")]
        [InlineData("CMUTStringUti1.isEmpty(x)", "CMUTStringUtil.isEmpty(x)")]
        [InlineData("new ServiceImp1()", "new ServiceImpl()")]
        [InlineData("getPicName1()", "getPicName1()")]               // số thật: giữ nguyên
        [InlineData("getPicName1Kana()", "getPicName1Kana()")]
        [InlineData("int x1 = v2;", "int x1 = v2;")]
        [InlineData("int 0xa1b = 1;", "int 0xa1b = 1;")]             // hex: không phải identifier
        public void FixesLetterLMisreadAsOne(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Theory]
        [InlineData("if ( DMFCOutputDocument.DOCUMENT TYPE Ie3.equa1s(p) ) {",
                    "if ( DMFCOutputDocument.DOCUMENT_TYPE_103.equals(p) ) {")]
        [InlineData("x = Foo.MAX VALUE;", "x = Foo.MAX_VALUE;")]
        [InlineData("x = Foo.MAX_VALUE;", "x = Foo.MAX_VALUE;")]
        [InlineData("Foo.DOCUMENT TYPE lez.", "Foo.DOCUMENT_TYPE lez.")]   // phần không chắc chắn: giữ nguyên
        [InlineData("s = \"A.B C\";", "s = \"A.B C\";")]
        public void JoinsConstantsSplitBySpaces(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Theory]
        [InlineData("LocalDate.parse(a, f) .p1usDays(21) .format(f);", "LocalDate.parse(a, f).plusDays(21).format(f);")]
        [InlineData("buff.add(data.getName() ;", "buff.add(data.getName();")]
        [InlineData("    .map(x -> x)", "    .map(x -> x)")]                // dòng nối tiếp: giữ thụt lề
        [InlineData("for (;;) {", "for (;;) {")]
        public void RemovesStraySpaces(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Fact]
        public void AddsMissingSemicolonsAndParens()
        {
            var input = string.Join("\r\n",
                "buff.clear();",
                "buff.add(data.getName() ;",
                "buff.add(data.getNameKana())",
                "buff.add(data.getZipCode());",
                "foo(a,",
                "    b)",
                "    .bar();",
                "if (x)",
                "    y();",
                "@Override",
                "public void run()",
                "{",
                "}");
            var expected = string.Join("\r\n",
                "buff.clear();",
                "buff.add(data.getName());",
                "buff.add(data.getNameKana());",
                "buff.add(data.getZipCode());",
                "foo(a,",
                "    b)",
                "    .bar();",
                "if (x)",
                "    y();",
                "@Override",
                "public void run()",
                "{",
                "}");
            Assert.Equal(expected, CodeFixer.Fix(input));
        }

        [Fact]
        public void KeepsCommentWhenAddingSemicolon()
        {
            var input = "a();\r\nb();\r\nc() // note\r\nd();";
            Assert.Equal("a();\r\nb();\r\nc(); // note\r\nd();", CodeFixer.Fix(input));
        }

        [Fact]
        public void DoesNotAddSemicolonsToPython()
        {
            var input = "print(a)\r\nfoo(b)\r\nbar(c)\r\nx = 1";
            Assert.Equal(input, CodeFixer.Fix(input));
        }

        [Theory]
        [InlineData("x = Foo.DOCUMENT_TYPE_10@;", "x = Foo.DOCUMENT_TYPE_100;")]   // số 0 có chấm -> "@"
        [InlineData("x = Foo.DOCUMENT_TYPE_1@1;", "x = Foo.DOCUMENT_TYPE_101;")]
        [InlineData("int a = 1@;", "int a = 10;")]
        [InlineData("@Override", "@Override")]
        [InlineData("getFileName( )", "getFileName()")]
        [InlineData("if ( a ) {", "if ( a ) {")]                                   // style "( a )" giữ nguyên
        public void FixesTesseractSymbols(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.FixLine(input));
        }

        [Theory]
        [InlineData("    /f 対面パターン", "    // 対面パターン")]
        [InlineData("    {f/f StHN--", "    // StHN--")]
        [InlineData("    Jf TREND", "    // TREND")]
        [InlineData("ff = foo();", "ff = foo();")]                                 // biến tên ff: giữ nguyên
        [InlineData("} else aif ( x ) {", "} else if ( x ) {")]
        [InlineData("if ( x ) £", "if ( x ) {")]
        [InlineData("t", "}")]
        [InlineData("    t", "    }")]
        public void FixesTesseractNoise(string input, string expected)
        {
            Assert.Equal(expected, CodeFixer.Fix(input));
        }

        [Fact]
        public void JoinsIdentifierSplitByTesseract()
        {
            var input = "buff.add(\"1\");\r\nbuf f.add(\"2\");\r\nint a = b;";
            Assert.Equal("buff.add(\"1\");\r\nbuff.add(\"2\");\r\nint a = b;", CodeFixer.Fix(input));
        }

        /// <summary>Kết quả Tesseract thật (ảnh Eclipse font nhỏ người dùng gửi) -> sau khi sửa.</summary>
        [Fact]
        public void RealWorldTesseractSample()
        {
            var input = string.Join("\r\n",
                "if ( DMFCOutputDocument.DOCUMENT_TYPE_103.equals(parameter.getDocumentType()) ) {",
                "    buff.add(\"1\");",
                "} else aif ( DMFCOutputDocument .DOCUMENT_TYPE_10@.equals(parameter.getDocumentType()) ) {",
                "    // FBR.",
                "    buf f.add(\"2\");",
                "} else if ( DMFCOutputDocument .DOCUMENT_TYPE_1@2.equals(parameter.getDocumentType()) ) £",
                "    {f/f StHN--",
                "    buf f.add(\"4\");",
                "}");
            var expected = string.Join("\r\n",
                "if ( DMFCOutputDocument.DOCUMENT_TYPE_103.equals(parameter.getDocumentType()) ) {",
                "    buff.add(\"1\");",
                "} else if ( DMFCOutputDocument.DOCUMENT_TYPE_100.equals(parameter.getDocumentType()) ) {",
                "    // FBR.",
                "    buff.add(\"2\");",
                "} else if ( DMFCOutputDocument.DOCUMENT_TYPE_102.equals(parameter.getDocumentType()) ) {",
                "    // StHN--",
                "    buff.add(\"4\");",
                "}");
            Assert.Equal(expected, CodeFixer.Fix(input));
        }

        /// <summary>Kết quả OCR thật (ảnh Eclipse người dùng gửi) -> sau khi sửa.</summary>
        [Fact]
        public void RealWorldEclipseSample()
        {
            var input = string.Join("\r\n",
                "buff.clear();",
                "",
                "buff.add(data.getPurchaseNo());",
                "buff.add(data.getName() ;",
                "buff.add(data.getNameKana())",
                "buff.add(data.getPicName1());",
                "buff.add(data.getPicName1Kana())",
                "buff.add(data.getReceivab1esAssignmentDate());",
                "",
                "if ( CMUTStringUti1.isEmpty(parameter.getPaymentDead1ine()) ) {",
                "    DateTimeFormatter formatter = DateTimeFormatter.ofPattern(\"yyyyMMdd\");",
                "    String result = LocalDate.parse(parameter.getSendDay(), formatter) .p1usDays(21) .format(formatter);",
                "    buff.add(result);",
                "} else {",
                "    buff.add(parameter.getPaymentDead1ine());",
                "}",
                "if ( DMFCOutputDocument.DOCUMENT TYPE Ie3.equa1s(parameter.getDocumentType()) ) {",
                "    buff.add(\"1\");",
                "}");
            var expected = string.Join("\r\n",
                "buff.clear();",
                "",
                "buff.add(data.getPurchaseNo());",
                "buff.add(data.getName());",
                "buff.add(data.getNameKana());",
                "buff.add(data.getPicName1());",
                "buff.add(data.getPicName1Kana());",
                "buff.add(data.getReceivablesAssignmentDate());",
                "",
                "if ( CMUTStringUtil.isEmpty(parameter.getPaymentDeadline()) ) {",
                "    DateTimeFormatter formatter = DateTimeFormatter.ofPattern(\"yyyyMMdd\");",
                "    String result = LocalDate.parse(parameter.getSendDay(), formatter).plusDays(21).format(formatter);",
                "    buff.add(result);",
                "} else {",
                "    buff.add(parameter.getPaymentDeadline());",
                "}",
                "if ( DMFCOutputDocument.DOCUMENT_TYPE_103.equals(parameter.getDocumentType()) ) {",
                "    buff.add(\"1\");",
                "}");
            Assert.Equal(expected, CodeFixer.Fix(input));
        }
    }
}
