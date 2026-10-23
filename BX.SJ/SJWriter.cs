using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Buffers;

namespace BX.SJ
{
    /// <summary>
    /// Base class for <i>most</i> JSON writers.
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// using BX.SJ;
    /// using System;
    /// 
    /// // An example class would look like this:
    /// public sealed class SJExampleWriter : SJWriter
    /// {
    ///     private readonly WriteSource data;
    /// 
    ///     public SJExampleWriter(WriteSource data)
    ///     {
    ///         this.data = data ?? throw new ArgumentNullException(nameof(data));
    ///     }
    /// 
    ///     public override void Append(char c) => data.Write(c);
    ///     public override void Append(ReadOnlySpan<char> s) => data.Write(s);
    /// 
    ///     public override bool CanReadData => true;
    ///     public override string ReadData()
    ///     {
    ///         // Convert the resulting data to string. Called if necessary
    ///         // (generally with test code or with in-memory resources.)
    ///         // Could be unnecessary on things like Stream- based writers. Default impl is not supported.
    ///         return data.ToString();
    ///     }
    ///
    ///     public override void Reset()
    ///     {
    ///         base.Reset();
    ///         data.Clear(); // Or reset the position to start and truncate remaining data.
    ///     }
    /// }
    /// ]]>
    /// </example>
    public abstract class SJWriter
    {
        // A span based writer would be nice, but then I have to pass it
        // into the method always instead of having the span stored inside the writer.
        // I could create a "SJSpanWriter" that does "the same behaviour",
        // but it's repeated code. So let's be "heap only" for the time being,
        // until the library becomes more stable. Or it's "BXSave" territory (leaning towards the latter)
        // ---

#pragma warning disable IDE0057 // Unity does not support System.Range

        /// <summary>
        /// Exception thrown on an error case while reading JSON.
        /// </summary>
        public class WriteException : Exception
        {
            public WriteException(SJWriter writer) : base($"{writer.Error} | at char={writer.count}")
            { }
            public WriteException(string message, int count) : base($"{message} | at char={count}")
            { }
            public WriteException(string message) : base(message)
            { }
        }
        /// <summary>
        /// A tracker for type information that can be stacked and indexed (i.e. recursive data)
        /// </summary>
        public struct State
        {
            /// <summary>
            /// Write type, this is generally either 
            /// <see cref="SJType.Array"/> or <see cref="SJType.Object"/>
            /// </summary>
            public SJType type;
            /// <summary>
            /// Currently stored write index (used more like a "count").
            /// </summary>
            public int index;

            /// <summary>
            /// Whether if this entry is valid or not.
            /// </summary>
            public readonly bool Valid => type != SJType.Error && type != SJType.End && index >= 0;

            public static readonly State Invalid = new State(SJType.Error) { index = -1 };
            public State(SJType type)
            {
                this.type = type;
                index = 0;
            }
            public static implicit operator State(SJType type) => new State(type);

            public override readonly string ToString()
            {
                if (!Valid)
                {
                    return $"[{base.ToString()}]";
                }

                return $"[{base.ToString()}] type={type}, index={index}";
            }
        }

        public readonly struct ObjectScope : IDisposable
        {
            private readonly SJWriter writer;
            private readonly bool success;

            public ObjectScope(SJWriter writer)
            {
                this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
                success = writer.BeginObject();
            }

            public void Dispose()
            {
                if (success)
                {
                    writer.EndObject();
                }
            }
        }
        public readonly struct ArrayScope : IDisposable
        {
            private readonly SJWriter writer;
            private readonly bool success;

            public ArrayScope(SJWriter writer)
            {
                this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
                success = writer.BeginArray();
            }

            public void Dispose()
            {
                if (success)
                {
                    writer.EndArray();
                }
            }
        }

