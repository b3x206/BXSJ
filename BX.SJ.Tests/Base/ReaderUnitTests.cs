using System.Text;
using System.Reflection;
using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests;

/// <summary>
/// Base unit tests for a <see cref="SJReader"/> that must pass.
/// </summary>
/// <typeparam name="TData">Data reader type for the target <see cref="SJReader"/>.</typeparam>
public abstract class ReaderUnitTests<TData> where TData : ISJDataReader
{
    // -- Config
    public abstract TData DataReaderFromString(string data);
    public abstract TData DataReaderFromStream(Stream data, Encoding? enc);
    public virtual TData DataReaderFromEncodedString(string data, Encoding enc)
    {
        var ms = new MemoryStream(enc.GetBytes(data));
        return DataReaderFromStream(ms, enc);
    }
    protected TData DataReaderFromMaxRecursionString(string tks, int maxDepth)
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
            // keys are needed for object
            // {"a":{"a":...
            return DataReaderFromString(string.Concat(tks[0], string.Join("", Enumerable.Repeat($"\"a\":{tks[0]}", maxDepth)), string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))));
        }
        else
        {
            return DataReaderFromString(string.Concat(string.Join("", Enumerable.Repeat(tks[0], maxDepth + 1)), string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))));
        }
    }
    public virtual StringBuilder ReadAndDispose(TData data)
    {
        using var r = new SJReader(data);
        return ReaderTester.Read(r);
    }
    public virtual StringBuilder ReadJSCAndDispose(TData data)
    {
        using var r = new SJReader(data);
        r.allowComments = true;
        return ReaderTester.Read(r);
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
    /// <summary>
    /// Collection of root data (that are either empty or not)
    /// </summary>
    public static IEnumerable<string[]> JsonRootDataProcessors => [[JsonDataEmptyObject], [JsonDataEmptyArray], [JsonDataRootString], [JsonDataRootNumber], [JsonDataRootBoolFalse], [JsonDataRootBoolTrue], [JsonDataRootNull]];
    public static IEnumerable<string[]> JsonTruncatedRootDataProcessors() => TestEx.CreateTruncatedDataProcessors(JsonRootDataProcessors.Where(v => v.Length > 0 && v[0] != JsonDataRootNumber));

    // Tests
    // Basic tests: Read and Empty
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestBasic() => ReadAndDispose(DataReaderFromString(JsonData1));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestBasicInvalid() => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromString(JsonDataInvalid)));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestSlightlyComplicated() => ReadAndDispose(DataReaderFromString(JsonData2));
    // Emoji spams will get differently encoded strings
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpam(Encoding enc)
    {
        using SJReader reader = new(DataReaderFromEncodedString(JsonData3, enc));
        var root = reader.Read();
        Assert.AreEqual(SJType.Object, root.type, $"Expected root to be object, it is instead this : {root}");
        while (reader.IterateObject(root, out SJReader.Value k, out SJReader.Value v))
        {
            Assert.AreEqual(JsonDataSpamKey, new string(k.Slice()), "Keys must be equal");
            Assert.AreEqual(SJType.Key, k.type);
            Assert.AreEqual(JsonDataSpamValue, new string(v.Slice()), "Values must be equal");
            Assert.AreEqual(SJType.String, v.type);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpamLiteral(Encoding enc)
    {
        // Buffered readers / writers (such as stream based ones) can struggle with this with invalidly truncated read from the resources with other encodings.
        using SJReader reader = new(DataReaderFromEncodedString($"\"{DataEmojiSpam}\"", enc));
        var root = reader.Read();
        Assert.AreEqual(SJType.String, root.type);
        Assert.IsTrue(root.Slice().SequenceEqual(DataEmojiSpam), "Sliced value must equal the string inside the literal.");
    }

    // Tricky conditions on the comma/colon state
    [TestMethod]
    [DataRow(64)]
    [Timeout(TestTimeouts.Short)]
    public void TestValidDeepObjects(int depth) => ReadAndDispose(DataReaderFromMaxRecursionString("{}", depth));
    [TestMethod]
    [DataRow(64)]
    [Timeout(TestTimeouts.Short)]
    public void TestValidDeepArray(int depth) => ReadAndDispose(DataReaderFromMaxRecursionString("[]", depth));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"[""hello"" 1 2 3 4 5]")]
    [DataRow(@"{""hello"": 124 ""world!"": ""world: yo"" ""look"": ""no comma! waow bradar please JSON code make no mistakes""}")]
    [DataRow(@"{""hello"" 124, ""gurt"" ""yo"", ""yogurt"" ""yes""}")]
    public void TestCommaColonState(string data) => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromString(data)));

    // Code Validation
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetRetainsData()
    {
        // Validate whether if JsonData1 is loaded correctly.
        using SJReader reader = new(DataReaderFromString(JsonData1));
        ReaderTester.Read(reader);
        Assert.AreEqual(JsonData1.Length, reader.data.Position);

        // Read again with test
        reader.Reset();
        ReaderTester.Read(reader);
        Assert.AreEqual(JsonData1.Length, reader.data.Position);
    }
    // Dispose is no-op for most cases.
    //public virtual void TestDisposeTwice()
    //{
    //    bool firstDisposeValid = false;
    //    using SJReader reader = new(DataReaderFromString(JsonData1));
    //    try
    //    {
    //        ReaderTester.Read(reader);

    //        firstDisposeValid = DisposeReader(reader);
    //        if (!firstDisposeValid)
    //        {
    //            Assert.Inconclusive($"Reader '{reader}' with type '{reader.GetType()}' is not disposable (according to the test)");
    //            return;
    //        }

    //        Assert.IsTrue(DisposeReader(reader), "Second dispose must not fail");
    //        Assert.IsTrue(DisposeReader(reader), "Third dispose must not fail");
    //    }
    //    finally
    //    {
    //        if (firstDisposeValid && reader is not null)
    //        {
    //            Assert.IsTrue(DisposeReader(reader), "Fourth dispose must not fail, if first one is valid.");
    //        }
    //    }
    //}
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetClearsState()
    {
        using SJReader reader = new(DataReaderFromString(JsonDataInvalid));
        ReaderTester.Read(reader, throwError: false);

        reader.Reset();
        Assert.That.IsNullOrEmpty(reader.Error, "Error state must reset on Reset");
        Assert.AreEqual(0, reader.data.Position, "Reader position must be zero after reset");
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
    public void TestDiscard() => TestDiscard(new(DataReaderFromString(JsonDataDiscard)));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDiscardInvalid() => Assert.Throws<SJReader.ReadException>(() => TestDiscard(new(DataReaderFromString(JsonDataDiscardInvalid))));

    //// Peek + Peek JSC
    //[TestMethod]
    //// [Timeout(TestTimeouts.Short)]
    //[DataRow(@"[]", null, 0, 0, 0)]
    //[DataRow(@"{}", null, 0, 0, 0)]
    //[DataRow(@"[1]", null, 0, 1, 0)]
    //[DataRow(@"{ ""1"":1 }", null, 1, 1, 0)]
    //[DataRow(@"[1, 2, 3]", null, 0, 3, 0)]
    //[DataRow(@"[1,{""v"":0,""o"":{}},3]", null, 0, 3, 0)]
    //[DataRow(@"[1,{""v"":0,""o"":/*c*/{""a"":24,""b"":[/* bla */],""c"":true/* blu */}},3, {}, {}, [[],[]]]", "c", 3, 3, 1)]
    //public void TestPeekCount(string data, string marker, int keys, int values, int others)
    //{
    //    TData? r = null;
    //    try
    //    {
    //        r = DataReaderFromString(data);
    //        r.ThrowOnError = true;
    //        if (!string.IsNullOrEmpty(marker)) r.allowComments = true;

    //        bool validatedMarker = false;
    //        void Validate(SJReader.Value v)
    //        {
    //            switch (v.type)
    //            {
    //                case SJType.Comment:
    //                    break;
    //                case SJType.Array:
    //                case SJType.Object:
    //                    {
    //                        Assert.IsTrue(r.PeekEntryCount(v, out int kc, out int vc, out int oc), "Peek must succeed on recursive root values.");
    //                        Assert.AreEqual(keys, kc, "Key count mismatch, check arguments or code");
    //                        Assert.AreEqual(values, vc, "Value count mismatch, check arguments or code");
    //                        Assert.AreEqual(others, oc, "Others count mismatch, check arguments or code");

    //                        validatedMarker = true;
    //                        break;
    //                    }
    //                default:
    //                    {
    //                        Assert.IsFalse(r.PeekEntryCount(v, out int kc, out int vc, out int oc), "Peek must fail on non recursive root values.");

    //                        validatedMarker = true;
    //                        break;
    //                    }
    //            }
    //        }
    //        // Should read the rest to ensure that the state was reset correctly after peek.
    //        void ReadWithMarker(SJReader r, SJReader.Value v, string m)
    //        {
    //            // Peek all elements if no marker.
    //            bool nextMarker = string.IsNullOrEmpty(m);

    //            switch (v.type)
    //            {
    //                default:
    //                case SJType.Error:
    //                    Assert.Fail($"Error or invalid type = '{v.type}'");
    //                    break;
    //                case SJType.End:
    //                    break;
    //                case SJType.Null:
    //                case SJType.String:
    //                case SJType.Number:
    //                case SJType.Key:
    //                case SJType.Bool:
    //                    if (!string.IsNullOrEmpty(m) && nextMarker && !validatedMarker)
    //                    {
    //                        Validate(v);
    //                        nextMarker = false;
    //                    }
    //                    break;
    //                case SJType.Object:
    //                    if (nextMarker && !validatedMarker)
    //                    {
    //                        Validate(v);
    //                        nextMarker = false;
    //                    }
    //                    while (r.IterateObjectEntries(v, out var oe))
    //                    {
    //                        ReadWithMarker(r, oe, m);
    //                    }
    //                    break;
    //                case SJType.Array:
    //                    if (nextMarker && !validatedMarker)
    //                    {
    //                        Validate(v);
    //                        nextMarker = false;
    //                    }
    //                    while (r.IterateArrayEntries(v, out var ae))
    //                    {
    //                        ReadWithMarker(r, ae, m);
    //                    }
    //                    break;
    //                case SJType.Comment:
    //                    if (nextMarker || validatedMarker)
    //                    {
    //                        break;
    //                    }

    //                    if (v.Slice().Trim().SequenceEqual(m))
    //                    {
    //                        // Next value will be the one to validate. It must be recursive.
    //                        nextMarker = true;
    //                    }
    //                    break;
    //            }
    //        }

    //        for (var v = r.Read(); !r.S.ended; v = r.Read())
    //        {
    //            if (v.type == SJType.Comment) continue; // Comments outside of the root value is ignored

    //            ReadWithMarker(r, v, marker);
    //        }
    //    }
    //    finally
    //    {
    //        if (r is not null) DisposeReader(r);
    //    }
    //}

    // Invalid JSON that is supported
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
    public void TestInvalidNumbers(string s) => ReadAndDispose(DataReaderFromString(s));

    // Invalid JSON with settings
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", ""c"": [""d"", ""e"", true, false, 23, { ""c"": false, },], ""ss"": true, }")]
    public void TestTrailingCommas(string d)
    {
        using SJReader r = new(DataReaderFromString(d));
        r.allowTrailingComma = true;
        ReaderTester.Read(r);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true }], 0x3589: true }")]
    public void TestOtherKeyTypes(string d)
    {
        using SJReader r = new(DataReaderFromString(d));
        r.allowOtherKeyTypes = true;
        ReaderTester.Read(r);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true, },], 0x3589: true, }")]
    public void TestAllTolerance(string d)
    {
        using SJReader r = new(DataReaderFromString(d));
        r.allowTrailingComma = true;
        r.allowOtherKeyTypes = true;
        ReaderTester.Read(r);
    }

    // JSC
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSC() => ReadJSCAndDispose(DataReaderFromString(JsonDataComment));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSCInvalid() => Assert.Throws<SJReader.ReadException>(() => ReadJSCAndDispose(DataReaderFromString(JsonDataCommentInvalid)));

    // JSC with Capture
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""key"" /* inline */ : ""value"" }")]
    [DataRow(@"{ ""key"" : /* inline */ ""value"" }")]
    [DataRow(@"{ ""key"" : ""value"" /* trailing */ , ""key2"": ""value2"" }")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */, 4, 5]")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */]")]
    public void TestTrickyJSC(string data) => ReadJSCAndDispose(DataReaderFromString(data));
    // Do also read literals with no ignore JSC to test "Ended".
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDataProcessors))]
    public void TestJSCRootLiteralsWithCapture(string data)
    {
        // Since 3.x, comments are now captured "content only", so this basic output is expected.
        const string ContentLeft = "This is a comment!";
        const string ContentRight1 = "Dear god..";
        const string ContentRight2 = "There's more! No.";
        string CommentLeft = $"// {ContentLeft}" + Environment.NewLine;
        string CommentRight = $"// {ContentRight1}" + Environment.NewLine + $"/* {ContentRight2} */";

        Assert.AreEqual(data, ReadJSCAndDispose(DataReaderFromString(data)).ToString());
        string readerData = $"{CommentLeft}{data}{CommentRight}",
            printData = $"{ContentLeft}{data}{ContentRight1}{ContentRight2}";
        Assert.AreEqual(printData, ReadJSCAndDispose(DataReaderFromString(readerData)).ToString());
    }

    // Empty root
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDataProcessors))]
    public void TestRootLiterals(string data) =>
        // Since the empty builder for the sample code is simple but somewhat matching, it should equal.
        Assert.AreEqual(data, ReadAndDispose(DataReaderFromString(data)).ToString());
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonTruncatedRootDataProcessors))]
    public void TestTruncatedRootLiterals(string data) => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromString(data)));
    [TestMethod]
    //[DataRow(true)]
    //[DataRow(false)]
    [Timeout(TestTimeouts.Short)]
    public void TestEmptyString(/*bool throwOnError*/)
    {
        Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromString(DataEmpty)));
    }

    // Stack
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestStacking() => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromString(JsonDataStackingInvalid)));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxArrayRecursion() => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose((DataReaderFromMaxRecursionString("[]", SJReader.DefaultMaxDepth))));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxObjectRecursion() => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromMaxRecursionString("{}", SJReader.DefaultMaxDepth)));

    // File tests
    public virtual Encoding JsonFileEncoding => Encoding.UTF8;
    /// <summary>
    /// Tests reading a basic JSON file (100kb-ish).
    /// </summary>
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileValidName)]
    public void TestFile(string name) => ReadAndDispose(DataReaderFromStream(LoadAsmFile(name), JsonFileEncoding));
    /// <summary>
    /// Tests reading large JSON file.
    /// </summary>
    [TestMethod]
    [Timeout(TestTimeouts.Long)]
    [DataRow(JsonFileLargeName)]
    [DataRow(JsonFileLargeMinName)]
    public void TestVeryLarge(string name) => ReadAndDispose(DataReaderFromStream(LoadAsmFile(name), JsonFileEncoding));
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileInvalidBinary)]
    [DataRow(JsonFileInvalidNoColon)]
    [DataRow(JsonFileInvalidUnterminated)]
    public void TestInvalidFile(string name) => Assert.Throws<SJReader.ReadException>(() => ReadAndDispose(DataReaderFromStream(LoadAsmFile(name), JsonFileEncoding)));
}
