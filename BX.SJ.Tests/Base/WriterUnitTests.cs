using System.Globalization;

namespace BX.SJ.Tests;

[TestClass]
public abstract class WriterUnitTests<TWriter> where TWriter : SJWriter
{
    /// <summary>
    /// Initialize an empty instance (that writes to memory generally) of <typeparamref name="TWriter"/>.
    /// </summary>
    public abstract TWriter CreateWriter();
    /// <summary>
    /// Dispose <paramref name="writer"/> object. This method must not catch exceptions.
    /// </summary>
    /// <returns>Only returns whether if <paramref name="writer"/> is a valid 
    /// <see cref="IDisposable"/> and <see cref="IDisposable.Dispose"/> was called.</returns>
    public virtual bool DisposeWriter(TWriter writer)
    {
        if (writer is IDisposable d)
        {
            d.Dispose();
            return true;
        }

        return false;
    }

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
        var writer = CreateWriter();
        try
        {
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

            if (!writer.CanReadData)
            {
                try
                {
                    writer.ReadData();
                }
                catch (Exception ex) when (ReadDataConsistencyExceptionTypes.Contains(ex.GetType()))
                {
                    Console.WriteLine($"Caught false CanReadData exception '{ex}'");
                }
            }
            else
            {
                // Reading written data must equal written count
                Assert.AreEqual(writer.count, writer.ReadData().Length);
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    public virtual void TestDisposeTwice()
    {
        bool firstDisposeValid = false;
        var writer = CreateWriter();
        try
        {
            writer.ThrowOnError = true;
            using (writer.Object())
            {
                WriterTester.WriteTest(writer);
            }

            firstDisposeValid = DisposeWriter(writer);
            if (!firstDisposeValid)
            {
                Assert.Inconclusive($"Writer '{writer}' with type '{writer.GetType()}' is not disposable (according to the test)");
                return;
            }

            Assert.IsTrue(DisposeWriter(writer), "Second dispose must not fail");
            Assert.IsTrue(DisposeWriter(writer), "Third dispose must not fail");
        }
        finally
        {
            if (firstDisposeValid && writer is not null)
            {
                Assert.IsTrue(DisposeWriter(writer), "Fourth dispose must not fail, if first one is valid.");
            }
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestResetClearsState()
    {
        static void ValidateResetState(TWriter w)
        {
            Assert.That.IsNullOrEmpty(w.Error, "Error state must reset on Reset");
            Assert.AreEqual(0, w.count, "Writer count must be zero after reset");
            Assert.AreEqual(0, w.depth, "Writer depth must be zero after reset");
            Assert.AreEqual(SJWriter.Expect.None, w.expect, "Writer expectation must be none for root");
            Assert.AreEqual(SJWriter.FormatExpect.None, w.fmtExpect, "Writer format expectation must be none for root");
            Assert.IsFalse(w.ended, "Writer finish state must be false");
            Assert.IsFalse(w.WrittenComment, "Writer comment state must be false");
            Assert.IsFalse(w.WrittenCustomKey, "Writer custom key state must be false");
            Assert.IsFalse(w.WrittenCustomValue, "Writer custom value state must be false");
        }

        var writer = CreateWriter();
        try
        {
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
                    Assert.IsTrue(writer.WrittenCustomValue, "Custom value was written, but the state hasn't changed.");
                    writer.WriteString("Just pick your favourite numbers <= 256! byte.MaxValue is plenty safe.");
                }

                writer.WriteCommentLine("Is it true?", hasNextValue: true);
                Assert.IsTrue(writer.WrittenComment, "Comment was written, but the state hasn't changed.");

                writer.WriteKeyBool(true);
                writer.WriteBool(true);
                Assert.IsTrue(writer.WrittenCustomKey, "Custom key was written, but the state hasn't changed.");
            }

            // Cause error
            writer.WriteNumber(123);
            Assert.That.IsNotNullOrEmpty(writer.Error);

            // Reset and validate anything that could have potentially changed
            writer.Reset();
            ValidateResetState(writer);

        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    // Write
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestRootWrite(int indent)
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = indent;
            writer.ThrowOnError = false;

            WriterTester.WriteTestRoot(writer);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestWrite(int indent)
    {
        var writer = CreateWriter();
        try
        {
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
            if (writer.CanReadData)
            {
                string? data = writer.ReadData();
                // Validate
                Assert.AreEqual(writer.count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
                ReaderTester.Read(new SJStringReader(data));
                // And show
                Console.WriteLine(data);
            }
            else
            {
                Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Mid)]
    public void TestWriteEmojiSpam()
    {
        var writer = CreateWriter();
        try
        {
            writer.ThrowOnError = true;
            writer.WriteString(TestData.DataEmojiSpam);
            if (!writer.CanReadData)
            {
                Assert.Inconclusive($"[!] Cannot read data from this writer '{writer}'");
            }

            // Buffered writers must truncate and write this correctly
            Assert.AreEqual(writer.ReadData(), $"\"{TestData.DataEmojiSpam}\"");
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    // Invalid Write
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDefaultNoCustomKey()
    {
        var writer = CreateWriter();
        try
        {
            const string Message = "Error must exist on default state of writing custom key with a writer";
            writer.ThrowOnError = false;

            using (writer.Object()) { writer.WriteKeyNumber(1.2424f); writer.Write("yes"); }
            Assert.That.IsNotNullOrEmpty(writer.Error, Message);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    // Custom values (WrittenCustomValue) which is checked only by number literals for now are accepted by default.
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestCustomNumberLiterals()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 0;
            writer.ThrowOnError = true;
            if (!writer.CanReadData)
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
                Assert.AreEqual(n.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", ""), writer.ReadData());
                writer.Reset();

                writer.WriteULong(un, fmt, "");
                Assert.AreEqual(un.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", ""), writer.ReadData());
                writer.Reset();
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public virtual void TestCustomKeys()
    {
        var writer = CreateWriter();
        try
        {
            writer.ThrowOnError = true;
            writer.allowOtherKeyTypes = true;
            writer.indentSize = 0;
            if (!writer.CanReadData)
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

                Assert.AreEqual($"{{{n.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", "")}:null}}", writer.ReadData());
                writer.Reset();

                using (writer.Object())
                {
                    writer.WriteKeyULong(un, fmt, "");
                    writer.WriteNull();
                }

                Assert.AreEqual($"{{{un.ToString(fmt, CultureInfo.InvariantCulture).Replace(",", "").Replace(" ", "")}:null}}", writer.ReadData());
                writer.Reset();
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    // JSC
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDefaultNoComments()
    {
        var writer = CreateWriter();
        try
        {
            const string Message = "Error must exist on default state of writing comments with a writer";
            writer.ThrowOnError = false;

            writer.WriteComment("A");
            Assert.That.IsNotNullOrEmpty(writer.Error, Message);

            writer.Reset();
            writer.WriteCommentLine("A");
            Assert.That.IsNotNullOrEmpty(writer.Error, Message);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    [Timeout(TestTimeouts.Short)]
    public void TestRootJSCWrite(int indent)
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = indent;
            writer.ThrowOnError = false;
            writer.allowComments = true;

            WriterTester.WriteTestRootJSC(writer);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestJSCWrite(int indent)
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = indent;
            writer.allowComments = true;
            writer.ThrowOnError = true;

            using (writer.Object())
            {
                Assert.IsTrue(WriterTester.WriteTestJSC(writer), "Writing must go without any errors");

                using (writer.ArrayKV("array"))
                {
                    Assert.IsTrue(WriterTester.WriteTestJSC(writer), "Writing must go without any errors");
                }
            }

            // Read resulting data
            if (writer.CanReadData)
            {
                string? data = writer.ReadData();
                // Validate
                Assert.AreEqual(writer.count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
                ReaderTester.ReadJSC(new SJStringReader(data));
                // And show
                Console.WriteLine(data);
            }
            else
            {
                Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0, DisplayName = "Indent 0 (No pretty print)")]
    [DataRow(4, DisplayName = "Indent 4")]
    public void TestJSCMatrix(int indent)
    {
        var writer = CreateWriter();
        try
        {
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
            if (writer.CanReadData)
            {
                string? data = writer.ReadData();
                Assert.AreEqual(writer.count, data?.Length, $"Resulting writer counts must match. Non matching writer data: {data}");
                var r = new SJStringReader(data) { ThrowOnError = true };
                ReaderTester.ReadJSC(r);
                Console.WriteLine(data);
            }
            else
            {
                Assert.Inconclusive("[!] Skipping validation of write data as this writer type does not support reading data. If the test has reached here, writing ended without any errors.");
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestJSCPadCharacters()
    {
        var writer = CreateWriter();
        try
        {
            if (!writer.CanReadData)
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
                Assert.AreEqual($"/*{Comment}*/", writer.ReadData(), InvalidPadMessage);

                writer.Reset();
                writer.WriteCommentLine(Comment, invalidPadChar);
                Assert.AreEqual($"//{Comment}", writer.ReadData(), InvalidPadMessage);
            }
            foreach (var validPadChar in Enumerable.Range(1, 127).Where(v => v != '\r' && v != '\n').Select(Convert.ToChar))
            {
                string ValidPadMessage = $"Valid pad char '{(int)validPadChar}' must be written";

                writer.Reset();
                writer.WriteComment(Comment, validPadChar);
                Assert.AreEqual($"/*{validPadChar}{Comment}{validPadChar}*/", writer.ReadData(), ValidPadMessage);

                writer.Reset();
                writer.WriteCommentLine(Comment, validPadChar);
                Assert.AreEqual($"//{validPadChar}{Comment}", writer.ReadData(), ValidPadMessage);
            }
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    // Depth / Stacking
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestDepth()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 4;
            writer.ThrowOnError = true;

            writer.maxDepth = 128;
            Assert.IsFalse(WriterTester.WriteTestDepth(writer, 64));
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [ExpectedException(typeof(SJWriter.WriteException))]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxDepth()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 4;
            writer.ThrowOnError = true;

            Assert.IsTrue(WriterTester.WriteMaxTestDepth(writer), "Depth test must fail"); // ← Must throw WriteException instead
            Console.WriteLine($"fail : {writer}");
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestMaxDepthNoExcept()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 4;
            writer.ThrowOnError = false;

            Assert.IsTrue(WriterTester.WriteMaxTestDepth(writer), "Depth test must fail");
            Assert.That.IsNotNullOrEmpty(writer.Error, "Writer should have an error set after failing");
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    static bool TestStackWith(SJWriter writer)
    {
        writer.BeginObject();

        writer.WriteKey("Fun fact : ");

        writer.BeginArray();
        bool result = !writer.EndObject(); // whoops
        result = result || !writer.EndArray();

        return result;
    }
    [TestMethod]
    [ExpectedException(typeof(SJWriter.WriteException))]
    [Timeout(TestTimeouts.Short)]
    public void TestStack()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 3;
            writer.ThrowOnError = true;

            Assert.IsTrue(TestStackWith(writer), "Stack test must fail");
            Console.WriteLine($"fail : {writer}");
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestStackNoExcept()
    {
        var writer = CreateWriter();
        try
        {
            writer.indentSize = 4;
            writer.ThrowOnError = false;
            Assert.IsTrue(TestStackWith(writer), "Stack test must fail");
            Assert.That.IsNotNullOrEmpty(writer.Error, "Writer should have an error set after failing");
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestNegativeWrite()
    {
        // The codepaths are generally the same so I will only test WriteLiteralValue and WriteKey instead
        var writer = CreateWriter();
        try
        {
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
        finally
        {
            DisposeWriter(writer);
        }
    }
}