        // settings
        /// <summary>
        /// The indent size to use. If this is 0 or less, no pretty printing is done.
        /// </summary>
        public int indentSize = 0;
        /// <summary>
        /// Whether if this writer has written a JSON document that is done.
        /// </summary>
        public bool finish = false;
        /// <summary>
        /// Maximum size for <see cref="writeStack"/>
        /// </summary>
        public int maxDepth = 128;
        /// <summary>
        /// Whether to allow any <see cref="WriteComment"/>.
        /// </summary>
        public bool allowComments = false;
        /// <summary>
        /// Whether to allow any <see cref="WriteCustomKey"/>.
        /// </summary>
        public bool allowOtherKeyTypes = false;
        /// <summary>
        /// Prefer ascii only string output. This can be used as a compatibility measure 
        /// with older servers / systems not behaving well with non-ascii strings.
        /// </summary>
        public bool asciiOnly = false;
        protected bool _ThrowOnError = false;
        /// <summary>
        /// When an erroreneous case occurs (or <see cref="Error"/> is set to anything other 
        /// than null/<see cref="string.Empty"/>), setting the <see cref="Error"/> will throw.
        /// <br>Setting this <see langword="true"/> while the <see cref="Error"/> 
        /// is not null will throw the currently existing error.</br>
        /// </summary>
        public bool ThrowOnError
        {
            get => _ThrowOnError;
            set
            {
                _ThrowOnError = value;

                if (_ThrowOnError && !string.IsNullOrEmpty(Error))
                {
                    ThrowError();
                }
            }
        }

        // state
        /// <summary>
        /// Amount written by this writer.
        /// </summary>
        public int count;
        public int depth;
        public bool WrittenComment { get; protected set; }
        public bool WrittenCustomKey { get; protected set; }
        protected string _Error = string.Empty;
        /// <summary>
        /// The error string detailing why the writer failed.
        /// </summary>
        public string Error
        {
            get => _Error;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _Error = string.Empty;
                    return;
                }

