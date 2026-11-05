using System.Collections.Specialized;
using System.Globalization;

namespace BX.SJ.Tests;

public static class WriterTester
{
    // - Extensions
    // Technically, could be a "fluent" extension that takes "object".
    // > But that is out of scope, BXSave will have these natively instead of the core lib authored as a submodule..
    public readonly struct WriteOptions(object value, WriteOptions.Type type)
    {
        public enum Type
        {
            None,
            Format,
            Ascii,

            // ??
            Custom
        }

        public readonly object value = value;
        public readonly Type type = type;

        public static WriteOptions Format(string format)
        {
            return new WriteOptions(format, Type.Format);
        }
        public static WriteOptions Ascii(bool ascii)
        {
            return new WriteOptions(ascii, Type.Ascii);
        }

        public static readonly WriteOptions[] TestConfigs = [Format("G"), Format("R"), Ascii(false), Ascii(true)];
    }
    public static bool Write(this SJWriter writer, object? value, params WriteOptions[] ws)
    {
        ArgumentNullException.ThrowIfNull(writer, nameof(writer));
        bool prevAsciiOnly = writer.asciiOnly;
        try
        {
            var asciiOpt = ws.FirstOrDefault(v => v.type == WriteOptions.Type.Ascii);
            if (asciiOpt.type == WriteOptions.Type.Ascii) writer.asciiOnly = Convert.ToBoolean(asciiOpt.value);

            string fmt = "";
            var fmtOpt = ws.FirstOrDefault(v => v.type == WriteOptions.Type.Format);
            if (fmtOpt.type == WriteOptions.Type.Format) fmt = Convert.ToString(fmtOpt.value)!;

            // common
            if (value is null) return writer.WriteNull();
            else if (value is string str) return writer.WriteString(str);
            else if (value is uint ui) return !string.IsNullOrEmpty(fmt) ? writer.WriteULong(ui, fmt) : writer.WriteULong(ui);
            else if (value is int i) return !string.IsNullOrEmpty(fmt) ? writer.WriteLong(i, fmt) : writer.WriteLong(i);
            else if (value is double d) return !string.IsNullOrEmpty(fmt) ? writer.WriteNumber(d, fmt) : writer.WriteNumber(d);
            else if (value is float f) return !string.IsNullOrEmpty(fmt) ? writer.WriteNumber(f, fmt) : writer.WriteNumber(f);
            else if (value is bool @bool) return writer.WriteBool(@bool);
            // less common
            else if (value is ulong ul) return !string.IsNullOrEmpty(fmt) ? writer.WriteULong(ul, fmt) : writer.WriteULong(ul);
            else if (value is long l) return !string.IsNullOrEmpty(fmt) ? writer.WriteLong(l, fmt) : writer.WriteLong(l);
            else if (value is ushort us) return !string.IsNullOrEmpty(fmt) ? writer.WriteULong(us, fmt) : writer.WriteULong(us);
            else if (value is short s) return !string.IsNullOrEmpty(fmt) ? writer.WriteLong(s, fmt) : writer.WriteLong(s);
            else if (value is byte b) return !string.IsNullOrEmpty(fmt) ? writer.WriteULong(b, fmt) : writer.WriteULong(b);
            else if (value is sbyte sb) return !string.IsNullOrEmpty(fmt) ? writer.WriteLong(sb, fmt) : writer.WriteLong(sb);
            // else if (value is ReadOnlySpan<char> c) return writer.WriteString(str); // must be where T : allows ref struct
            // + upper values can't be matched with "is"
            else throw new ArgumentException($"Value with type {value.GetType()} does not have a supported method on writer", nameof(value));
        }
        finally
        {
            writer.asciiOnly = prevAsciiOnly;
        }
    }
    public static void WriteMustFailAfter(SJWriter writer)
    {
        Assert.That.IsNullOrEmpty(writer.Error, $"Writer error must be empty before checking a failing write. Error : {writer.Error}");
        Assert.IsFalse(writer.Write("Real or fake? No no fake!"), "Writing must fail");
    }

