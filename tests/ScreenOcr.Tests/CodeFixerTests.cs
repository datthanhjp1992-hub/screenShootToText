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
    }
}
