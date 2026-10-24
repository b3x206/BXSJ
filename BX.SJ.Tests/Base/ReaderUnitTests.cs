using System.Text;
using System.Reflection;
using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests;

/// <summary>
/// Base unit tests for a <see cref="SJReader"/> that must pass.
/// </summary>
/// <typeparam name="TReader">Type for the target <see cref="SJReader"/></typeparam>
public abstract class ReaderUnitTests<TReader> where TReader : SJReader
{
    // uh oh:
    // * https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices#avoid-coding-logic-in-unit-tests
    // anyways, "it works"(tm). if only the data attributes could have been extended better.

    // -- Config
    /// <summary>
    /// Create <typeparamref name="TReader"/> with the data for <paramref name="data"/>.
    /// </summary>
    public abstract TReader CreateFromString(string data);
    /// <summary>
    /// Create <typeparamref name="TReader"/> with encoded data, if encoding related matters are to be tested. (for StringReader, this just tests the System libraries, but it will make sense for buffered readers)
    /// </summary>
    /// <param name="data">
    /// Data stream, can be created by a stream or something else.
    /// The stream can be disposed by this method if the <typeparamref name="TReader"/> does not rely on it.
    /// </param>
    /// <param name="enc">Encoding of the stream, if applicable.</param>
    public abstract TReader CreateFromStream(Stream data, Encoding? enc);
    /// <summary>
    /// Called when to dispose the <paramref name="reader"/>.
    /// </summary>
    /// <param name="reader">Reader to dispose.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="reader"/> is <see cref="IDisposable"/> and <see cref="IDisposable.Dispose"/> was called.
    /// Disposing over one times must not throw and return true. If the initial dispose does not return true, the reader is not tested for dispose.
    /// </returns>
    public virtual bool DisposeReader(TReader reader)
    {
        if (reader is null) return false;
        if (reader is not IDisposable d) return false;

        d.Dispose();
        return true;
    }
    /// <summary>
    /// Re-encodes <paramref name="data"/> to a stream and feeds it into <see cref="CreateWithStream(Stream)"/>.
    /// </summary>
    /// <param name="data">Data to create from.</param>
    /// <param name="enc">Encoding to mutilate <paramref name="data"/>'s bits.</param>
    public virtual TReader CreateFromEncodedString(string data, Encoding enc)
    {
        var ms = new MemoryStream(enc.GetBytes(data));
        return CreateFromStream(ms, enc);
    }
    /// <summary>
    /// Create a string that will exceed <paramref name="maxDepth"/>.
    /// </summary>
    /// <param name="tks">This argument must be the opening and enclosing tags for a recursive data structure, making it exactly 2 chars.</param>
    /// <exception cref="ArgumentException"></exception>
    protected TReader CreateFromMaxRecursionString(string tks, int maxDepth)
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
            return CreateFromString(string.Concat(tks[0], string.Join("", Enumerable.Repeat($"\"a\":{tks[0]}", maxDepth)), string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))));
        }
        else
        {
            return CreateFromString(string.Concat(string.Join("", Enumerable.Repeat(tks[0], maxDepth + 1)), string.Join("", Enumerable.Repeat(tks[1], maxDepth + 1))));
        }
    }
    /// <summary>
    /// Depth of property used on "MaxNRecursion". Override if your class initializes this property differently on a constructor.
    /// </summary>
    public virtual int MaxRecursionDepth => 128;
    /// <summary>
    /// Method to use if the reader must just be read and then disposed afterwards.
    /// </summary>
    public virtual StringBuilder ReadAndDispose(TReader reader)
    {
        try
        {
            return ReaderTester.Read(reader);
        }
        finally
        {
            // This should just not throw for "Read"
            DisposeReader(reader);
        }
    }
    /// <summary>
    /// Method to use if the reader must just be read and then disposed afterwards.
    /// </summary>
    public virtual StringBuilder ReadJSCAndDispose(TReader reader, bool captureComments = false)
    {
        try
        {
            return ReaderTester.ReadJSC(reader, captureComments);
        }
        finally
        {
            // This should just not throw for "Read"
            DisposeReader(reader);
        }
    }
    // - Processors
    /// <summary>
    /// Processors for the encoding parameter in the tests.
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
    public void TestBasic() => ReadAndDispose(CreateFromString(JsonData1));
    [TestMethod]
    [ExpectedException(typeof(SJReader.ReadException))]
    [Timeout(TestTimeouts.Short)]
    public void TestBasicInvalid() => ReadAndDispose(CreateFromString(JsonDataInvalid));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestSlightlyComplicated() => ReadAndDispose(CreateFromString(JsonData2));
    // Emoji spams will get differently encoded strings
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpam(Encoding enc)
    {
        var reader = CreateFromEncodedString(JsonData3, enc);
        var root = reader.Read();
        Assert.AreEqual(SJType.Object, root.type, $"Expected root to be object, it is instead this : {root}");
        while (reader.IterateObject(root, out SJReader.Value k, out SJReader.Value v))
        {
            Assert.AreEqual(new string(k.Slice()), JsonDataSpamKey, "Keys must be equal");
            Assert.AreEqual(SJType.Key, k.type);
            Assert.AreEqual(new string(v.Slice()), JsonDataSpamValue, "Values must be equal");
            Assert.AreEqual(SJType.String, v.type);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DynamicData(nameof(EncodedStringProcessors), DynamicDataDisplayName = nameof(GetEncodedTestName))]
    public void TestEmojiSpamLiteral(Encoding enc)
    {
        // Buffered readers / writers (such as stream based ones) can struggle with this with invalidly truncated read from the resources with other encodings.
        var reader = CreateFromEncodedString($"\"{DataEmojiSpam}\"", enc);
        var root = reader.Read();
        Assert.AreEqual(SJType.String, root.type);
        Assert.IsTrue(root.Slice().SequenceEqual(DataEmojiSpam), "Sliced value must equal the string inside the literal.");
    }

    // Tricky conditions on the comma/colon state
    [TestMethod]
    [DataRow(64)]
    [Timeout(TestTimeouts.Short)]
    public void TestValidDeepObjects(int depth) => ReadAndDispose(CreateFromMaxRecursionString("{}", depth));
    [TestMethod]
    [DataRow(64)]
    [Timeout(TestTimeouts.Short)]
    public void TestValidDeepArray(int depth) => ReadAndDispose(CreateFromMaxRecursionString("[]", depth));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"[""hello"" 1 2 3 4 5]")]
    [DataRow(@"{""hello"": 124 ""world!"": ""world: yo"" ""look"": ""no comma! waow bradar please JSON code make no mistakes""}")]
    [DataRow(@"{""hello"" 124, ""gurt"" ""yo"", ""yogurt"" ""yes""}")]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestCommaColonState(string data) => ReadAndDispose(CreateFromString(data));

    // Code Validation
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetRetainsData()
    {
        var reader = CreateFromString(JsonData1);
        try
        {
            int loadedDataLength = reader.Length;
            // Validate whether if JsonData1 is loaded correctly.
            Assert.AreEqual(JsonData1.Length, loadedDataLength);

            ReaderTester.Read(reader);
            reader.Reset();

            Assert.AreEqual(loadedDataLength, reader.Length);
        }
        finally
        {
            // where's RAII when you need it?
            if (reader is not null)
            {
                DisposeReader(reader);
            }
        }
    }
    public virtual void TestDisposeTwice()
    {
        bool firstDisposeValid = false;
        var reader = CreateFromString(JsonData1);
        try
        {
            ReaderTester.Read(reader);

            firstDisposeValid = DisposeReader(reader);
            if (!firstDisposeValid)
            {
                Assert.Inconclusive($"Reader '{reader}' with type '{reader.GetType()}' is not disposable (according to the test)");
                return;
            }

            Assert.IsTrue(DisposeReader(reader), "Second dispose must not fail");
            Assert.IsTrue(DisposeReader(reader), "Third dispose must not fail");
        }
        finally
        {
            if (firstDisposeValid && reader is not null)
            {
                Assert.IsTrue(DisposeReader(reader), "Fourth dispose must not fail, if first one is valid.");
            }
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestResetClearsState()
    {
        var reader = CreateFromString(JsonDataInvalid);
        try
        {
            reader.ThrowOnError = false;
            ReaderTester.Read(reader, throwError: false);

            reader.Reset();
            Assert.That.IsNullOrEmpty(reader.Error, "Error state must reset on Reset");
            Assert.AreEqual(0, reader.current, "Reader position must be zero after reset");
        }
        finally
        {
            DisposeReader(reader);
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
    public void TestDiscard() => TestDiscard(CreateFromString(JsonDataDiscard));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestDiscardInvalid() => TestDiscard(CreateFromString(JsonDataDiscardInvalid));

    // Peek + Peek JSC
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"[1, 2, 3]", null, 0, 3, 0)]
    public void TestPeekCount(string data, string marker, int keys, int values, int others)
    {
        TReader? r = null;
        try
        {
            r = CreateFromString(data);
            r.ThrowOnError = true;
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
                            Assert.IsTrue(r.PeekEntryCount(v, out int kc, out int vc, out int oc), "Peek must succeed on recursive root values.");
                            Assert.AreEqual(keys, kc, "Key count mismatch, check arguments or code");
                            Assert.AreEqual(values, vc, "Value count mismatch, check arguments or code");
                            Assert.AreEqual(others, oc, "Others count mismatch, check arguments or code");

                            validatedMarker = true;
                            break;
                        }
                    default:
                        {
                            Assert.IsFalse(r.PeekEntryCount(v, out int kc, out int vc, out int oc), "Peek must fail on non recursive root values.");

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
                        Assert.Fail($"Error or invalid type for value {v.type}");
                        break;
                    case SJType.End:
                        break;
                    case SJType.Null:
                    case SJType.String:
                    case SJType.Number:
                    case SJType.Key:
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

            for (var v = r.Read(); !r.ended; v = r.Read())
            {
                if (v.type == SJType.Comment) continue; // Comments outside of the root value is ignored

                ReadWithMarker(r, v, marker);
            }
        }
        finally
        {
            if (r is not null) DisposeReader(r);
        }
    }

    // Invalid JSON with settings
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", ""c"": [""d"", ""e"", true, false, 23, { ""c"": false, },], ""ss"": true, }")]
    public void TestTrailingCommas(string d)
    {
        TReader? r = null;
        try
        {
            r = CreateFromString(d);
            r.allowTrailingComma = true;
            ReaderTester.Read(r);
        }
        finally
        {
            if (r is not null) DisposeReader(r); // Very RAII
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true }], 0x3589: true }")]
    public void TestOtherKeyTypes(string d)
    {
        TReader? r = null;
        try
        {
            r = CreateFromString(d);
            r.allowOtherKeyTypes = true;
            ReaderTester.Read(r);
        }
        finally
        {
            if (r is not null) DisposeReader(r);
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""a"": ""b"", 1235: [""d"", ""e"", true, false, 23, { null: false, true: true, },], 0x3589: true, }")]
    public void TestAllTolerance(string d)
    {
        TReader? r = null;
        try
        {
            r = CreateFromString(d);
            r.allowOtherKeyTypes = true;
            r.allowTrailingComma = true;
            ReaderTester.Read(r);
        }
        finally
        {
            if (r is not null) DisposeReader(r);
        }
    }

    // JSC
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSC() => ReadJSCAndDispose(CreateFromString(JsonDataComment));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestJSCInvalid() => ReadJSCAndDispose(CreateFromString(JsonDataCommentInvalid));

    // JSC with Capture
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSCWithCapture() => ReadJSCAndDispose(CreateFromString(JsonDataComment), true);
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(@"{ ""key"" /* inline */ : ""value"" }")]
    [DataRow(@"{ ""key"" : /* inline */ ""value"" }")]
    [DataRow(@"{ ""key"" : ""value"" /* trailing */ , ""key2"": ""value2"" }")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */, 4, 5]")]
    [DataRow(@"[ 1, 2, 3 /* trailing array */]")]
    public void TestJSCWithTrickyCapture(string data) => ReadJSCAndDispose(CreateFromString(data), true);
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestJSCInvalidWithCapture() => ReadJSCAndDispose(CreateFromString(JsonDataInvalid), true);
    // Do also read literals with no ignore JSC to test "Ended".
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDataProcessors))]
    public void TestJSCRootLiteralsWithCapture(string data)
    {
        // Micro$oft pls
        string CommentLeft = "// This is a comment!" + Environment.NewLine;
        string CommentRight = "// One more comment!" + Environment.NewLine + "/* Is it the end of file? */"; // All whitespace (surrounding the document) is ignored.

        Assert.AreEqual(data, ReadJSCAndDispose(CreateFromString(data), true).ToString());
        // To spice it up, surround root data with comments on second iter.
        var data2 = $"{CommentLeft}{data}{CommentRight}";
        Assert.AreEqual(data2, ReadJSCAndDispose(CreateFromString(data2), true).ToString());
    }

    // Empty root
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonRootDataProcessors))]
    public void TestRootLiterals(string data) =>
        // Since the empty builder for the sample code is simple but somewhat matching, it should equal.
        Assert.AreEqual(data, ReadAndDispose(CreateFromString(data)).ToString());
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(JsonTruncatedRootDataProcessors), DynamicDataSourceType.Method)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestTruncatedRootLiterals(string data) => ReadAndDispose(CreateFromString(data));
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestEmptyString(bool throwOnError)
    {
        var r = CreateFromString(DataEmpty);
        r.ThrowOnError = throwOnError; // The execution order may set Ended early, but this isn't the case anymore.
        ReadAndDispose(r);
    }

    // Stack
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestStacking() => ReadAndDispose(CreateFromString(JsonDataStackingInvalid));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestMaxArrayRecursion() => ReadAndDispose((CreateFromMaxRecursionString("[]", MaxRecursionDepth)));
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestMaxObjectRecursion() => ReadAndDispose(CreateFromMaxRecursionString("{}", MaxRecursionDepth));

    // File tests
    public virtual Encoding JsonFileEncoding => Encoding.UTF8;
    /// <summary>
    /// Tests reading a basic JSON file (100kb-ish).
    /// </summary>
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileValidName)]
    public void TestFile(string name) => ReadAndDispose(CreateFromStream(LoadAsmFile(name), JsonFileEncoding));
    /// <summary>
    /// Tests reading large JSON file.
    /// </summary>
    [TestMethod]
    [Timeout(TestTimeouts.Long)]
    [DataRow(JsonFileLargeName)]
    [DataRow(JsonFileLargeMinName)]
    public void TestVeryLarge(string name) => ReadAndDispose(CreateFromStream(LoadAsmFile(name), JsonFileEncoding));
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    [DataRow(JsonFileInvalidBinary)]
    [DataRow(JsonFileInvalidNoColon)]
    [DataRow(JsonFileInvalidUnterminated)]
    [ExpectedException(typeof(SJReader.ReadException))]
    public void TestInvalidFile(string name) => ReadAndDispose(CreateFromStream(LoadAsmFile(name), JsonFileEncoding));
}