                _Error = value;
                if (_ThrowOnError)
                {
                    ThrowError();
                }
            }
        }
        protected const int DefaultStackSize = 8;
        protected State[] _stateStack = new State[DefaultStackSize];
        private State _stubState = State.Invalid;
        public bool HasState => depth > 0;
        public virtual int PushState(State s)
        {
            if ((_stateStack?.Length - 1) < depth)
            {
                System.Array.Resize(ref _stateStack, _stateStack.Length <= 0 ? DefaultStackSize : _stateStack.Length * 2);
            }
            int pushIndex = depth++;
            _stateStack[pushIndex] = s;
            return pushIndex;
        }
        public virtual ref State PeekState()
        {
            if (!HasState)
            {
                _stubState = State.Invalid;
                return ref _stubState;
            }
            return ref _stateStack[depth - 1];
        }
        public virtual ref State PopState()
        {
            // PopState should throw as it mutates..
            if (!HasState) throw new InvalidOperationException("Cannot peek state while there is no state.");
            return ref _stateStack[--depth];
        }
        [Flags]
        public enum Expect { None, Key = 1 << 0, Value = 1 << 1, Comma = 1 << 2 }
        public Expect expect = Expect.None;
        [Flags]
        public enum FormatExpect { None, Newline = 1 << 0, Indent = 1 << 1, NewlineIndent = Newline | Indent }
        public FormatExpect fmtExpect = FormatExpect.None; // Newline is done first, then indent.

        /// <summary>
        /// Append to the underlying StringBuilder/Stream-like object.
        /// <br>You can use this to write anything in anywhere, but it isn't recommended.</br>
        /// </summary>
        public abstract void Append(char c);
        /// <summary>
        /// <inheritdoc cref="Append(char)"/>
        /// </summary>
        public virtual void Append(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                Append(s[i]);
            }
        }

        /// <summary>
        /// Throws the currently stored error. It does not check whether if an error is available.
        /// </summary>
        /// <exception cref="WriteException"></exception>
        public virtual void ThrowError()
        {
            throw new WriteException(this);
        }

        // The base writers
        private static readonly Action<SJWriter, char> selfAppend = (SJWriter writer, char c) => writer.Append(c);
        protected void WriteIndent(int depth)
        {
            if (depth <= 0 || indentSize <= 0)
            {
                return;
            }

            Span<char> indent = indentSize < 32 ? stackalloc char[indentSize] : new char[indentSize];
            for (int i = 0; i < indent.Length; i++) { indent[i] = ' '; }
            for (int i = 0; i < depth; i++)
            {
                Append(indent);
            }
            count += indentSize * depth;
        }
        protected virtual void WriteEscaped(ReadOnlySpan<char> data, bool asciiOnly)
        {
            count += SJEscape.Escape(this, selfAppend, data, asciiOnly);
        }
        protected void PrepareFormat()
        {
            if (fmtExpect == FormatExpect.None) return;
            if (indentSize <= 0)
            {
                // On zero depth, newlines are allowed again for "single line comment" related reasons.
                if (depth <= 0 && (fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Append('\n'); count++; }
                fmtExpect = FormatExpect.None;
                return;
            }

            if ((fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Append('\n'); count++; }
            if ((fmtExpect & FormatExpect.Indent) == FormatExpect.Indent) { WriteIndent(depth); }

            fmtExpect = FormatExpect.None;
        }
        protected void PrepareValue()
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return;
            }

            ref State top = ref PeekState();
            if (top.Valid)
            {
                if ((expect & Expect.Comma) == Expect.Comma && top.index > 0)
                {
                    Append(',');
                    count++;
                    expect &= ~Expect.Comma;
                }

                // keys shouldn't affect the write index as it's a pair
                if ((expect & Expect.Key) != Expect.Key)
                {
                    top.index++;
                }
            }

            // Do this regardless of depth now
            PrepareFormat();
        }
        protected void PrepareCommentValue(bool expectValue)
        {
            if (expectValue && depth > 0)
            {
                if ((expect & Expect.Comma) == Expect.Comma)
                {
                    Append(',');
                    count++;
                    expect &= ~Expect.Comma;
                }
            }

            PrepareFormat();
        }
        protected void PrepareEndValue(int prevIndex)
        {
            if (indentSize > 0 && prevIndex > 0)
            {
                Append('\n');
                count++;

                WriteIndent(depth);

                fmtExpect = FormatExpect.None;
            }
        }

        public bool BeginObject()
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if (maxDepth > 0 && depth >= maxDepth)
            {
                Error = $"Exceeded max write depth '{maxDepth}'";
                return false;
            }

            PrepareValue();
            Append('{');
            count++;

            PushState(SJType.Object);

            expect = Expect.Key;
            fmtExpect = FormatExpect.NewlineIndent;

            return true;
        }
        public bool EndObject()
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            ref State top = ref PeekState();
            if (!top.Valid || top.type != SJType.Object)
            {
                Error = $"Invalid EndObject for toplevel with type '{top.type}', expected Object";
                return false;
            }
            if ((expect & Expect.Value) == Expect.Value)
            {
                Error = "Expected writing object value";
                return false;
            }
            if ((expect & Expect.Key) != Expect.Key && (expect & Expect.Comma) != Expect.Comma)
            {
                Error = "Trailing comma written - the comment functions may cause this, if no end of content is specified.";
                return false;
            }

            // Can end object
            expect = Expect.None;

            int prevIndex = top.index;
            PopState();
            top = ref PeekState();

            if (depth <= 0)
            {
                finish = true;
            }
            else
            {
                switch (top.type)
                {
                    case SJType.Array:
                        break;
                    case SJType.Object:
                        expect = Expect.Key;
                        break;

                    default:
                        Error = $"Invalid top state type {top.type}";
                        return false;
                }
                if (top.index > 0) expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            fmtExpect = FormatExpect.NewlineIndent;

            Append('}');
            count++;

            return true;
        }
        public bool BeginArray()
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if (maxDepth > 0 && depth >= maxDepth)
            {
                Error = $"Exceeded max write depth '{maxDepth}'";
                return false;
            }

            PrepareValue();
            Append('[');
            count++;

            expect = Expect.None;
            fmtExpect = FormatExpect.NewlineIndent;

            PushState(SJType.Array);
            return true;
        }
        public bool EndArray()
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if ((expect & Expect.Comma) != Expect.Comma)
            {
                // We are write only. In future I may add record the last comma,
                // roll back until it, replace it with whitespace and then proceed.
                Error = "Trailing comma written - the comment functions may cause this, if no end of content is specified.";
                return false;
            }
            ref State top = ref PeekState();
            if (!top.Valid || top.type != SJType.Array)
            {
                Error = $"Invalid EndObject for toplevel with type '{top.type}', expected Array";
                return false;
            }

            expect = Expect.None;

            int prevIndex = top.index;
            PopState();
            top = ref PeekState();

            if (depth <= 0)
            {
                finish = true;
            }
            else
            {
                switch (top.type)
                {
                    case SJType.Array:
                        break;
                    case SJType.Object:
                        expect = Expect.Key;
                        break;

                    default:
                        Error = $"Invalid top state type {top.type}";
                        return false;
                }
                if (top.index > 0) expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            fmtExpect = FormatExpect.NewlineIndent;

            Append(']');
            count++;

            return true;
        }

        /// <summary>
        /// Write a key, while <see cref="expect"/> has <see cref="Expect.Key"/>.
        /// <b>This method will output the <paramref name="value"/> literally so it's considered a custom key.</b>
        /// </summary>
        /// <remarks>
        /// No duplicates are checked.
        /// </remarks>
        /// <param name="value">Name of the key entry.</param>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteLiteralKey(ReadOnlySpan<char> value)
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) != Expect.Key)
            {
                Error = "Did not expect an object key";
                return false;
            }

            WrittenCustomKey = true;

            PrepareValue();
            Append(value);
            Append(':'); count++;

            if (indentSize > 0)
            {
                Append(' ');
                count++;
            }

            expect = Expect.Value;
            fmtExpect = FormatExpect.None;

            return true;
        }
        /// <summary>
        /// Write a key, while <see cref="expect"/> has <see cref="Expect.Key"/>.
        /// </summary>
        /// <remarks>
        /// Base implementation <b>does not check duplicate keys for the time being within the depth..</b>
        /// </remarks>
        /// <param name="name">Name of the key entry.</param>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteKey(ReadOnlySpan<char> name)
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) != Expect.Key)
            {
                Error = "Did not expect an object key";
                return false;
            }

            PrepareValue();

            Append('"'); count++;
            WriteEscaped(name, asciiOnly);
            Append('"'); count++;

            Append(':'); count++;

            if (indentSize > 0)
            {
                Append(' ');
                count++;
            }

            expect = Expect.Value;
            fmtExpect = FormatExpect.None;

            return true;
        }
        public bool WriteKeyNumber(float number, string format = "R")
        {
            // number.ToString("G999") is totally reasonable and possible. So for floats, this is done.
            // Integers get a fixed size buffer.
            int capacity = 32, written = 0;
            Span<char> data = stackalloc char[capacity];
            char[] rented = null;

            try
            {
                while (!number.TryFormat(data, out written, format, CultureInfo.InvariantCulture))
                {
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);

                    capacity *= 2;
                    data = rented = ArrayPool<char>.Shared.Rent(capacity);
                }

                return WriteLiteralKey(data.Slice(0, written));
            }
            finally
            {
                if (!(rented is null))
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }
        public bool WriteKeyNumber(double number, string format = "R")
        {
            // number.ToString("G999") is totally reasonable and possible. So for floats, this is done.
            // Integers get a fixed size buffer.
            int capacity = 32, written = 0;
            Span<char> data = stackalloc char[capacity];
            char[] rented = null;

            try
            {
                while (!number.TryFormat(data, out written, format, CultureInfo.InvariantCulture))
                {
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);

                    capacity *= 2;
                    data = rented = ArrayPool<char>.Shared.Rent(capacity);
                }

                return WriteLiteralKey(data.Slice(0, written));
            }
            finally
            {
                if (!(rented is null))
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }
        public bool WriteKeyLong(long number, string format = "G")
        {
            Span<char> data = stackalloc char[20]; // long.MinValue.ToString().Length == 20

            if (!number.TryFormat(data, out int written, format, CultureInfo.InvariantCulture))
            {
                Error = $"Failed to convert number to string {number} with format {format}";
                return false;
            }

            return WriteLiteralKey(data.Slice(0, written));
        }
        public bool WriteKeyULong(ulong number, string format = "G")
        {
            Span<char> data = stackalloc char[20]; // long.MinValue.ToString().Length == 20

            if (!number.TryFormat(data, out int written, format, CultureInfo.InvariantCulture))
            {
                Error = $"Failed to convert number to string {number} with format {format}";
                return false;
            }

            return WriteLiteralKey(data.Slice(0, written));
        }
        public bool WriteKeyBool(bool value) => WriteLiteralKey(value ? "true" : "false");
        public bool WriteKeyNull() => WriteLiteralKey("null");

        /// <summary>
        /// Write a comment for JSON, starting with /* and ending with */.
        /// </summary>
        /// <param name="data">Data inside the comment to write. It is padded with spaces by default.</param>
        /// <param name="pad">Padding left and right character to use. Pass 0 to not pad the <paramref name="data"/> with anything. Invalid characters (like '\n') also avoids padding.</param>
        /// <param name="hasNextValue">
        /// <br>To print "prettier", add a trailing comma or colon to the previous value. This will look nicer like</br>
        /// <c><br>"value": // ...\n</br></c>
        /// <br>instead of the usual but always valid <c>"value" // ...\n:</c></br>
        /// <br><b>Set this <see langword="true"/> with caution, as you can produce "invalid" JSC with it.</b></br>
        /// </param>
        /// <param name="newlineImmediately">Append a \n character after writing multiline comment. Only works with pretty printing.</param>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteComment(ReadOnlySpan<char> data, char pad = ' ', bool hasNextValue = false, bool newlineImmediately = false)
        {
            if (!allowComments)
            {
                Error = "Cannot write comments on this writer, enable allowComments";
                return false;
            }

            WrittenComment = true;
            PrepareCommentValue(hasNextValue);

            Append("/*"); count += 2;
            for (int i = -1; i <= data.Length; i++)
            {
                if (i < 0 || i >= data.Length)
                {
                    if (pad != 0 && pad != '\r' && pad != '\n') { Append(pad); count++; }
                    continue;
                }

                char c = data[i];
                if (c == '*' && (i < (data.Length - 1) ? data[i + 1] : '\0') == '/')
                {
                    // Very fun escape action (Write as '*\/' instead of '*/' which ends comment)
                    Append(c); count++;
                    Append('\\'); count++;
                    continue;
                }

                Append(c); count++;
                if (c == '\n')
                {
                    WriteIndent(depth);
                }
            }
            Append("*/"); count += 2;

            fmtExpect = FormatExpect.None;
            if (indentSize > 0)
            {
                fmtExpect = FormatExpect.NewlineIndent;
                if (newlineImmediately)
                {
                    Append('\n'); count++;
                    fmtExpect &= ~FormatExpect.Newline;
                }
            }

            return true;
        }
        /// <summary>
        /// Write a comment for JSON, starting with // and ending with newline character
        /// </summary>
        /// <remarks>
        /// If no pretty printing and writing inside, this calls <see cref="WriteComment"/> instead.
        /// <br>If no pretty printing, but not writing inside, this will do a new line.</br>
        /// </remarks>
        /// <param name="hasNextValue">
        /// <br>To print "prettier", add a trailing comma or colon to the previous value. This will look nicer like</br>
        /// <c><br>["value", // ...\n ... </br></c>
        /// <br>instead of the usual <c>["value" // ...\n,</c></br>
        /// <br><b>Set this <see langword="true"/> with caution, as you can produce invalid Json (trailing commas) with it.</b></br>
        /// </param>
        /// <param name="data">Data inside the comment to write. It is padded with spaces by default.</param>
        /// <param name="prefixPad">Padding prefix character to use. Pass 0 to not pad the <paramref name="data"/> with anything. Invalid characters (like '\n') also avoids padding.</param>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteCommentLine(ReadOnlySpan<char> data, char prefixPad = ' ', bool hasNextValue = false)
        {
            if (!allowComments)
            {
                Error = "Cannot write comments on this writer, enable allowComments";
                return false;
            }
            if (depth > 0 && indentSize <= 0)
            {
                return WriteComment(data, prefixPad, hasNextValue);
            }

            WrittenComment = true;
            PrepareCommentValue(hasNextValue);

            Append("//"); count += 2;
            for (int i = -1; i < data.Length; i++)
            {
                if (i < 0)
                {
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Append(prefixPad); count++; }
                    continue;
                }

                char c = data[i];
                Append(c); count++;
                if (c == '\n')
                {
                    WriteIndent(depth);
                    Append("//"); count += 2;
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Append(prefixPad); count++; }
                }
            }

            fmtExpect = FormatExpect.NewlineIndent;
            // something something FormatExpect is done after Expect tokens and this is whitespace sensitive
            if (depth > 0 && (expect & Expect.Comma) == Expect.Comma)
            {
                Append('\n'); count++;
                fmtExpect &= ~FormatExpect.Newline;
            }

            return true;
        }

        /// <summary>
        /// Writes a literal value with the rules for the top level object or array.
        /// <br><b>Warning :</b> Using this, you can write invalid JSON
        /// (because it writes the value as is, but with the value rules and indenting).
        /// This method is not recommended for use, unless you know what you are doing.</br>
        /// <br>If on a object or array, comma is appended before the <paramref name="data"/> 
        /// (on <see cref="PrepareValue"/>). Use <see cref="WriteComment"/>- family of methods to write comment instead of this!.</br>
        /// <br>This also can be used as a faster way to write numbers if you have data for that.</br>
        /// </summary>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteLiteralValue(ReadOnlySpan<char> data)
        {
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key, got WriteLiteral";
                return false;
            }

            PrepareValue();

            Append(data);
            count += data.Length;

            if (depth <= 0)
            {
                // Value is top level
                finish = true;
            }
            else
            {
                var t = PeekState().type;
                switch (t)
                {
                    case SJType.Object:
                        expect = Expect.Key | Expect.Comma;
                        break;
                    case SJType.Array:
                        expect = Expect.Value | Expect.Comma;
                        break;
                    default:
                        Error = $"Invalid top level type {t}";
                        return false;
                }
            }
            fmtExpect = FormatExpect.NewlineIndent;

            return true;
        }
        public virtual bool WriteString(ReadOnlySpan<char> value)
        {
            // This one does some things different enough that it can't be "WriteLiteral"
            if (finish)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((expect & Expect.Key) == Expect.Key)
            {
                // eh sure, works, similar intent
                return WriteKey(value);
            }

            PrepareValue();

            Append('"');
            count++;

            WriteEscaped(value, asciiOnly);

            Append('"');
            count++;

            if (depth <= 0)
            {
                finish = true;
            }
            else
            {
                var t = PeekState().type;
                switch (t)
                {
                    case SJType.Object:
                        expect = Expect.Key | Expect.Comma;
                        break;
                    case SJType.Array:
                        expect = Expect.Value | Expect.Comma;
                        break;
                    default:
                        Error = $"Invalid top level type {t}";
                        return false;
                }
            }
            fmtExpect = FormatExpect.NewlineIndent;

            return true;
        }
        public bool WriteNumber(float number, string format = "R")
        {
            // number.ToString("G999") is totally reasonable and possible. So for floats, this is done.
            // Integers get a fixed size buffer.
            int capacity = 32, written = 0;
            Span<char> data = stackalloc char[capacity];
            char[] rented = null;

            try
            {
                while (!number.TryFormat(data, out written, format, CultureInfo.InvariantCulture))
                {
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);

                    capacity *= 2;
                    data = rented = ArrayPool<char>.Shared.Rent(capacity);
                }

                return WriteLiteralValue(data.Slice(0, written));
            }
            finally
            {
                if (!(rented is null))
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }
        public bool WriteNumber(double number, string format = "R")
        {
            int capacity = 32, written = 0;
            Span<char> data = stackalloc char[capacity];
            char[] rented = null;

            try
            {
                while (!number.TryFormat(data, out written, format, CultureInfo.InvariantCulture))
                {
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);

                    capacity *= 2;
                    data = rented = ArrayPool<char>.Shared.Rent(capacity);
                }

                return WriteLiteralValue(data.Slice(0, written));
            }
            finally
            {
                if (!(rented is null))
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }
        public bool WriteLong(long number, string format = "G")
        {
            Span<char> data = stackalloc char[20]; // long.MinValue.ToString().Length == 20

            if (!number.TryFormat(data, out int written, format, CultureInfo.InvariantCulture))
            {
                Error = $"Failed to convert number to string {number} with format {format}";
                return false;
            }

            return WriteLiteralValue(data.Slice(0, written));
        }
        public bool WriteULong(ulong number, string format = "G")
        {
            Span<char> data = stackalloc char[20]; // ulong.MaxValue.ToString().Length == 20

            if (!number.TryFormat(data, out int written, format, CultureInfo.InvariantCulture))
            {
                Error = $"Failed to convert number to string {number} with format {format}";
                return false;
            }

            return WriteLiteralValue(data.Slice(0, written));
        }
        public bool WriteBool(bool value) => WriteLiteralValue(value ? "true" : "false");
        public bool WriteNull() => WriteLiteralValue("null");

        // These are like "extensions"
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(float number, string format = "R") => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(double number, string format = "R") => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(long number, string format = "G") => WriteLong(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(ulong number, string format = "G") => WriteULong(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(ReadOnlySpan<char> value) => WriteString(value);
        public bool Write(string value)
        {
            // For the case of "Write" without any "info", the string overload is called for `null`
            // There will be an explicit check only for this. For anything else, null is treated as `default` or `string.Empty`
            // Because of this, it is suggested not to use `Write` for keys and strings. For anything else, it will work fine.
            if (value is null)
            {
                return WriteNull();
            }

            return WriteString(value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(bool value) => WriteBool(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, float number, string format = "R")
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteNumber(number, format);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, double number, string format = "R")
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteNumber(number, format);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, long number, string format = "G")
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteLong(number, format);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, ulong number, string format = "G")
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteULong(number, format);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, ReadOnlySpan<char> value)
        {
            if (!WriteKey(key))
            {
                return false;
            }

            return WriteString(value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, string value)
        {
            if (!WriteKey(key))
            {
                return false;
            }
            // No need to "worry about key", but still doing the null thing
            if (value is null)
            {
                return WriteNull();
            }

            return WriteString(value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WriteKV(ReadOnlySpan<char> key, bool value)
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteBool(value);
        }

        public ArrayScope Array() => new ArrayScope(this);
        public ArrayScope ArrayKV(ReadOnlySpan<char> key)
        {
            if (!WriteKey(key))
            {
                // No operation is done if key fails.
                return new ArrayScope();
            }

            return new ArrayScope(this);
        }

        public ObjectScope Object() => new ObjectScope(this);
        public ObjectScope ObjectKV(ReadOnlySpan<char> key)
        {
            if (!WriteKey(key))
            {
                // No operation is done if key fails.
                return new ObjectScope();
            }

            return new ObjectScope(this);
        }

        /// <summary>
        /// If data can be read.
        /// </summary>
        /// <remarks>
        /// You should also override this if <see cref="ReadData"/> has an implementation.
        /// <br>Check using the implementation of WriterUnitTests on your target class.</br>
        /// </remarks>
        public virtual bool CanReadData => false;
        /// <summary>
        /// Read the written data from this writer, if applicable.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public virtual string ReadData() => throw new NotSupportedException("Cannot read data from this writer.");
        /// <summary>
        /// Shows a simple preview of the current state.
        /// </summary>
        public override string ToString() => $"[{base.ToString()}] count={count}, error={Error}, top={{{PeekState()}}}";

        public virtual void Reset()
        {
            count = 0;
            depth = 0;
            expect = Expect.None;
            fmtExpect = FormatExpect.None;
            finish = false;
            WrittenComment = false;
            Error = null;
        }

#pragma warning restore IDE0057 // Unity does not support System.Range
    }
}
