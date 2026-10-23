using System.Text;
using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests;

[TestClass]
public sealed class EscapeUnitTests
{
    static void TestEscapeUnescape(string content)
    {
        var sb = new StringBuilder(content.Length * 2);
        var sbUnescaped = new StringBuilder(content.Length);
        foreach (var asciiOnly in new[] { false, true })
        {
            sb.Clear(); sbUnescaped.Clear(); // Clear to avoid invalid length

            int escapeCount = SJEscape.Escape(sb, static (s, c) => s.Append(c), content, asciiOnly);
            Assert.AreEqual(sb.Length, escapeCount, $"Expected {sb.Length}, got {escapeCount} for:\n{sb}");
            if (asciiOnly)
            {
                Assert.IsTrue(sb.ToString().All(c => c <= sbyte.MaxValue), $"All characters must be ascii on escaped string\n> '{content}'");
            }

            int unescapeCount = SJEscape.Unescape(sbUnescaped, static (s, c) => s.Append(c), sb.ToString());
            Assert.AreEqual(sbUnescaped.Length, unescapeCount, $"Expected {sbUnescaped.Length}, got {unescapeCount} for :\n{sbUnescaped}");

            // content Escape -> content Unescape must equal itself
            Assert.AreEqual(content, sbUnescaped.ToString());
        }
    }

    public static IEnumerable<string[]> DataProcessors => [[EscapeContent], [EscapeInvalidSurrogate], [EscapeContentAsciiSequence], [EscapeInvalidUnescape]];
    public static IEnumerable<string[]> TruncatedDataProcessors => TestEx.CreateTruncatedDataProcessors(DataProcessors);

    [TestMethod]
    [DataRow("")]
    [DataRow(EscapeContent)]
    [Timeout(TestTimeouts.Short)]
    public void TestBasic(string data) => TestEscapeUnescape(data);

    [TestMethod]
    [DynamicData(nameof(DataProcessors))]
    [Timeout(TestTimeouts.Short)]
    public void TestWith(string data) => TestEscapeUnescape(data);

    [TestMethod]
    [DynamicData(nameof(TruncatedDataProcessors))]
    [Timeout(TestTimeouts.Short)]
    public void TestTruncated(string data) => TestEscapeUnescape(data);

    [TestMethod]
    [DataRow(EscapeInvalidUnescape)]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(ArgumentException))]
    public void TestInvalidUnescape(string data) => SJEscape.Unescape(data, allowInvalidEscapes: false);

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestInvalidUnescape()
    {
        Assert.IsTrue(!SJEscape.Unescape(EscapeInvalidUnescape).Contains('\\'), "Behaviour : Should not contain '\\' token from invalid escapes (as it's removed and the invalid escape is written as-is)");
    }
}
