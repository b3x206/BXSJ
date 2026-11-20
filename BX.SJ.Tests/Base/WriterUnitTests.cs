using System.Globalization;

namespace BX.SJ.Tests;

public abstract class WriterUnitTests<TWriter> where TWriter : ISJDataWriter
{
    // TODO : Organize this better.
    public abstract TWriter CreateData();

    // Data / State
    private static readonly Type[] _ReadDataConsistencyExceptionTypes = [typeof(NotSupportedException), typeof(InvalidOperationException)];
    /// <summary>
    /// Exception types expected from your variety of Writer.
    /// </summary>
    public virtual Type[] ReadDataConsistencyExceptionTypes => _ReadDataConsistencyExceptionTypes;
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestReadDataConsistency()
    {
        using var writer = new SJWriter(CreateData());
        // Buffered objects may struggle with converting representation to string.
        writer.ThrowOnError = true;
        using (writer.Object())
        {
            Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");

            using (writer.ArrayKV("array"))
            {
                Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");
            }
        }

        if (!writer.Data.CanRead)
        {
            try
            {
                writer.Data.ReadWritten();
            }
            catch (Exception ex) when (ReadDataConsistencyExceptionTypes.Contains(ex.GetType()))
            {
                Console.WriteLine($"Caught false Data.CanRead exception '{ex}'");
            }
        }
        else
        {
            // Reading written data must equal written count
            Assert.AreEqual(writer.Data.Count, writer.Data.ReadWritten().Length);
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestResetClearsState()
    {
        static void ValidateResetState(SJWriter w)
        {
            Assert.That.IsNullOrEmpty(w.Error, "Error state must reset on Reset");
            Assert.AreEqual(0, w.Data.Count, "Writer count must be zero after reset");
            Assert.AreEqual(0, w.S.depth, "Writer depth must be zero after reset");
            Assert.AreEqual(SJWriter.Expect.None, w.S.expect, "Writer expectation must be none for root");
            Assert.AreEqual(SJWriter.FormatExpect.None, w.S.fmtExpect, "Writer format expectation must be none for root");
            Assert.IsFalse(w.S.ended, "Writer finish state must be false");
            Assert.IsFalse(w.S.WrittenComment, "Writer comment state must be false");
            Assert.IsFalse(w.S.WrittenCustomKey, "Writer custom key state must be false");
            Assert.IsFalse(w.S.WrittenCustomValue, "Writer custom value state must be false");
            Assert.IsFalse(w.S.WrittenType, "Writer custom value state must be false");
        }

        using var writer = new SJWriter(CreateData());
        writer.ThrowOnError = false;
        writer.allowComments = true;
        writer.allowOtherKeyTypes = true;
        writer.indentSize = 4;

        using (writer.Object())
        {
            using (writer.ArrayKV("What is the private key?"))
            {
                // TODO : Add other keys and write in 384 bit format :)
                writer.WriteLong(4, format: "x");
                Assert.IsTrue(writer.S.WrittenCustomValue, "Custom value was written, but the state hasn't changed.");
                writer.WriteString("Just pick your favourite numbers <= 256! byte.MaxValue is plenty safe.");
            }

            writer.WriteCommentLine("Is it true?", hasNextValue: true);
            Assert.IsTrue(writer.S.WrittenComment, "Comment was written, but the state hasn't changed.");

            writer.WriteKeyBool(true);
            writer.WriteBool(true);
            Assert.IsTrue(writer.S.WrittenCustomKey, "Custom key was written, but the state hasn't changed.");
        }

        // Cause error
        writer.WriteNumber(123);
        Assert.That.IsNotNullOrEmpty(writer.Error);

        // Reset and validate anything that could have potentially changed
        writer.Reset();
        ValidateResetState(writer);

    }

    // Write
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestRootWrite(int indent)
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = indent;
        writer.ThrowOnError = false;

        WriterTester.WriteTestRoot(writer);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestWrite(int indent)
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = indent;
        writer.ThrowOnError = true;

        // Writing an object that looks like this.
        using (writer.Object())
        {
            // Though ThrowOnError is true, so :shrug:
            Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");

            using (writer.ArrayKV("test on le array"))
            {
                Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");
            }
        }

        // Read resulting data
        if (writer.Data.CanRead)
        {
            string? data = writer.Data.ReadWritten();
            // Validate
            Assert.AreEqual(writer.Data.Count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
            var r = new SJReader(new SJStringReader(data)) { allowComments = false };
            ReaderTester.ReadWithTokens(r);
            // And show
            Console.WriteLine(data);
        }
        else
        {
            Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
        }

    }
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    public void TestWriteEmojiSpam()
    {
        using var writer = new SJWriter(CreateData());
        writer.ThrowOnError = true;
        writer.WriteString(TestData.DataEmojiSpam);
        if (!writer.Data.CanRead)
        {
            Assert.Inconclusive($"[!] Cannot read data from this writer '{writer}'");
        }

        // Buffered writers must truncate and write this correctly
        Assert.AreEqual(writer.Data.ReadWritten(), $"\"{TestData.DataEmojiSpam}\"");
    }

    // Invalid Write
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDefaultNoCustomKey()
    {
        using var writer = new SJWriter(CreateData());
        const string Message = "Error must exist on default state of writing custom key with a writer";
        writer.ThrowOnError = false;

        using (writer.Object()) { writer.WriteKeyNumber(1.2424f); writer.Write("yes"); }
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);
    }
    // Custom values (WrittenCustomValue) which is checked only by number literals for now are accepted by default.
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestCustomNumberLiterals()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 0;
        writer.ThrowOnError = true;
        if (!writer.Data.CanRead)
        {
            Assert.Inconclusive("Cannot read data from this type of writer.");
            return;
        }

        // Check number literals + custom prefixes
        // The only formats for invariant culture that are unchanged are the ones that use no commas in it's format.
        // So 'C', 'N', etc. are technically not allowed. Comparison will replace all "," with "" if such formats are used.
        const long n = -0x12341234123412;
        const ulong un = 0x12341234123412;
        string[] fmts = ["B", "C", "D", "E", "F", "G", "N", "P", "R", "X"];
        foreach (string fmt in fmts)
        {
            // Prefixes are auto determined, so explicitly disable those for the purposes of this test.
            writer.WriteLong(n, fmt, "");
            Assert.AreEqual(n.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", ""), writer.Data.ReadWritten());
            writer.Reset();

            writer.WriteULong(un, fmt, "");
            Assert.AreEqual(un.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", ""), writer.Data.ReadWritten());
            writer.Reset();
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestCustomKeys()
    {
        using var writer = new SJWriter(CreateData());
        writer.ThrowOnError = true;
        writer.allowOtherKeyTypes = true;
        writer.indentSize = 0;
        if (!writer.Data.CanRead)
        {
            Assert.Inconclusive("Cannot read data from this type of writer.");
            return;
        }

        const long n = -0x12341234123412;
        const ulong un = 0x12341234123412;
        string[] fmts = ["B", "C", "D", "E", "F", "G", "N", "P", "R", "X"];
        foreach (string fmt in fmts)
        {
            using (writer.Object())
            {
                writer.WriteKeyLong(n, fmt, "");
                writer.WriteNull();
            }

            Assert.AreEqual($"{{{n.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", "")}:null}}", writer.Data.ReadWritten());
            writer.Reset();

            using (writer.Object())
            {
                writer.WriteKeyULong(un, fmt, "");
                writer.WriteNull();
            }

            Assert.AreEqual($"{{{un.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", "")}:null}}", writer.Data.ReadWritten());
            writer.Reset();
        }
    }

    // JSC
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDefaultNoComments()
    {
        using var writer = new SJWriter(CreateData());
        const string Message = "Error must exist on default state of writing comments with a writer";
        writer.ThrowOnError = false;

        writer.WriteComment("A");
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);

        writer.Reset();
        writer.WriteCommentLine("A");
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);
    }
    [TestMethod]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    [Timeout(TestTimeouts.Short)]
    public void TestRootJSCWrite(int indent)
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = indent;
        writer.ThrowOnError = false;
        writer.allowComments = true;

        WriterTester.WriteTestRootJSC(writer);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestJSCWrite(int indent)
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = indent;
        writer.allowComments = true;
        writer.ThrowOnError = true;

        using (writer.Object())
        {
            Assert.IsTrue(WriterTester.WriteTestComments(writer), "Writing must go without any errors");

            using (writer.ArrayKV("array"))
            {
                Assert.IsTrue(WriterTester.WriteTestComments(writer), "Writing must go without any errors");
            }
        }

        // Read resulting data
        if (writer.Data.CanRead)
        {
            string? data = writer.Data.ReadWritten();
            // Validate
            Assert.AreEqual(writer.Data.Count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
            var r = new SJReader(new SJStringReader(data)) { allowComments = true };
            ReaderTester.ReadWithTokens(r);
            // And show
            Console.WriteLine(data);
        }
        else
        {
            Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestJSCMatrix(int indent)
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = indent;
        writer.allowComments = true;
        writer.ThrowOnError = true;

        using (writer.Object())
        {
            WriterTester.WriteTestJSCMatrix(writer);

            using (writer.ArrayKV("array"))
            {
                WriterTester.WriteTestJSCMatrix(writer);
            }
        }

        // Read resulting data
        if (writer.Data.CanRead)
        {
            string? data = writer.Data.ReadWritten();
            Assert.AreEqual(writer.Data.Count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
            var r = new SJReader(new SJStringReader(data)) { allowComments = true };
            ReaderTester.ReadWithTokens(r);
            Console.WriteLine(data);
        }
        else
        {
            Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSCPadCharacters()
    {
        using var writer = new SJWriter(CreateData());
        if (!writer.Data.CanRead)
        {
            Assert.Inconclusive($"[!] Cannot test '{writer.GetType()}' as data can't be read.");
            return;
        }

        const string Comment = "Hello world!";

        writer.allowComments = true;
        foreach (var invalidPadChar in new[] { '\0', '\r', '\n' })
        {
            string InvalidPadMessage = $"Invalid pad char '{(int)invalidPadChar}' must be ignored, unless that's a feature (no).";

            writer.Reset();
            writer.WriteComment(Comment, invalidPadChar);
            Assert.AreEqual($"/*{Comment}*/", writer.Data.ReadWritten(), InvalidPadMessage);

            writer.Reset();
            writer.WriteCommentLine(Comment, invalidPadChar);
            Assert.AreEqual($"//{Comment}", writer.Data.ReadWritten(), InvalidPadMessage);
        }
        foreach (var validPadChar in Enumerable.Range(1, 127).Where(v => v != '\r' && v != '\n').Select(Convert.ToChar))
        {
            string ValidPadMessage = $"Valid pad char '{(int)validPadChar}' must be written";

            writer.Reset();
            writer.WriteComment(Comment, validPadChar);
            Assert.AreEqual($"/*{validPadChar}{Comment}{validPadChar}*/", writer.Data.ReadWritten(), ValidPadMessage);

            writer.Reset();
            writer.WriteCommentLine(Comment, validPadChar);
            Assert.AreEqual($"//{validPadChar}{Comment}", writer.Data.ReadWritten(), ValidPadMessage);
        }
    }

    // Depth / Stacking
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDepth()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 4;
        writer.ThrowOnError = true;

        writer.maxDepth = SJWriter.DefaultMaxDepth;
        Assert.IsFalse(WriterTester.WriteTestDepth(writer, 64));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxDepth()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 4;
        writer.ThrowOnError = true;

        Assert.Throws<SJWriter.WriteException>(() => WriterTester.WriteMaxTestDepth(writer));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxDepthNoExcept()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 4;
        writer.ThrowOnError = false;

        Assert.IsTrue(WriterTester.WriteMaxTestDepth(writer), "Depth test must fail");
        Assert.That.IsNotNullOrEmpty(writer.Error, "Writer should have an error set after failing");
    }

    static bool TestStackWith(SJWriter writer)
    {
        writer.BeginObject();

        writer.WriteKey("Fun fact : ");

        writer.BeginArray();
        bool result = !(bool)writer.EndObject(); // whoops
        result = result || !(bool)writer.EndArray();

        return result;
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestStack()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 3;
        writer.ThrowOnError = true;

        Assert.Throws<SJWriter.WriteException>(() => TestStackWith(writer));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestStackNoExcept()
    {
        using var writer = new SJWriter(CreateData());
        writer.indentSize = 4;
        writer.ThrowOnError = false;
        Assert.IsTrue(TestStackWith(writer), "Stack test must fail");
        Assert.That.IsNotNullOrEmpty(writer.Error, "Writer should have an error set after failing");
    }
    public TestContext? TestContext { get; set; }
    // State
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(
        nameof(TestData.StringDatas), typeof(TestData),
        DynamicDataDisplayName = nameof(TestData.StringDatasDisplayName),
        DynamicDataDisplayNameDeclaringType = typeof(TestData)
    )]
    public void TestState(string write)
    {
        Console.WriteLine($"TestContext : {TestContext?.ResultsDirectory}");

        // Test this with many strings (including emoji spam,
        // to check for stuff like whether if the StreamWriter has
        // resetted it's state correctly, well as correct as it can be.)
        using var writer = new SJWriter(CreateData());
        writer.ThrowOnError = false;
        const string Key1 = "Hello", Key2 = "this should be all";
        const string Val2Fmt = "X";
        var Val2Prefix = SJWriter.FormatToPrefix(Val2Fmt);
        const long Val2 = 0x616C7269676874;

        // Start writing
        using (writer.Object())
        {
            writer.WriteKey("Hello").WriteString(write);

            // Me thinks this next code can do a mistake
            Assert.AreEqual(0, writer.PushState(), "Must push state at index 0");
            {
                writer.WriteKey(write); // Do this for lulz
                writer.WriteKey("");    // This may help flush it, so write empty
            }
            Assert.AreEqual(0, writer.PopState(), "Must pop state at index 0");
            Assert.That.IsNullOrEmpty(writer.Error, "Error must be null");

            Assert.IsTrue((bool)writer.WriteKey(Key2).WriteLong(Val2, Val2Fmt), "Write must be successful.");
        }
        Assert.That.IsNullOrEmpty(writer.Error, "Error must be null");

        if (writer.Data.CanRead)
        {
            // Ensure rollback of data works properly for the writer and that it correctly truncates.
            string data = writer.Data.ReadWritten();
            string expected = $@"{{""{Key1}"":""{SJEscape.Escape(write, writer.asciiOnly)}"",""{Key2}"":{Val2Prefix}{Val2.ToString(Val2Fmt)}}}";
            Assert.AreEqual(expected, data);
        }
        else
        {
            Console.WriteLine($"[!] Did not validate the data, cannot read data from {writer}.");
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestLargeState()
    {
        throw new NotImplementedException();
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestNegativeWrite()
    {
        // The codepaths are generally the same so I will only test WriteLiteralValue and WriteKey instead
        using var writer = new SJWriter(CreateData());
        writer.ThrowOnError = false;

        using (writer.Object())
        {
            // Mess up the KV order
            writer.Write(3.141592);
            Assert.That.IsNotNullOrEmpty(writer.Error);

            writer.Reset(); // Resetting within an object scope that is successful will call "EndObject" and fail the object.
            writer.BeginObject(); // So start an object to not cause that
                                  // ?? : Perhaps each scope can hold "version" (which changes with every write)
                                  //      of the writer to avoid this footgun? (the versions are checked in the writer as well?)
                                  //      To avoid this, I need to store the version on the scope and the stack and then match the versions (or whether if the version is present on the stack at all. Which is more code so meh).

            // ↓ This one will completely fail, unlike the object that has partially succeeded.
            using (writer.Array()) { }
            Assert.That.IsNotNullOrEmpty(writer.Error);

            // And it will error out with EndObject. Not that it matters
            writer.Reset();
            writer.BeginObject();

            // Test WriteKey twice
            writer.WriteKey("aaa");
            writer.WriteKey("bbb");
            Assert.That.IsNotNullOrEmpty(writer.Error);
            writer.BeginObject();
            Assert.That.IsNotNullOrEmpty(writer.Error);

            // Test WriteKey on array
            writer.Reset();
            writer.BeginObject();
            writer.WriteKey("array");
            using (writer.Array())
            {
                writer.WriteKey("keys on my array? it's more likely than you think");
                Assert.That.IsNotNullOrEmpty(writer.Error);
            }

            writer.Reset();
        }

        Console.WriteLine($"Error present : {writer.Error}");
        writer.Reset();
        Assert.That.IsNullOrEmpty(writer.Error);
    }
}
