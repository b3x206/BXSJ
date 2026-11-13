using System.Text;
using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class EscapeUnitTests
{
    static void TestEscapeUnescape(string content)
    {
        // The mojibake may occur if "sb" has other chars that may mean something else in the context that it's writing.
        // Make the conditions more realistic in that regard as how it's used, rather than to create the string in isolation.
        const char Pad = '"';
        // Content with prefix and postfix
        var comparedContent = new StringBuilder(content.Length + 2);
        comparedContent.Append(Pad).Append(content).Append(Pad);

        var sb = new StringBuilder(content.Length * 2);
        var sbUnescaped = new StringBuilder(content.Length);
        foreach (var asciiOnly in new[] { false, true })
        {
            sb.Clear(); sbUnescaped.Clear(); // Clear to avoid invalid length

            sb.Append(Pad);
            int escapeCount = SJEscape.Escape(sb, static (s, c) => s.Append(c), content, asciiOnly);
            sb.Append(Pad);
            Assert.AreEqual(sb.Length - 2, escapeCount, $"Expected {sb.Length - 2}, got {escapeCount} for:\n{sb}");

            string notFixedSb = sb.ToString(1, sb.Length - 2);
            if (asciiOnly)
            {
                Assert.IsTrue(notFixedSb.All(c => c <= sbyte.MaxValue), $"All characters must be ascii on escaped string\n> '{notFixedSb}'");
            }

            sbUnescaped.Append(Pad);
            int unescapeCount = SJEscape.Unescape(sbUnescaped, static (s, c) => s.Append(c), notFixedSb);
            sbUnescaped.Append(Pad);
            Assert.AreEqual(sbUnescaped.Length - 2, unescapeCount, $"Expected {sbUnescaped.Length - 2}, got {unescapeCount} for :\n{sbUnescaped}");

            // content Escape -> content Unescape must equal itself
            Assert.AreEqual(comparedContent.ToString(), sbUnescaped.ToString());
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
    [Timeout(TestTimeouts.Short)]
    [DynamicData(
        nameof(StringDataProvider), typeof(TestData),
        DynamicDataDisplayName = nameof(StringDataProviderName),
        DynamicDataDisplayNameDeclaringType = typeof(TestData)
    )]
    public void TestBasicEx(string d) => TestEscapeUnescape(d);

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
    public void TestInvalidUnescape(string data)
    {
        Assert.IsFalse(SJEscape.Unescape(data).Contains('\\'), $"Behaviour : Should not contain '\\' from invalid escapes.\n{data}");
    }

    [TestMethod]
    [DataRow(EscapeInvalidUnescape)]
    [Timeout(TestTimeouts.Short)]
    public void TestInvalidUnescapeThrows(string data) => 
        Assert.Throws<ArgumentException>(() => SJEscape.Unescape(data, allowInvalidEscapes: false));
}
