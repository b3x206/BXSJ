using System.Globalization;

namespace BX.SJ.Tests;

public abstract class WriterUnitTests<TWriter> where TWriter : ISJDataWriter
{
    public abstract TWriter CreateWriter();
    public virtual bool CheckIsStackable() => typeof(TWriter).IsAssignableTo(typeof(ISJStateStackable));
    public virtual bool AssertIsStackable()
    {
        bool r = CheckIsStackable();
        if (!r) Assert.Inconclusive(@$"Cannot test stack related tests on type {typeof(TWriter)} as it does not implement {typeof(ISJStateStackable)}.
If this warning isn't desired, override this method to return false and remove this line.");
        return r;
    }

    // Write
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void RootWrite(int indent)
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = indent;
        writer.ThrowOnError = false;

        WriterTester.WriteTestRoot(writer);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void Write(int indent)
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = indent;
        writer.ThrowOnError = true;

        // Writing an object that looks like this.
        using (writer.Object())
        {
            // Though ThrowOnError is true, so :shrug:
            Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");

            using (writer.ArrayWithKey("test on le array"))
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
    public void WriteEmojiSpam()
    {
        using var writer = new SJWriter(CreateWriter());
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
    public void InvalidWrite()
    {
        // The codepaths are generally the same so I will only test WriteLiteralValue and WriteKey instead
        using var writer = new SJWriter(CreateWriter());
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
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void DefaultNoCustomKey()
    {
        const string Message = "Error must exist on default state of writing custom key with a writer";
        using var writer = new SJWriter(CreateWriter());
        writer.ThrowOnError = false;

        using (writer.Object())
        {
            writer.WriteKey(1.2424f);
            writer.Write("yes");
        }
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void DefaultNoTypes()
    {
        const string Message = "Error must exist on default state of writing custom type with a writer";
        using var writer = new SJWriter(CreateWriter());
        writer.ThrowOnError = false;

        using (writer.Object())
        {
            writer.WriteKey("Foo");
            writer.WriteType("YesType").Write("yes");
        }
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void DefaultNoComments()
    {
        using var writer = new SJWriter(CreateWriter());
        const string Message = "Error must exist on default state of writing comments with a writer";
        writer.ThrowOnError = false;

        writer.WriteComment("A");
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);

        writer.Reset();
        writer.WriteCommentLine("A");
        Assert.That.IsNotNullOrEmpty(writer.Error, Message);
    }

    // Custom values (WrittenCustomValue) which is checked only by number literals for now are accepted by default.
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void CustomNumberLiterals()
    {
        using var writer = new SJWriter(CreateWriter());
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
    public void CustomKeys()
    {
        using var writer = new SJWriter(CreateWriter());
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
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void Types()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.ThrowOnError = false;
        writer.allowTypes = true;
        writer.indentSize = 0;

        const string Type1 = "Hello", Type2 = "World", Type3 = "SJ", Type4 = "Escaped\"",
            Key1 = "Lorem", Value1 = "Ipsum",
            Key2 = "Dolor", Value2 = "Sit amet",
            Key3 = "Consectetur", Value3 = "Adipiscing elit";

        // * There cannot be two consecutive types
        writer.WriteType(Type1);
        writer.WriteType(Type2);
        Assert.That.IsNotNullOrEmpty(writer.Error);
        writer.Reset();

        // * Root can have type
        writer.WriteType("Hello");
        using (writer.Object())
        {
            // * Key can have type
            Assert.IsTrue((bool)writer
                .WriteType(Type1).WriteKey(Key1)
                .Write(Value1)
            );
            // * Value can have type
            Assert.IsTrue((bool)writer
                .WriteKey(Key2).WriteType(Type2)
                .Write(Value2)
            );
            // * Both can have type
            Assert.IsTrue((bool)writer
                .WriteType(Type3).WriteKey(Key3)
                .WriteType(Type4, true).Write(Value3)
            );
        }

        // Assert data to equal:
        static string E(string s) => SJEscape.Escape(s);
        string expectedData =
            $"(Hello){{({Type1})\"{E(Key1)}\":\"{E(Value1)}\"," +
            $"\"{E(Key2)}\":({Type2})\"{Value2}\"," +
            $"({Type3})\"{E(Key3)}\":(\"{E(Type4)}\")\"{E(Value3)}\"" +
            $"}}";
        if (writer.Data.CanRead)
        {
            Assert.AreEqual(expectedData, writer.Data.ReadWritten());
        }

        // * Cannot write type after ended
        Assert.That.HasFlag(writer.S.expect, SJWriter.Expect.NotType);
        writer.WriteType("");
        Assert.That.IsNotNullOrEmpty(writer.Error);
    }
    [TestMethod]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    [Timeout(TestTimeouts.Short)]
    public void RootComments(int indent)
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = indent;
        writer.ThrowOnError = false;
        writer.allowComments = true;

        WriterTester.WriteTestRootComments(writer);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void Comments(int indent)
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = indent;
        writer.allowComments = true;
        writer.ThrowOnError = true;

        using (writer.Object())
        {
            Assert.IsTrue(WriterTester.WriteTestComments(writer), "Writing must go without any errors");

            using (writer.ArrayWithKey("array"))
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
    public void CommentsMatrix(int indent)
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = indent;
        writer.allowComments = true;
        writer.ThrowOnError = true;

        using (writer.Object())
        {
            WriterTester.WriteTestCommentMatrix(writer);

            using (writer.ArrayWithKey("array"))
            {
                WriterTester.WriteTestCommentMatrix(writer);
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
    public void CommentsPadCharacters()
    {
        using var writer = new SJWriter(CreateWriter());
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
    public void Depth()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = 4;
        writer.ThrowOnError = true;

        writer.maxDepth = SJConstants.MaxDepth;
        const int halfDepth = SJConstants.MaxDepth / 2;
        // Assert.AreNotEqual(0, halfDepth);
        WriterTester.WriteTestDepth(writer, halfDepth);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void MaxDepth()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = 4;
        writer.ThrowOnError = true;

        Assert.Throws<SJWriter.WriteException>(() => WriterTester.WriteMaxTestDepth(writer));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void MaxDepthNoExcept()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = 4;
        writer.ThrowOnError = false;

        Assert.IsFalse(WriterTester.WriteMaxTestDepth(writer), "Depth test must fail");
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
    public void Stack()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = 3;
        writer.ThrowOnError = true;

        Assert.Throws<SJWriter.WriteException>(() => TestStackWith(writer));
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void StackNoExcept()
    {
        using var writer = new SJWriter(CreateWriter());
        writer.indentSize = 4;
        writer.ThrowOnError = false;
        Assert.IsTrue(TestStackWith(writer), "Stack test must fail");
        Assert.That.IsNotNullOrEmpty(writer.Error, "Writer should have an error set after failing");
    }

    // State
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void ReadData()
    {
        using var w = new SJWriter(CreateWriter());
        if (!w.Data.CanRead)
        {
            Assert.Inconclusive($"Cannot test ReadData as data cannot be read for writer with type {typeof(TWriter)}");
            return;
        }

        const string Data = TestData.AZaz09;
        w.Write(Data);

        static string E(string d) => SJEscape.Escape(d);
        string expected = $"\"{E(Data)}\"";
        Assert.AreEqual(expected, w.Data.ReadWritten());

        char[] data = new char[w.Data.Count];
        Assert.AreEqual(data.Length, w.Data.ReadWritten(data));
        Assert.AreEqual(expected, new string(data));
    }
    private static readonly Type[] _ReadDataConsistencyExceptionTypes = [typeof(NotSupportedException), typeof(InvalidOperationException)];
    /// <summary>
    /// Exception types expected from your variety of Writer if it cannot be read.
    /// </summary>
    public virtual Type[] ReadDataConsistencyExceptionTypes => _ReadDataConsistencyExceptionTypes;
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void ReadDataConsistency()
    {
        using var writer = new SJWriter(CreateWriter());
        // Buffered objects may struggle with converting representation to string.
        writer.ThrowOnError = true;
        using (writer.Object())
        {
            Assert.IsTrue(WriterTester.WriteTest(writer), "Writing must go without any errors");

            using (writer.ArrayWithKey("array"))
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
    [DataRow(1)]
    [DataRow(4)]
    public void DisposeTimes(int n)
    {
        // Disposing multiple times shouldn't error.
        SJWriter writer = new(CreateWriter());
        writer.Write(0);
        for (int i = 0; i < n; i++) writer.Dispose();
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void AfterDispose()
    {
        SJWriter writer = new(CreateWriter());
        Action<SJWriter>[] acts = [
            // Anything that must throw is here
            // (since Action does not match these methods exactly this is done)
            static (r) => r.BeginObject(),
            static (r) => r.BeginArray(),
            static (r) => r.WriteLiteralValue("0"),
            static (r) => r.WriteComment("0"),
            static (r) => r.WriteCommentLine("0"),
            static (r) => r.WriteLiteralKey("0", true),  // Is error by default, but must throw the dispose error.
            static (r) => r.WriteType(0),
            static (r) => r.Reset(),
            static (r) => r.Data = null,                 // Will throw InvalidOperationException when disposed
            (r) => { if (CheckIsStackable()) r.PushState(); },
            (r) => { if (CheckIsStackable()) r.PopState(); },
        ];

        writer.Dispose();

        // SJWriter
        for (int i = 0; i < acts.Length; i++)
        {
            Action<SJWriter> mustThrow = acts[i];
            Assert.Throws<InvalidOperationException>(() => mustThrow(writer), $"Action {i + 1} does not throw InvalidOperationException on disposed writer.");
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void ResetClearsState()
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

        using var writer = new SJWriter(CreateWriter());
        writer.ThrowOnError = false;
        writer.allowComments = true;
        writer.allowOtherKeyTypes = true;
        writer.indentSize = 4;

        using (writer.Object())
        {
            using (writer.ArrayWithKey("What is the private key?"))
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

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(
        nameof(TestData.StringDatas), typeof(TestData),
        DynamicDataDisplayName = nameof(TestData.StringDatasDisplayName),
        DynamicDataDisplayNameDeclaringType = typeof(TestData)
    )]
    public void StateStacking(string write)
    {
        // Test this with many strings (including emoji spam,
        // to check for stuff like whether if the StreamWriter has
        // resetted it's state correctly, well as correct as it can be.)
        using var writer = new SJWriter(CreateWriter());
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
                writer.WriteKey("");    // This may help flush any underlying buffers, so write empty
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
            Console.WriteLine($"[!] Did not validate the data, cannot read data from {writer}. Otherwise completed w/o errors.");
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(16)]
    [DataRow(SJConstants.MaxDepth)]
    public void MaxStateStacking(int count)
    {
        using var writer = new SJWriter(CreateWriter())
        {
            maxDepth = 0,           // Disable depth checks
            MaxStateCount = count,
        };

        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual(i, writer.PushState());
            writer.BeginArray(); // Object needs keys
        }
        for (int i = count - 1; i >= 0; i--)
        {
            Assert.AreEqual(i, writer.S.depth - 1); // depth is off by one. this is expected
            Assert.AreEqual(i, writer.PopState());
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(16)]
    [DataRow(SJConstants.MaxDepth)]
    public void InvalidStateStacking(int count)
    {
        if (!AssertIsStackable()) return;

        using var r = new SJWriter(CreateWriter())
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
            Assert.Inconclusive($"Cannot test as type {typeof(TWriter)} is stackable.");
            return;
        }

        using var r = new SJWriter(CreateWriter())
        {
            ThrowOnError = true
        };
        Assert.Throws<SJReader.ReaderException>(() => r.PushState());
        Assert.Throws<SJReader.ReaderException>(() => r.PopState());
    }
}
