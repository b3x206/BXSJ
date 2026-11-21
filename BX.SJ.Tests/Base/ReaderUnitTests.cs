using static BX.SJ.Tests.TestData;
using static BX.SJ.Tests.ReaderTester;
using System.Text;
using System.Reflection;
using System.Globalization;

namespace BX.SJ.Tests;

/// <summary>
/// Base unit tests for a <see cref="SJReader"/> that must pass.
/// </summary>
/// <typeparam name="TData">Data reader type for the target <see cref="SJReader"/>.</typeparam>
public abstract class ReaderUnitTests<TData> where TData : ISJDataReader
{
    // -- Config
    public abstract TData FromString(string data);
    public abstract TData FromStream(Stream data, Encoding? enc);
    public virtual TData FromStringWithEncoding(string data, Encoding enc)
    {
        // Do not override this to cheat it. If you can encode data
        // and read it back with that encoding, then go ahead.
        // Otherwise, just implement FromStream.
        var ms = new MemoryStream(enc.GetBytes(data));
        return FromStream(ms, enc);
    }
    protected string CreateMaxRecursiveString(string tks, int maxDepth)
    {
        if (string.IsNullOrEmpty(tks))
        {
            throw new ArgumentException($"'{nameof(tks)}' cannot be null or empty.", nameof(tks));
        }
        if (tks.Length != 2)
        {
            throw new ArgumentException($"'{nameof(tks)}' must exactly have two characters, one for opening and one for closing.", nameof(tks));
        }

        if (tks[0] == '{')
        {
            // keys are needed for nesting object, with the exception of the last object
            // {"a":{"a":...
            return string.Concat(tks[0],
                string.Join("", Enumerable.Repeat($"\"a\":{tks[0]}", maxDepth)),
                string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))
            );
        }
        else
        {
            // [[[...
            return string.Concat(
                string.Join("", Enumerable.Repeat(tks[0], maxDepth + 1)),
                string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))
            );
        }
    }
    public virtual bool CheckIsStackable() => typeof(TData).IsAssignableTo(typeof(ISJStateStackable));
    public virtual bool AssertIsStackable()
    {
        bool r = CheckIsStackable();
        if (!r) Assert.Inconclusive(@$"Cannot test stack related tests on type {typeof(TData)} as it does not implement {typeof(ISJStateStackable)}.
If this warning isn't desired, override this method to return false and remove this line.");
        return r;
    }

    // - Processors
    /// <summary>
    /// Processors for the encoding parameter in the tests.
    /// Only unicode compatible encodings are allowed, even though theoretically SJ can work fine with just ascii.
    /// <br>This is because the test tests emojis and other stuff.</br>
    /// </summary>
    public static IEnumerable<Encoding[]> EncodedStringProcessors =>
        [[Encoding.UTF8], [Encoding.Unicode], [Encoding.BigEndianUnicode], [Encoding.UTF32]];
    public static string GetEncodedTestName(MethodInfo info, object[] data)
    {
        var data0 = data[0];
        if (ReferenceEquals(data0, Encoding.Unicode))
        {
            return $"{info.Name}_UnicodeEncoding";
        }
        if (ReferenceEquals(data0, Encoding.BigEndianUnicode))
        {
            return $"{info.Name}_BigEndianUnicodeEncoding";
        }
        return $"{info.Name}_{data0}";
    }

    // - Tests
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDatas), typeof(TestData))]
    public void TestRootLiterals(string data, string _) =>
        // Since the empty builder for the sample code is simple but matching, it must equal.
        Assert.AreEqual(data, ReadWithTokens(FromString(data)).ToString());
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonTruncatedRootDatas), typeof(TestData))]
    public void TestTruncatedRootLiterals(string data) =>
        Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(data)));
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("    ")]
    [DataRow("    \t\r\n\v")]
    [Timeout(TestTimeouts.Short)] // !! : Only ASCII whitespace is detected, rest is considered invalid token.
    public void TestEmptyOrWhitespace(string data) => Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(data)));

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(BXSJRootDatas), typeof(TestData))]
    public void TestBXSJRootLiterals(string data, string expected) =>
        Assert.AreEqual(expected, ReadBXSJ(FromString(data)).ToString());
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(BXSJTruncatedRootDatas), typeof(TestData))]
    public void TestTruncatedBXSJRootLiterals(string data) =>
        Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(data)));

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDatas), typeof(TestData))]
    public void TestRootWithComments(string data, string _)
    {
        const string ContentLeft = "// This is a comment!";
        const string ContentRight1 = "// Dear god..";
        const string ContentRight2 = "/* There's more! No. */";
        string CommentLeft = $"{ContentLeft}" + Environment.NewLine;
        string CommentRight = $"{ContentRight1}" + Environment.NewLine + $"{ContentRight2}";

        Assert.AreEqual(data, ReadWithTokens(FromString(data), jsc: true).ToString());
        // One with valid formatting and one that the reader outputs.
        string readerData = $"{CommentLeft}{data}{CommentRight}";
        Assert.AreEqual(readerData, ReadWithTokens(FromString(readerData), jsc: true).ToString());
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDatas), typeof(TestData))]
    public void TestRootWithTypes(string data, string _)
    {
        Assert.AreEqual(data, ReadBXSJWithTokens(FromString(data)).ToString());
        foreach (string type in BXSJTypeDatas.Select(v => v[0]))
        {
            string readerData = $"{type}{data}"; // Read with tokens ignores whitespace
            Assert.AreEqual(readerData, ReadBXSJWithTokens(FromString(readerData)).ToString());
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonDatas), typeof(TestData))]
    public void TestJsonDatas(string d) => ReadWithTokens(FromString(d));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(InvalidJsonDatas), typeof(TestData))]
    public void TestInvalidJsonDatas(string d) => Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(d)));

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(BxsjDatas), typeof(TestData))]
    public void TestBXSJDatas(string d) => ReadBXSJWithTokens(FromString(d));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(InvalidBxsjDatas), typeof(TestData))]
    public void TestInvalidBXSJDatas(string d) => Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(d)));

    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpam(Encoding enc)
    {
        using SJReader reader = new(FromStringWithEncoding(JsonDataFireSpam, enc));
        var root = reader.Read();
        Assert.AreEqual(SJType.Object, root.type, $"Expected root to be object, it is instead this : {root}");
        while (reader.IterateObject(root, out SJReader.Value k, out SJReader.Value v))
        {
            Assert.AreEqual(FireSpamKey, new string(k.Slice()), "Keys must be equal");
            Assert.AreEqual(SJType.Key, k.type);
            Assert.AreEqual(FireSpamValue, new string(v.Slice()), "Values must be equal");
            Assert.AreEqual(SJType.String, v.type);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpamLiteral(Encoding enc)
    {
        // Buffered readers / writers (such as stream based ones) can struggle with this with invalidly truncated read from the resources with other encodings.
        using SJReader reader = new(FromStringWithEncoding($"\"{DataEmojiSpam}\"", enc));
        var root = reader.Read();
        Assert.AreEqual(SJType.String, root.type);
        Assert.IsTrue(root.Slice().SequenceEqual(DataEmojiSpam), "Sliced value must equal the string inside the literal.");
    }

    // Tricky conditions on the comma/colon state / Stacking
    [TestMethod]
    [DataRow("[]", SJReader.DefaultMaxDepth - 1)]
    [DataRow("{}", SJReader.DefaultMaxDepth - 1)]
    [Timeout(TestTimeouts.Short)]
    public void TestValidDepth(string tks, int depth) => ReadWithTokens(FromString(CreateMaxRecursiveString(tks, depth)));
    [TestMethod]
    [DataRow("[]", SJReader.DefaultMaxDepth)]
    [DataRow("{}", SJReader.DefaultMaxDepth)]
    [Timeout(TestTimeouts.Short)]
    public void TestInvalidDepth(string tks, int depth) => Assert.Throws<SJReader.ReaderException>(() =>
        ReadWithTokens(FromString(CreateMaxRecursiveString(tks, depth)))
    );
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"[""hello"" 1 2 3 4 5]")]
    [DataRow(@"{""hello"": 124 ""world!"": ""world: yo"" ""look"": ""no comma! waow bradar please JSON code make no mistakes""}")]
    [DataRow(@"{""hello"" 124, ""gurt"" ""yo"", ""yogurt"" ""yes""}")]
    public void TestCommaColonState(string data) => Assert.Throws<SJReader.ReaderException>(() => ReadWithTokens(FromString(data)));

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""numbers"": {
""normal"": 123124.214124,
""nNormal"": -123124.214124,
""hex"": 0xDEADBEEF,
""sci"": 1e+17,
""octal"": 0o22447,
""binary"": 0b11010011101,
""notVeryValidButAccepted"": 01oo3o5x070e0e00
}}")]
    public void TestAcceptedNumbers(string s) => ReadWithTokens(FromString(s));

    // Invalid Json with settings
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", ""c"": [""d"", ""e"", true, false, 23, { ""c"": false, },], ""ss"": true, }")]
    public void TestTrailingCommas(string s)
    {
        using SJReader r = new(FromString(s));
        r.allowTrailingComma = true;
        ReadWithTokens(r);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true }], 0x3589: true }")]
    public void TestOtherKeyTypes(string s)
    {
        using SJReader r = new(FromString(s));
        r.allowOtherKeyTypes = true;
        ReadWithTokens(r);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true, },], 0x3589: true, }")]
    public void TestAllTolerance(string s)
    {
        using SJReader r = new(FromString(s));
        r.allowTrailingComma = true;
        r.allowOtherKeyTypes = true;
        ReadWithTokens(r);
    }

    // BXSJ
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""key"" /* inline */ : ""value"" }")]
    [DataRow(@"{ ""key"" : /* inline */ ""value"" }")]
    [DataRow(@"{ ""key"" : ""value"" /* trailing */ , ""key2"": ""value2"" }")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */, 4, 5]")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */]")]
    public void TestTrickyComments(string data) => ReadWithTokens(FromString(data), jsc: true);

    // Code Validation
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetRetainsData()
    {
        // Validate whether if JsonData1 is loaded correctly.
        using SJReader reader = new(FromString(JsonData1));
        ReadWithTokens(reader);
        // Position is off by one if all chars are exhausted, which is generally the case.
        Assert.AreEqual(JsonData1.Length, reader.Data.Index);

        // Read again with test
        reader.Reset();
        ReadWithTokens(reader);
        Assert.AreEqual(JsonData1.Length, reader.Data.Index);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(BxsjValidatedDatas), typeof(TestData))]
    public void TestStateValidatedData(string s)
    {
        static bool AssertValidation(SJReader reader, SJReader.Value v)
        {
            // @"assert=\s*(top.)?([^=]+)=(.*)"
            Assert.AreEqual(SJType.Comment, v.type);

            const string start = "assert=";
            ReadOnlySpan<char> s = v.Slice();
            if (!s.StartsWith(start)) return false; // Not an assertion
            s = s[start.Length..];

            const string sep = "=";
            const string stackable = "top.";

            int sepIdx = s.IndexOf(sep);
            ReadOnlySpan<char> sMemberName = s[..sepIdx];
            int stackedIndex = sMemberName.IndexOf(stackable);

            Type type = typeof(SJReader.State);
            object state = reader.S;

            switch (stackedIndex)
            {
                // Must be prefix, but is ignored if not..
                case 0:
                    sMemberName = sMemberName[stackable.Length..];
                    type = typeof(SJReader.State.Stacked);
                    state = reader.S.stack.PeekValue();
                    break;
            }

            string memberName = new(sMemberName.Trim());
            ReadOnlySpan<char> value = s[(sepIdx + sep.Length)..].Trim();

            // We can compare ToString representation (invariant one)
            CultureInfo prevCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var f = type.GetField(memberName, bf);
                if (f is not null)
                {
                    Assert.That.AreEqual(value, f.GetValue(state)?.ToString());
                    return true;
                }
                var p = type.GetProperty(memberName, bf);
                if (p is not null)
                {
                    Assert.That.AreEqual(value, p.GetValue(state)?.ToString());
                    return true;
                }

                string fieldsAndProperties = "";
                try
                {
                    var members = type.GetMembers(bf);
                    fieldsAndProperties = string.Join(", ",
                        members
                            .Where(v => v is FieldInfo)
                            .Select(v => $"{(v as FieldInfo)?.FieldType} {v.Name}")
                        .Concat(
                        members
                            .Where(v => v is PropertyInfo)
                            .Select(v => $"{(v as PropertyInfo)?.PropertyType} {v.Name}"))
                    );
                }
                catch
                {
                    fieldsAndProperties = "";
                }

                Assert.Fail(@$"Field or Property to assert '{memberName}' does not exist on type '{type}'
Fields + Properties available : {fieldsAndProperties}");
                return false;
            }
            finally
            {
                CultureInfo.CurrentCulture = prevCulture;
            }
        }

        using var r = new SJReader(FromString(s))
        {
            allowComments = true
        };
        ReadWithHandler(r, (self, v) =>
        {
            if (v.type == SJType.Comment)
            {
                if (!AssertValidation(self, v)) Console.WriteLine($"Comment with content <{v.Slice()}> does not have an assertion on any state.");
            }
            return true;
        });
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(4)]
    public void TestDisposeTimes(int n)
    {
        // Disposing multiple times shouldn't error.
        SJReader reader = new(FromString(JsonData1));
        Read(reader);
        for (int i = 0; i < n; i++) reader.Dispose();
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestAfterDispose()
    {
        // Data itself is also disposable, but it's assumed that it has all the resources freed up.
        SJReader reader = new(FromString(JsonData1));
        Action<SJReader>[] acts = [
            // Anything that must throw is here (since Action does not match these methods exactly this is done)
            static (r) => r.Read(),
            static (r) => r.Reset(),
            (r) => { if (CheckIsStackable()) r.PushState(); },
            (r) => { if (CheckIsStackable()) r.PopState(); },
            static (r) => r.Data = null, // Will throw InvalidOperationException when disposed
        ];
        SJReader.Value lastValue = default;
        Action<SJReader.Value>[] valActs = [
            static (v) => v.Slice(),
            static (v) => { if (!v.TrySlice(out _)) throw new InvalidOperationException(); },
        ];

        // JsonData1 should have more than 4 elements
        ReadWithHandler(reader, (r, v) =>
        {
            lastValue = v;
            return r.S.totalValueCount <= 4;
        });
        reader.Dispose();

        // SJReader
        for (int i = 0; i < acts.Length; i++)
        {
            Action<SJReader> mustThrow = acts[i];
            Assert.Throws<InvalidOperationException>(() => mustThrow(reader), $"{i} action does not throw InvalidOperationException on disposed reader.");
        }
        // SJReader.Value
        for (int i = 0; i < valActs.Length; i++)
        {
            Action<SJReader.Value> mustThrow = valActs[i];
            Assert.Throws<InvalidOperationException>(() => mustThrow(lastValue), $"{i} value action does not throw InvalidOperationException on disposed reader.");
        }
        for (int i = 0; i < valActs.Length; i++)
        {
            Action<SJReader.Value> mustThrow = valActs[i];
            Assert.Throws<InvalidOperationException>(() => mustThrow(default), $"{i} value action does not throw InvalidOperationException on disposed reader.");
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetClearsState()
    {
        using SJReader reader = new(FromString(JsonDataInvalid));
        ReadWithTokens(reader, throwError: false);

        reader.Reset();
        Assert.That.IsNullOrEmpty(reader.Error, "Error state must reset on Reset");
        Assert.AreEqual(0, reader.Data.Position, "Reader position must be zero after reset");
    }

    // Other functionality
    // Stacking
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void StateStacking()
    {
        if (!AssertIsStackable()) return;

        const string FirstKey = "a", SecondKey = "b", ThirdKey = "c";
        const string TestData = $@"{{
    ""{FirstKey}"": true,
    ""{SecondKey}"": [1, 2, 3, 4, {{ ""test"": true }}],
    ""{ThirdKey}"": false
}}";

        using var r = new SJReader(FromString(TestData));
        const string PushKey = SecondKey, PopKey = ThirdKey;
        string? expectedKey = null;
        bool postPop = false;

        // Pop after second key entry and expect second key again.
        for (var v = r.Read(); !r.S.ended; v = r.Read())
        {
            switch (v.type)
            {
                case SJType.End:
                    break;

                default:
                    Assert.Fail($"Did not expect type {v.type} on root");
                    return;

                case SJType.Object:
                    while (r.IterateObject(v, out var ok, out var ov))
                    {
                        var keySlice = ok.Slice();
                        if (postPop)
                        {
                            Assert.That.AreEqual(expectedKey, keySlice);
                            break;
                        }

                        // When the key is read and the state is to be pushed into, we are already on the next key.
                        // So set the expectedKey to be the next value.
                        if (keySlice.SequenceEqual(PushKey))
                        {
                            Assert.AreEqual(0, r.PushState());
                            // .. to the next key. IterateObject will use the last key found
                            r.DiscardUntil(v.objectDepth);
                            expectedKey = new string(r.Read().Slice());
                        }
                        else if (keySlice.SequenceEqual(PopKey)) { Assert.AreEqual(0, r.PopState()); postPop = true; }
                    }
                    break;
            }
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(16)]
    [DataRow(SJReader.DefaultMaxDepth)]
    public void MaxStateStacking(int count)
    {
        if (!AssertIsStackable()) return;

        // TODO : This is off by a little bit
        using var r = new SJReader(FromString(CreateMaxRecursiveString("{}", count - 1)))
        {
            maxDepth = 0,         // Disable depth checks
            MaxStateCount = count // Count inclusive (error case handling is done in seperate method and usually occurs in full push or empty pop)
        };

        // Pop after second key entry and expect second key again.
        var root = r.Read();
        switch (root.type)
        {
            default:
                Assert.Fail($"Did not expect type {root.type}");
                return;

            // We realistically can't reach "End" now
            case SJType.Object:
                // Assuming parameter evaluation in C# is deterministic and is left to right.
                // https://stackoverflow.com/a/7360045
                Assert.AreEqual(r.StateCount, r.PushState(), "");     // depth = 1
                while (r.IterateObject(root, out _, out var ov))
                {
                    Assert.AreEqual(r.StateCount, r.PushState(), "Expected push to be valid"); // depth = ..count
                    root = ov;                                                                 // to move into reading the next value
                }
                break;
        }

        while (r.StateCount > 0)
        {
            int sc = r.StateCount;
            Assert.AreEqual(sc, r.S.depth, "Depth does not match while popping.");
            Assert.AreEqual(sc, r.PopState(), "Index does not match to the popped value id.");
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(16)]
    [DataRow(SJReader.DefaultMaxDepth)]
    public void InvalidStateStacking(int count)
    {
        if (!AssertIsStackable()) return;

        using var r = new SJReader(FromString("0"))
        {
            MaxStateCount = count,
        };

        // Stacking does not set error.
        for (int i = 0; i < count; i++) r.PushState();
        Assert.AreNotEqual(count, r.PushState(), "Expected to return an error value that isn't equal to the next expected value.");

        for (int i = 0; i < count; i++) r.PopState();
        Assert.AreNotEqual(0, r.PopState(), "Expected to return an error value that isn't equal to the next expected value.");
    }
    public virtual void WithUnstackableType()
    {
        if (CheckIsStackable())
        {
            // Technically inconclusive, but should "pass".
            Assert.Inconclusive($"Cannot test as type {typeof(TData)} is stackable.");
            return;
        }

        using var r = new SJReader(FromString("0"))
        {
            ThrowOnError = true
        };
        Assert.Throws<SJReader.ReaderException>(() => r.PushState());
    }

    // Peek + Peek comments / other entries
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"[]", null, 0, 0)]
    [DataRow(@"{}", null, 0, 0)]
    [DataRow(@"[1]", null, 1, 0)]
    [DataRow(@"{ ""1"":1 }", null, 1, 0)]
    [DataRow(@"[1, 2, 3]", null, 3, 0)]
    [DataRow(@"[1,{""v"":0,""o"":{}},3]", null, 3, 0)]
    [DataRow(@"[1,{""v"":0,""o"": /*m*/{""a"":24,""b"":[/* bla */],""c"":true/* blu */} }, 3, {}, {}, [[],[]]]", "m", 3, 1)]                      // Only first layer entries are peeked
    [DataRow(@"[1,{""v"":0,""o"": /*m*/{(KeyType)""a"":24,""b"":(Type)[/* bla */],""c"":(Bool)true/* blu */} }, 3, {}, {}, [[],[]]]", "m", 3, 4)] // ↑
    public void PeekEntryCount(string data, string marker, int values, int others)
    {
        // TODO : Test can be simplified
        if (!AssertIsStackable()) return;

        using var r = new SJReader(FromString(data))
        {
            ThrowOnError = true,
            allowTypes = true,
        };
        if (!string.IsNullOrEmpty(marker)) r.allowComments = true;

        bool validatedMarker = false;
        void Validate(SJReader.Value v)
        {
            switch (v.type)
            {
                case SJType.Comment:
                    break;
                case SJType.Array:
                case SJType.Object:
                    {
                        Assert.IsTrue(r.PeekEntryCount(v, out int vc, out int oc), "Peek must succeed on recursive root values.");
                        Assert.AreEqual(values, vc, "Value count mismatch, check arguments or code");
                        Assert.AreEqual(others, oc, "Others count mismatch, check arguments or code");

                        validatedMarker = true;
                        break;
                    }
                default:
                    {
                        Assert.IsFalse(r.PeekEntryCount(v, out int vc, out int oc), "Peek must fail on non recursive root values.");

                        validatedMarker = true;
                        break;
                    }
            }
        }
        // Should read the rest to ensure that the state was reset correctly after peek.
        void ReadWithMarker(SJReader r, SJReader.Value v, string m)
        {
            // Peek all elements if no marker.
            bool nextMarker = string.IsNullOrEmpty(m);

            switch (v.type)
            {
                default:
                case SJType.Error:
                    Assert.Fail($"Error or invalid type = '{v.type}'");
                    break;
                case SJType.End:
                    break;
                case SJType.Type:
                case SJType.Null:
                case SJType.String:
                case SJType.Number:
                case SJType.Key:
                case SJType.Bool:
                    if (!string.IsNullOrEmpty(m) && nextMarker && !validatedMarker)
                    {
                        Validate(v);
                        nextMarker = false;
                    }
                    break;
                case SJType.Object:
                    if (nextMarker && !validatedMarker)
                    {
                        Validate(v);
                        nextMarker = false;
                    }
                    while (r.IterateObjectEntries(v, out var oe))
                    {
                        ReadWithMarker(r, oe, m);
                    }
                    break;
                case SJType.Array:
                    if (nextMarker && !validatedMarker)
                    {
                        Validate(v);
                        nextMarker = false;
                    }
                    while (r.IterateArrayEntries(v, out var ae))
                    {
                        ReadWithMarker(r, ae, m);
                    }
                    break;
                case SJType.Comment:
                    if (nextMarker || validatedMarker)
                    {
                        break;
                    }

                    if (v.Slice().Trim().SequenceEqual(m))
                    {
                        // Next value will be the one to validate. It must be recursive.
                        nextMarker = true;
                    }
                    break;
            }
        }

        for (var v = r.Read(); !r.S.ended; v = r.Read())
        {
            if (v.type == SJType.Comment) continue; // Comments outside of the root value is ignored

            ReadWithMarker(r, v, marker);
        }
    }

    // Discarding
    protected static void TestDiscard(SJReader reader, string discardKey = JsonDataDiscardKey)
    {
        // Manual reading
        ArgumentNullException.ThrowIfNull(reader);
        reader.ThrowOnError = true;

        var root = reader.Read();
        while (reader.IterateObject(root, out SJReader.Value k, out SJReader.Value v))
        {
            if (k.Slice().Equals(discardKey, StringComparison.Ordinal))
            {
                Console.WriteLine($"Skipping very secret key '{k.Slice()}'");
                continue;
            }

            switch (v.type)
            {
                default:
                    Console.WriteLine($"{k.Slice()} : {v.Slice()}");
                    break;

                case SJType.Object:
                    Console.WriteLine($"{k.Slice()} :");
                    while (reader.IterateObject(v, out SJReader.Value k2, out SJReader.Value v2))
                    {
                        Console.WriteLine($"  {k2.Slice()} : {v2.Slice()}");
                    }
                    break;
                case SJType.Array:
                    Console.WriteLine($"{k.Slice()} :");
                    while (reader.IterateArray(v, out SJReader.Value av))
                    {
                        Console.WriteLine($"  {av.Slice()}");
                    }
                    break;
            }
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDiscard() => TestDiscard(new(FromString(JsonDataDiscard)));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDiscardInvalid() => Assert.Throws<SJReader.ReaderException>(() => TestDiscard(new(FromString(JsonDataDiscardInvalid))));

    // File tests
    public virtual Encoding JsonFileEncoding => Encoding.UTF8;
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileValidName)]
    public void TestFile(string name) => ReadWithTokens(FromStream(LoadAsmFile(name), JsonFileEncoding));
    [TestMethod]
    [Timeout(TestTimeouts.Long)]
    [DataRow(JsonFileLargeName)]
    [DataRow(JsonFileLargeMinName)]
    public void TestVeryLarge(string name) => ReadWithTokens(FromStream(LoadAsmFile(name), JsonFileEncoding));
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileInvalidBinary)]
    [DataRow(JsonFileInvalidNoColon)]
    [DataRow(JsonFileInvalidUnterminated)]
    public void TestInvalidFile(string name) => Assert.Throws<SJReader.ReaderException>(() =>
        ReadWithTokens(FromStream(LoadAsmFile(name), JsonFileEncoding))
    );
}
