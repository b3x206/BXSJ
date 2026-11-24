using System.Text;
using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class EscapeUnitTests
{
    static void TestEscapeUnescape(string? content)
    {
        // Make the conditions more realistic in that regard as how it's used
        // rather than to create the string in isolation. This will complicate things a little bit more.
        const char Pad = '"';
        int contentLength = content?.Length ?? 0;
        // Content with prefix and postfix
        var comparedContent = new StringBuilder(contentLength + 2);
        comparedContent.Append(Pad).Append(content).Append(Pad);

        var sb = new StringBuilder(contentLength * 2);
        var sbUnescaped = new StringBuilder(contentLength);
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

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow(EscapeContent)]
    [Timeout(TestTimeouts.Short)]
    public void Basic(string? data) => TestEscapeUnescape(data);
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(
        nameof(StringDatas), typeof(TestData),
        DynamicDataDisplayName = nameof(StringDatasDisplayName),
        DynamicDataDisplayNameDeclaringType = typeof(TestData)
    )]
    public void WithDatas1(string d) => TestEscapeUnescape(d);
    [TestMethod]
    [DynamicData(nameof(EscapeDatas), typeof(TestData))]
    [Timeout(TestTimeouts.Short)]
    public void WithDatas2(string data) => TestEscapeUnescape(data);

    [TestMethod]
    [DynamicData(nameof(TruncatedEscapeDatas), typeof(TestData))]
    [Timeout(TestTimeouts.Short)]
    public void WithTruncatedDatas(string data) => TestEscapeUnescape(data);

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void RejectsNullAppender()
    {
        Assert.Throws<ArgumentNullException>(() => SJEscape.Unescape<object?>(null, null, ""));
        Assert.Throws<ArgumentNullException>(() => SJEscape.Escape<object?>(null, null, ""));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void AcceptsNullSelf()
    {
        SJEscape.Unescape<object?>(null, (_, c) => { }, "abcdef");
        SJEscape.Escape<object?>(null, (_, c) => { }, "abcdef");
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void EscapesControlCharacters()
    {
        string cc = string.Join(null, Enumerable.Range(0, 0x1F + 1).Select(Convert.ToChar));
        Assert.DoesNotContain(char.IsControl, SJEscape.Escape(cc), "Escape must not contain any control characters");
        Assert.DoesNotContain(cc.Contains, SJEscape.Escape(cc), "Escape must not contain any characters from itself");
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void EscapesSpecifiedCharacters()
    {
        string cc = "\r\t\n\v\b\"\\";
        Assert.DoesNotContain(char.IsControl, SJEscape.Escape(cc), "Escape must not contain any control characters");
        // Contains '\' and '"', but not control characters.
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(InvalidEscapeDatas), typeof(TestData))]
    public void Invalid(string data) => TestEscapeUnescape(data);
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(InvalidEscapesData)] // Specific test that includes only invalid escapes
    public void UnescapeInvalidEscapes(string data)
    {
        Assert.IsFalse(
            SJEscape.Unescape(data, allowInvalidEscapes: true).Contains('\\'),
            $"Must not contain '\\' from invalid escape sequences if it's allowed.\n{data}"
        );
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(InvalidEscapeDatas), typeof(TestData))]
    public void InvalidEscapesThrow(string data) => 
        Assert.Throws<ArgumentException>(() => SJEscape.Unescape(data, allowInvalidEscapes: false));
}