    // - Test
    public static bool WriteTest(SJWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (writer.depth <= 0)
        {
            throw new ArgumentException("Can't write test values to top level object.", nameof(writer));
        }

        int startingIndex = writer.PeekState().index;
        void CheckWriterIndex() => Assert.AreEqual(++startingIndex, writer.PeekState().index, "Expected write to increase top level element counter index");

        bool success = string.IsNullOrEmpty(writer.Error);
        switch (writer.PeekState().type)
        {
            case SJType.Object:
                {
                    // There isn't any checks for duplicate keys.. Be careful!
                    if ((writer.expect & SJWriter.Expect.Value) == SJWriter.Expect.Value)
                    {
                        Console.WriteLine($"The writer {writer} was expecting a value. Setting that key null.");
                        success = success && writer.WriteNull();
                    }

                    // "Index" count is tracked only on the top level recursive structures
                    success = success && writer.WriteKey("yes");
                    success = success && writer.Write(true);
                    CheckWriterIndex();

                    success = success && writer.WriteKey("no");
                    success = success && writer.Write(false);
                    CheckWriterIndex();

                    success = success && writer.WriteKey("maybe");
                    success = success && writer.WriteNull();
                    CheckWriterIndex();

                    success = success && writer.WriteKey("strings");
                    success = success && writer.Write("Did you know : Every minute in here 60 seconds pass. But when I'm programming every key press makes minute pass in twice the speed!");
                    CheckWriterIndex();

                    success = success && writer.WriteKey("integer number");
                    success = success && writer.Write(12345 + 67740);
                    CheckWriterIndex();

                    success = success && writer.WriteKey("float number");
                    success = success && writer.Write(Math.Sin(Math.PI * 0.245) * 100);
                    CheckWriterIndex();

                    // Use WriteKV
                    success = success && writer.WriteKV("yes 2", true);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("no 2", false);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("maybe 2", null);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("strings 2", "bla bla bla");
                    CheckWriterIndex();
                    success = success && writer.WriteKV("calc short for calc", 50 + 3 - 7 + 12 - 5 + 24 - 12 + 53 + -12 - 40 + 1);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("some number", Math.ScaleB(2.5, 4));
                    CheckWriterIndex();

                    success = success && writer.WriteKey("array");
                    if (success)
                        using (writer.Array())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                success = success && writer.Write(i + 1);
                            }
                        }
                    success = success && writer.WriteKey("object");
                    if (success)
                        using (writer.Object())
                        {
                            success = success && writer.WriteKey("uhhh");
                            success = success && writer.Write("idk..");
                        }
                    break;
                }
            case SJType.Array:
                {
                    if ((writer.expect & SJWriter.Expect.Value) == SJWriter.Expect.Value)
                    {
                        Console.WriteLine(
                            $"The writer {writer} was expecting a value." +
                            $"Setting that key null. Wait a minute"
                        );
                        Assert.Fail("NeedValue is true while supposedly on an array.");
                    }

                    success = success && writer.Write(true);
                    CheckWriterIndex();
                    success = success && writer.Write(false);
                    CheckWriterIndex();
                    success = success && writer.WriteNull();
                    CheckWriterIndex();
                    success = success && writer.Write("Did you know : Every minute in here, 60 seconds pass.");
                    CheckWriterIndex();
                    success = success && writer.Write(12345 + 67740);
                    CheckWriterIndex();
                    success = success && writer.Write(Math.Sin(Math.PI * 0.245) * 100);
                    CheckWriterIndex();

                    if (success)
                        using (writer.Array())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                success = success && writer.Write(i + 1);
                            }
                        }

                    if (success)
                        using (writer.Object())
                        {
                            success = success && writer.WriteKey("uhhh");
                            success = success && writer.Write("idk..");
                        }
                    break;
                }
            default:
                throw new ArgumentException($"Unexpected top level type {writer.PeekState().type}", nameof(writer));
        }

        return success;
    }
    public static bool WriteTestJSC(SJWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (writer.depth <= 0)
        {
            throw new ArgumentException("Can't write test values to top level object.", nameof(writer));
        }
        writer.allowComments = true;
        bool success = string.IsNullOrEmpty(writer.Error);

        int startingIndex = writer.PeekState().index;
        void CheckWriterIndex() => Assert.AreEqual(
            ++startingIndex, writer.PeekState().index, "Expected write to increase top level element counter index"
        );
        int commentCount = 0;
        void WriteCommentLine(bool expectValue = false) => success = success && writer.WriteCommentLine(
            $"Comment {++commentCount}", hasNextValue: expectValue
        );
        void WriteComment(bool expectValue = false) => success = success && writer.WriteComment(
            $"Comment {++commentCount}", hasNextValue: expectValue
        );

        // My poor Writer.. My poor writer..
        WriteComment();
        switch (writer.PeekState().type)
        {
            default:
                throw new ArgumentException($"Unexpected top level type {writer.PeekState().type}", nameof(writer));

            case SJType.Object:
                {
                    // There isn't any checks for duplicate keys.. Be careful!
                    if ((writer.expect & SJWriter.Expect.Value) == SJWriter.Expect.Value)
                    {
                        Console.WriteLine($"The writer {writer} was expecting a value. Setting that key null.");
                        success = success && writer.WriteNull();
                    }

                    WriteCommentLine();
                    WriteComment();

                    // "Index" count is tracked only on the top level recursive structures
                    success = success && writer.WriteKey("yes");
                    WriteComment();
                    success = success && writer.Write(true);
                    CheckWriterIndex();

                    WriteComment();
                    success = success && writer.WriteKey("no");
                    success = success && writer.Write(false);
                    WriteComment();
                    CheckWriterIndex();

                    WriteCommentLine();
                    success = success && writer.WriteKey("maybe");
                    WriteCommentLine();
                    success = success && writer.WriteNull();
                    WriteComment();
                    CheckWriterIndex();

                    success = success && writer.WriteKey("strings");
                    WriteComment();
                    WriteComment();
                    WriteCommentLine();
                    success = success && writer.Write("Did you know : Every minute in here 60 seconds pass. But when I'm programming every key press makes minute pass in twice the speed!");
                    WriteCommentLine();
                    CheckWriterIndex();

                    success = success && writer.WriteKey("integer number");
                    WriteCommentLine();
                    WriteCommentLine();
                    success = success && writer.Write(12345 + 67740);
                    CheckWriterIndex();

                    WriteComment();
                    success = success && writer.WriteKey("float number");
                    WriteCommentLine();
                    WriteComment();
                    success = success && writer.Write(Math.Sin(Math.PI * 0.245) * 100);
                    WriteCommentLine();
                    CheckWriterIndex();

                    // Use WriteKV
                    success = success && writer.WriteKV("yes 2", true);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("no 2", false);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("maybe 2", null);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("strings 2", "bla bla bla");
                    CheckWriterIndex();
                    success = success && writer.WriteKV("calc short for calc", 50 + 3 - 7 + 12 - 5 + 24 - 12 + 53 + -12 - 40 + 1);
                    CheckWriterIndex();
                    success = success && writer.WriteKV("some number", Math.ScaleB(2.5, 4));
                    CheckWriterIndex();

                    success = success && writer.WriteKey("array");
                    if (success)
                        using (writer.Array())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                if (i % 2 == 0)
                                {
                                    WriteCommentLine();
                                }
                                else
                                {
                                    WriteComment();
                                }

                                success = success && writer.Write(i + 1);
                            }
                        }
                    success = success && writer.WriteKey("object");
                    if (success)
                        using (writer.Object())
                        {
                            success = success && writer.WriteKey("uhhh");
                            WriteComment();
                            success = success && writer.Write("idk..");
                        }
                    break;
                }
            case SJType.Array:
                {
                    if ((writer.expect & SJWriter.Expect.Value) == SJWriter.Expect.Value)
                    {
                        Console.WriteLine(
                            $"The writer {writer} was expecting a value." +
                            $"Setting that key null. Wait a minute"
                        );
                        Assert.Fail("NeedValue is true while supposedly on an array.");
                    }

                    success = success && writer.Write(true);
                    WriteComment();
                    WriteCommentLine();
                    CheckWriterIndex();
                    WriteComment();
                    success = success && writer.Write(false);
                    WriteCommentLine();
                    CheckWriterIndex();
                    WriteCommentLine();
                    success = success && writer.WriteNull();
                    WriteCommentLine();
                    CheckWriterIndex();
                    WriteCommentLine();
                    success = success && writer.Write("Did you know : Every minute in here, 60 seconds pass.");
                    WriteComment();
                    CheckWriterIndex();
                    WriteComment();
                    success = success && writer.Write(12345 + 67740);
                    WriteComment();
                    CheckWriterIndex();
                    WriteComment();
                    WriteComment();
                    success = success && writer.Write(Math.Sin(Math.PI * 0.245) * 100);
                    WriteComment();
                    CheckWriterIndex();

                    if (success)
                        using (writer.Array())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                success = success && writer.Write(i + 1);
                            }
                        }

                    if (success)
                        using (writer.Object())
                        {
                            success = success && writer.WriteKey("uhhh");
                            success = success && writer.Write("idk..");
                        }
                    break;
                }
        }

        return success;
    }
    public static void WriteTestJSCMatrix(SJWriter writer, int writeCount = 3)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.allowComments = true;
        writer.ThrowOnError = true;

        int commentCount = 0;
        void WriteComment(bool expectValue = false) => writer.WriteComment($"Comment {++commentCount} */ :)", hasNextValue: expectValue);
        void WriteCommentLine(bool expectValue = false) => writer.WriteCommentLine($"Comment {++commentCount} :) //", hasNextValue: expectValue);

        switch (writer.PeekState().type)
        {
            default:
                throw new ArgumentException($"Unexpected top level type {writer.PeekState().type}", nameof(writer));

            case SJType.Object:
                {
                    if (!writer.expect.HasFlag(SJWriter.Expect.Key)) writer.WriteNull();

                    // Test the following pattern for Object:
                    // [comment] [line] "key": "value"
                    // [line] "key": [comment] "value"
                    // [line] "key": "value" [comment] 
                    // [comment] "key": [line] "value" 
                    // "key": [comment] [line] "value" 
                    // "key": [line] "value" [comment]
                    // [comment] "key": "value" [line]
                    // "key": [comment] "value" [line]
                    // "key": "value" [line] [comment]
                    for (int i = 0; i < writeCount; i++)
                    {
                        for (int newline = 0; newline < 3; newline++)
                        {
                            for (int comment = 0; comment < 3; comment++)
                            {
                                if (comment == 0) WriteComment(true);
                                if (newline == 0) WriteCommentLine(true);

                                writer.WriteKey($"newline {newline} + {i}");
                                if (comment == 1) WriteComment(true);
                                if (newline == 1) WriteCommentLine(true);

                                writer.WriteString($"comment {comment} + {i}");
                                if (comment == 2) WriteComment(i < (writeCount - 1));
                                if (newline == 2) WriteCommentLine(i < (writeCount - 1));
                            }
                        }
                    }
                    break;
                }
            case SJType.Array:
                {
                    for (int i = 0; i < writeCount; i++)
                    {
                        for (int newline = 0; newline < 2; newline++)
                        {
                            for (int comment = 0; comment < 2; comment++)
                            {
                                if (comment == 0) WriteComment(true);
                                if (newline == 0) WriteCommentLine(true);

                                writer.Write($"newline + comment {newline} + {comment} + {i}");
                                if (comment == 1) WriteComment(i < (writeCount - 1));
                                if (newline == 1) WriteCommentLine(i < (writeCount - 1));
                            }
                        }
                    }
                    break;
                }
        }
    }

    public static bool WriteMaxTestDepth(SJWriter writer)
    {
        writer.maxDepth = Math.Max(writer.maxDepth, 128);
        return WriteTestDepth(writer, writer.maxDepth + 1);
    }
    public static bool WriteTestDepth(SJWriter writer, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            if ((i % 2) == 0)
            {
                if (!writer.BeginObject()) return true;
            }
            else
            {
                // Previous entry was Object, which should be "valid" if it didn't fail
                if (!writer.WriteKey("a")) return false;
                if (!writer.BeginArray()) return true;
            }
        }

        return false;
    }
    public static void WriteTestRoot(SJWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        void WrittenEquals<T>(T? v, string s, params WriteOptions[] ws)
        {
            foreach (var newlineAfterMl in new[] { true, false })
            {
                writer.Write(v, ws: ws);
                if (!writer.CanReadData)
                {
                    Assert.Inconclusive($"[!] Cannot TestRootWith on writer type '{writer.GetType().AssemblyQualifiedName}' as data can't be read. Comparison expected was for string : '{s}'");
                }
                Assert.AreEqual(s, writer.ReadData());
                WriteMustFailAfter(writer);
                writer.Reset();
            }

            writer.Reset();
        }

        // Root level writes
        WrittenEquals(true, "true");
        WrittenEquals(false, "false");
        WrittenEquals<object>(null, "null");

        const string IntFmt = "G";
        const string FloatFmt = "R";
        var numberCulture = CultureInfo.InvariantCulture;

        const int IntVal = 676767;
        WrittenEquals(IntVal, IntVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const long LongVal = 1234123412341234123L;
        WrittenEquals(LongVal, LongVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const ulong ULongVal = 12341234123412341234UL;
        WrittenEquals(ULongVal, ULongVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const float FloatVal = 1.234f;
        WrittenEquals(FloatVal, FloatVal.ToString(FloatFmt, numberCulture), WriteOptions.Format(FloatFmt));

        const double DoubleVal = 0.1d + 0.2d;
        WrittenEquals(DoubleVal, DoubleVal.ToString(FloatFmt, numberCulture), WriteOptions.Format(FloatFmt));

        // Well I didn't know this was possible
        const string DoubleFmt = "G999";
        WrittenEquals(DoubleVal, DoubleVal.ToString(DoubleFmt, numberCulture), WriteOptions.Format(DoubleFmt));

        // Generally, the test content isn't escaped (it's written as is)
        // The escaping is "tested", so I will not test that..
        const string TestContent = "Quote: \", Backslash: \\, Tab: \t, Newline: \n, Pizza: \uD83C\uDF55, The 🅱 variant: \uD83C\uDD71\uFE0F";
        foreach (var asciiOnly in new[] { false, true })
        {
            WrittenEquals(TestContent, $"\"{SJEscape.Escape(TestContent, asciiOnly)}\"", WriteOptions.Ascii(asciiOnly));
        }
    }
    public static void WriteTestRootJSC(SJWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        const char Pad = ' '; // PAD MUST NOT BE '\0', '\r' or '\n'. This is tested in the WriterUnitTests
        const string C1 = "CommentLine", C2 = "Comment ML", C3 = "Last Comment Line";
        void WrittenEquals<T>(T? v, string s, params WriteOptions[] ws)
        {
            foreach (var nl in new[] { false, true })
            {
                writer.WriteCommentLine(C1, Pad);
                writer.Write(v, ws: ws);
                writer.WriteComment(C2, Pad, newlineImmediately: nl);
                writer.WriteCommentLine(C3, Pad);
                if (!writer.CanReadData)
                {
                    Assert.Inconclusive($"[!] Cannot TestRootWith on writer type '{writer.GetType().AssemblyQualifiedName}' as data can't be read. Comparison expected was for string : '{s}'");
                }

                // Depth=0 writes now work like "pretty print", if printing line.
                Assert.AreEqual($"//{Pad}{C1}\n{s}\n/*{Pad}{C2}{Pad}*/{(writer.indentSize > 0 ? "\n" : "")}//{Pad}{C3}", writer.ReadData());
                WriteMustFailAfter(writer);
                writer.Reset();
            }

            writer.Reset();
        }

        writer.indentSize = 4;
        writer.allowComments = true;

        // Root level writes
        WrittenEquals(true, "true");
        WrittenEquals(false, "false");
        WrittenEquals<object>(null, "null");

        const string IntFmt = "G";
        const string FloatFmt = "R";
        var numberCulture = CultureInfo.InvariantCulture;

        const int IntVal = 676767;
        WrittenEquals(IntVal, IntVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const long LongVal = 1234123412341234123L;
        WrittenEquals(LongVal, LongVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const ulong ULongVal = 12341234123412341234UL;
        WrittenEquals(ULongVal, ULongVal.ToString(IntFmt, numberCulture), WriteOptions.Format(IntFmt));

        const float FloatVal = 1.234f;
        WrittenEquals(FloatVal, FloatVal.ToString(FloatFmt, numberCulture), WriteOptions.Format(FloatFmt));

        const double DoubleVal = 0.1d + 0.2d;
        WrittenEquals(DoubleVal, DoubleVal.ToString(FloatFmt, numberCulture), WriteOptions.Format(FloatFmt));

        // Well I didn't know this was possible
        const string DoubleFmt = "G999";
        WrittenEquals(DoubleVal, DoubleVal.ToString(DoubleFmt, numberCulture), WriteOptions.Format(DoubleFmt));

        // Generally, the test content isn't escaped (it's written as is)
        // The escaping is "tested", so I will not test that..
        const string TestContent = "Quote: \", Backslash: \\, Tab: \t, Newline: \n, Pizza: \uD83C\uDF55, The 🅱 variant: \uD83C\uDD71\uFE0F";
        foreach (var asciiOnly in new[] { false, true })
        {
            WrittenEquals(TestContent, $"\"{SJEscape.Escape(TestContent, asciiOnly)}\"", WriteOptions.Ascii(asciiOnly));
        }
    }
}
