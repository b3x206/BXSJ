using BX.SJ.Collections;
using System;
using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Base class for <i>most</i> JSON-compatible writers.
    /// </summary>
    /// <remarks>
    /// * Can write incompatible JSON if not careful. This can be checked through <see cref="S"/> 
    /// or by validating against a strict <see cref="SJReader"/>.
    /// </remarks>
    /// <example>
    /// // Refer to file:///./SJStringWriter.cs
    /// </example>
    public abstract class SJWriter
    {
#pragma warning disable IDE0057 // ***** does not support System.Range

        /// <summary>
        /// Exception thrown on an error case while reading JSON.
        /// </summary>
        public class WriteException : Exception
        {
            public WriteException(SJWriter writer) : base($"{writer.Error} | at char={writer.S.count}")
            { }
            public WriteException(string message, int count) : base($"{message} | at char={count}")
            { }
            public WriteException(string message) : base(message)
            { }
        }
        public struct StateScope : IDisposable
        {
            private bool disposed;
            private readonly bool popIfError;
            private readonly SJWriter writer;

            public StateScope(SJWriter writer, bool popIfError)
            {
                this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

                disposed = false;
                this.popIfError = popIfError;
                writer.PushState();
            }

            public void Dispose()
            {
                if (disposed || writer == null) return;

                if (!popIfError || !string.IsNullOrEmpty(writer.Error))
                {
                    writer.PopState();
                }
                disposed = true;
            }
        }
        [Flags]
        public enum Expect
        {
            // This state isn't quite "stacked" and can work under assumptions
            // (because it's controlled if interacted in the intended way),
            // so we can preserve the negative type literal check here.
            None, Key = 1 << 0, Value = 1 << 1, Comma = 1 << 2, NotType = 1 << 3
        }
        [Flags]
        public enum FormatExpect { None, Newline = 1 << 0, Indent = 1 << 1, NewlineIndent = Newline | Indent }
        // TODO(?): Move other operations to target this enumeration instead.
        /// <summary>
        /// Utility enumeration used to define the target <c>WriteLiteral-</c> method on formatted writers (with numbers)
        /// </summary>
        public enum WriteTarget { Value, Key, Comment, Type }
        /// <summary>
        /// Copyable state contained on a struct.
        /// This is not extensible as it should be opaque to the using code (`object` vs `SJWriter&lt;T&gt;` vs this).
        /// <br>Instead manage your own state structures or values / positions 
        /// using the methods provided within <see cref="SJWriter"/>.</br>
        /// </summary>
        public struct State
        {
            /// <summary>
            /// A tracker for type information that can be stacked and indexed (recursive data)
            /// </summary>
            public struct Stacked
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

                public static readonly Stacked Invalid = new Stacked(SJType.Error) { index = -1 };
                public Stacked(SJType type)
                {
                    this.type = type;
                    index = 0;
                }
                public static implicit operator Stacked(SJType type) => new Stacked(type);

                public override readonly string ToString()
                {
                    if (!Valid)
                    {
                        return $"[{base.ToString()}]";
                    }

                    return $"[{base.ToString()}] type={type}, index={index}";
                }
            }

            [EditorBrowsable(EditorBrowsableState.Never)]
            public string _error;
            public int count;
            public int depth
            {
                readonly get => stack.Count;
                internal set => stack.Count = value;
            }
            public Expect expect;
            public FormatExpect fmtExpect; // Newline is done first, then indent. Is not needed to be stacked

            /// <summary>
            /// Whether if this writer has written a JSON document that is done.
            /// <br>Comments can be written (if <see cref="allowComments"/>) while this 
            /// is <see langword="true"/> as those can be placed anywhere.</br>
            /// </summary>
            public bool ended;

            public bool WrittenComment { get; private set; }
            public bool WrittenType { get; private set; }
            public bool WrittenCustomKey { get; private set; }
            public bool WrittenCustomValue { get; private set; }
            public bool WrittenTrailingComma { get; private set; }

            public ValueStack<Stacked> stack;
            public readonly bool HasStackValue => depth > 0;

            public readonly void CopyTo(ref State s)
            {
                var prevStacked = s.stack;
                s = this;
                s.stack = prevStacked;

                s.stack.Clear(false);
                stack.CopyTo(ref s.stack);
            }

            public void MarkWrittenComment() => WrittenComment = true;
            public void MarkWrittenCustomKey() => WrittenCustomKey = true;
            public void MarkWrittenCustomValue() => WrittenCustomValue = true;
            public void MarkWrittenType() => WrittenType = true;
            public void MarkWrittenTrailingComma() => WrittenTrailingComma = true;
            public void Reset()
            {
                count = 0;
                stack.Clear(false);

                expect = Expect.None;
                fmtExpect = FormatExpect.None;
                ended = false;
                _error = null;
                WrittenComment = WrittenCustomKey = WrittenCustomValue = WrittenType = WrittenTrailingComma = false;
            }

            public override readonly string ToString()
            {
                return $"count={count}, depth={depth} top={{{stack.PeekValue()}}}";
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
        /// Maximum size for <see cref="writeStack"/>
        /// </summary>
        public ushort maxDepth = 128;
        /// <summary>
        /// Maximum state stacking.
        /// </summary>
        public int maxStateStack
        {
            get => _stateStack.maxSize;
            set => _stateStack.maxSize = value;
        }
        /// <summary>
        /// Limit for the built in writing buffer's.
        /// <br>Set to zero to always use <see cref="ArrayPool{T}"/>. Or less to disable limit for <see cref="_formatBuffer"/>.</br>
        /// </summary>
        public int maxFormatBufferSize = 1024;
        /// <summary>
        /// Whether to allow any <see cref="WriteComment"/>.
        /// </summary>
        public bool allowComments = false;
        /// <summary>
        /// Allows writing trailing comma.
        /// <br>This case only happens when <see cref="WriteComment"/> was called
        /// with "hasNextValue" set as true, but there wasn't going to be any next value.</br>
        /// </summary>
        public bool allowTrailingComma = false;
        /// <summary>
        /// Whether to allow any <see cref="State.WriteCustomKey"/>.
        /// </summary>
        public bool allowOtherKeyTypes = false;
        /// <summary>
        /// Allows writing types for key or value. Check <see cref="SJReader.allowTypes"/> for more info.
        /// </summary>
        public bool allowTypes = false;
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
        private State _S;
        /// <summary>
        /// Primary state contained in the writer.
        /// <br>This may not contain the other states that this writer may have.
        /// Use the other state management methods to save/load state.</br>
        /// </summary>
        public ref State S => ref _S;
        /// <summary>
        /// The error string detailing why the writer failed.
        /// </summary>
        public string Error
        {
            get => S._error;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    S._error = string.Empty;
                    return;
                }

                S._error = value;
                if (_ThrowOnError)
                {
                    ThrowError();
                }
            }
        }

        // helper
        // To avoid using ArrayPool, which is maybe slower in case of small heap buffers.
        // Though resizing will allocate garbage and I will add a fallback.
        protected const int LikelyMaxIntFormatSize = 66; // prefix + binary
        protected char[] _formatBuffer = new char[LikelyMaxIntFormatSize];
        /// <summary>
        /// Returns the <see cref="_formatBuffer"/> as <paramref name="chars"/>.
        /// If <see cref="maxFormatBufferSize"/> is exceeded, returns from <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="chars">Either a slice of the <see cref="_formatBuffer"/> or <paramref name="rented"/>.</param>
        /// <param name="rented">Rented array from <see cref="ArrayPool{T}.Shared"/>.
        /// You must return this to the aforementioned pool to not leak memory if it's not <see langword="null"/>.</param>
        /// <param name="size">Size to get the buffer in.</param>
        protected virtual bool GetFormatBuffer(ref Span<char> chars, out char[] rented, int size)
        {
            if (maxFormatBufferSize > 0 && size > maxFormatBufferSize)
            {
                chars = rented = ArrayPool<char>.Shared.Rent(size);
                return true;
            }
            // Previous data on the _formatBuffer is irrelevant.
            if (size > _formatBuffer.Length)
            {
                _formatBuffer = new char[size];
            }

            chars = _formatBuffer.AsSpan(0, size);
            rented = null;
            return true;
        }

        protected abstract void DoAppend(char c);
        /// <summary>
        /// Append to the underlying StringBuilder/Stream-like object.
        /// <br>You can use this to write anything in anywhere, but it isn't recommended.</br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(char c)
        {
            checked { S.count++; }
            DoAppend(c);
        }
        protected virtual void DoAppend(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++) DoAppend(s[i]);
        }
        /// <summary>
        /// <inheritdoc cref="Append(char)"/>
        /// </summary>
        public void Append(ReadOnlySpan<char> s)
        {
            checked { S.count += s.Length; }
            DoAppend(s);
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
        private static readonly Action<SJWriter, char> selfAppend = (writer, c) => writer.Append(c);
        protected void WriteIndent(int depth)
        {
            if (depth <= 0 || indentSize <= 0)
            {
                return;
            }

            Span<char> indent = indentSize <= 32 ? stackalloc char[indentSize] : Span<char>.Empty;
            if (indent.IsEmpty)
            {
                char[] rented = null;
                try
                {
                    GetFormatBuffer(ref indent, out rented, indentSize);
                    indent.Fill(' ');
                    for (int i = 0; i < depth; i++)
                    {
                        Append(indent);
                    }
                }
                finally
                {
                    // Indent array is returned, which could be "used after free".
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);
                }
            }
            else
            {
                indent.Fill(' ');
                for (int i = 0; i < depth; i++)
                {
                    Append(indent);
                }
            }
        }
        protected virtual void AppendEscaped(ReadOnlySpan<char> data, bool asciiOnly)
        {
            SJEscape.Escape(this, selfAppend, data, asciiOnly);
        }
        protected void PrepareFormat()
        {
            ref var s = ref S;
            if (s.fmtExpect == FormatExpect.None) return;
            if (indentSize <= 0)
            {
                // On zero depth, newlines are allowed again for "single line comment" related reasons.
                if (S.depth <= 0 && (s.fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Append('\n'); }
                s.fmtExpect = FormatExpect.None;
                return;
            }

            if ((s.fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Append('\n'); }
            if ((s.fmtExpect & FormatExpect.Indent) == FormatExpect.Indent) { WriteIndent(S.depth); }

            s.fmtExpect = FormatExpect.None;
        }
        protected void PrepareValue()
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return;
            }

            ref var top = ref s.stack.PeekValue();
            if (top.Valid)
            {
                if ((s.expect & Expect.Comma) == Expect.Comma && top.index > 0)
                {
                    Append(',');
                    s.expect &= ~Expect.Comma;
                }

                // keys shouldn't affect the write index as it's a pair
                if ((s.expect & Expect.Key) != Expect.Key)
                {
                    top.index++;
                }
            }

            // Do this regardless of depth now
            PrepareFormat();
        }
        protected void PrepareCommentValue(bool expectValue)
        {
            ref var s = ref S;
            if (expectValue && s.depth > 0)
            {
                if ((s.expect & Expect.Comma) == Expect.Comma)
                {
                    Append(',');

                    s.expect &= ~Expect.Comma;
                }
            }

            PrepareFormat();
        }
        protected void PrepareType()
        {
            ref var s = ref S;
            if (s.depth > 0)
            {
                if ((s.expect & Expect.Comma) == Expect.Comma)
                {
                    Append(',');

                    s.expect &= ~Expect.Comma;
                }
            }

            // Needed regardless as format is "postfix" to the previous value.
            PrepareFormat();
        }
        protected void PrepareEndValue(int prevIndex)
        {
            ref var s = ref S;
            if (indentSize > 0 && prevIndex > 0)
            {
                Append('\n');
                WriteIndent(s.depth);
                s.fmtExpect = FormatExpect.None;
            }
        }
        public const int MaxNumberPrefixLength = 32;
        /// <summary>
        /// Convert supported <paramref name="format"/> types to a prefix used 
        /// to specify the <see cref="SJType.Number"/> values.
        /// </summary>
        /// <param name="format">Numeric format kind.</param>
        /// <returns>A prefix or equalivent of <see cref="ReadOnlySpan{T}.Empty"/></returns>
        public static ReadOnlySpan<char> FormatToPrefix(ReadOnlySpan<char> format)
        {
            if (format == null || format.IsEmpty) return ReadOnlySpan<char>.Empty;
            return char.ToLowerInvariant(format[0]) switch
            {
                'x' => "0x",
                'b' => "0b",
                'o' => "0o", // Octal format for integers does not exist yet, but it may in future.
                'c' => "0c", // To be able to parse currency with the current constraints of SJ, this is needed.
                             // It is suggested to use string for this case instead, but this will be implicitly handled.
                _ => ReadOnlySpan<char>.Empty,
            };
        }
        protected static bool IsUnsupportedIntegerFormat(ReadOnlySpan<char> format)
        {
            if (format == null || format.IsEmpty) return false;
            return char.ToLowerInvariant(format[0]) switch
            {
                'g' => false, // Compact notation
                'n' => false, // Technically unsupported, but CleanupCharacters fixes it.
                              // Could use / support a culture info that uses '_' to seperate?
                'r' => false, // Round trip
                'e' => false, // Supported for some reason
                'd' => false, // Decimal
                _ => true // Will likely postfix the invalid format specifier?
            };
        }
        protected static bool IsInvalidTypeLiteral(ReadOnlySpan<char> literal, out char invalid)
        {
            int checkSt = 0;
            for (int i = 0; i < literal.Length; i++)
            {
                char c = literal[i];
                switch (checkSt)
                {
                    case 0:
                        if (SJReader.IsTypeLiteralContinuation(c) || c == '"') checkSt = 1;
                        else if (!char.IsWhiteSpace(c))
                        {
                            invalid = c;
                            return false;
                        }
                        break;
                    case 1:
                        if (char.IsWhiteSpace(c) || c == '"' && (literal[i - 1] != '\\')) checkSt = 2;
                        else if (!SJReader.IsTypeLiteralContinuation(c))
                        {
                            invalid = c;
                            return false;
                        }
                        break;
                    case 2:
                        if (!char.IsWhiteSpace(c))
                        {
                            invalid = c;
                            return false;
                        }
                        break;

                    default:
                        checkSt = 0;
                        break;
                }
            }

            bool r = checkSt <= 0;
            invalid = r ? ' ' : '\0';
            return r; // empty literals / spaces aren't valid
        }
        protected static int CleanupCharacters(ref Span<char> s, ReadOnlySpan<char> cs)
        {
            // Currency format or other "number seperation" on invariant culture is like ¤1,234.00,
            // so should remove commas from the format as it's a json token and won't be able to read fully.
            static bool Contains(ReadOnlySpan<char> c, char v)
            {
                for (int i = 0; i < c.Length; i++)
                {
                    if (c[i] == v)
                    {
                        return true;
                    }
                }
                return false;
            }

            int marked = 0;
            const char ReplaceMarker = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                if (Contains(cs, s[i]))
                {
                    s[i] = ReplaceMarker;
                    marked++;
                }
            }
            if (marked <= 0) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != ReplaceMarker) s[w++] = s[i];
            }
            s = s.Slice(0, w);

            return marked;
        }
        public delegate bool TryFormatHandler<in T>(T number, Span<char> dest, ReadOnlySpan<char> format, out int charsWritten);
        protected bool WriteFormattedNumber<T>(
            WriteTarget target, T number, ReadOnlySpan<char> format, ReadOnlySpan<char> prefix, TryFormatHandler<T> tryFormat,
            bool checkEnded = true
        )
        {
            ref var s = ref S;
            if (!string.IsNullOrEmpty(Error)) return false;
            if (checkEnded && s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }

            switch (target)
            {
                case WriteTarget.Value:
                    if (IsUnsupportedIntegerFormat(format)) s.MarkWrittenCustomValue();
                    break;
                case WriteTarget.Key:
                    s.MarkWrittenCustomKey();
                    break;

                default:
                    break;
            }
            if (prefix == null) prefix = FormatToPrefix(format);
            int written = 0, offset = Math.Min(prefix.Length, MaxNumberPrefixLength);
            Span<char> data = stackalloc char[20 + offset], view = data.Slice(offset);
            char[] rented = null;

            try
            {
                while (!tryFormat(number, view, format, out written))
                {
                    if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);
                    GetFormatBuffer(ref data, out rented, Math.Min(data.Length * 2, LikelyMaxIntFormatSize + offset));

                    view = data.Slice(offset);
                }

                if (offset > 0) prefix.Slice(0, offset).CopyTo(data);
                data = data.Slice(0, written + offset);
                // Do this only for currency / other formats as it iterates the number string unnecessarily?
                // Though it could also be necessary for prefix and stability (over speed) is better for now.
                CleanupCharacters(ref data, ", ");
                return target switch
                {
                    WriteTarget.Key => WriteLiteralKey(data, stringify: false),
                    WriteTarget.Comment => WriteComment(data),
                    WriteTarget.Type => WriteType(data, stringify: false),
                    _ => WriteLiteralValue(data),
                };
            }
            finally
            {
                if (!(rented is null)) ArrayPool<char>.Shared.Return(rented);
            }
        }

        public bool BeginObject()
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if (maxDepth > 0 && S.depth >= maxDepth)
            {
                Error = $"Exceeded max write depth '{maxDepth}'";
                return false;
            }

            PrepareValue();
            Append('{');

            s.stack.Push(SJType.Object);

            s.expect = Expect.Key;
            s.fmtExpect = FormatExpect.NewlineIndent;

            return true;
        }
        public bool EndObject()
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            ref var top = ref s.stack.PeekValue();
            if (!top.Valid || top.type != SJType.Object)
            {
                Error = $"Invalid EndObject for toplevel with type '{top.type}', expected Object";
                return false;
            }
            if ((s.expect & Expect.Value) == Expect.Value)
            {
                Error = "Expected writing object value";
                return false;
            }
            if ((s.expect & Expect.NotType) == Expect.NotType)
            {
                Error = "Expected a Key or Value after type literal";
                return false;
            }
            if ((s.expect & Expect.Key) != Expect.Key && (s.expect & Expect.Comma) != Expect.Comma)
            {
                if (!allowTrailingComma)
                {
                    Error = "Trailing comma written - the comment functions may cause this, if no end of content is specified.";
                    return false;
                }
                else
                {
                    s.MarkWrittenTrailingComma();
                }
            }

            // Can end object
            s.expect = Expect.None;

            int prevIndex = top.index;
            s.stack.PopValue();
            top = ref s.stack.PeekValue();

            if (s.depth <= 0)
            {
                s.ended = true;
                s.expect |= Expect.NotType;
            }
            else
            {
                switch (top.type)
                {
                    case SJType.Array:
                        break;
                    case SJType.Object:
                        s.expect = Expect.Key;
                        break;

                    default:
                        Error = $"Invalid top state type {top.type}";
                        return false;
                }
                if (top.index > 0) s.expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            s.fmtExpect = FormatExpect.NewlineIndent;

            Append('}');


            return true;
        }
        public bool BeginArray()
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if (maxDepth > 0 && s.depth >= maxDepth)
            {
                Error = $"Exceeded max write depth '{maxDepth}'";
                return false;
            }

            PrepareValue();
            Append('[');

            s.expect = Expect.None;
            s.fmtExpect = FormatExpect.NewlineIndent;

            s.stack.Push(SJType.Array);
            return true;
        }
        public bool EndArray()
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key";
                return false;
            }
            if ((s.expect & Expect.NotType) == Expect.NotType)
            {
                Error = "Expected a Value after type literal";
                return false;
            }
            if ((s.expect & Expect.Comma) != Expect.Comma)
            {
                if (!allowTrailingComma)
                {
                    Error = "Trailing comma written - the comment functions may cause this, if no end of content is specified.";
                    return false;
                }
                else
                {
                    s.MarkWrittenTrailingComma();
                }
            }
            ref var top = ref S.stack.PeekValue();
            if (!top.Valid || top.type != SJType.Array)
            {
                Error = $"Invalid EndObject for toplevel with type '{top.type}', expected Array";
                return false;
            }

            s.expect = Expect.None;

            int prevIndex = top.index;
            s.stack.PopValue();
            top = ref s.stack.PeekValue();

            if (s.depth <= 0)
            {
                s.ended = true;
                s.expect |= Expect.NotType;
            }
            else
            {
                switch (top.type)
                {
                    case SJType.Array:
                        break;
                    case SJType.Object:
                        s.expect = Expect.Key;
                        break;

                    default:
                        Error = $"Invalid top state type {top.type}";
                        return false;
                }
                if (top.index > 0) s.expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            s.fmtExpect = FormatExpect.NewlineIndent;

            Append(']');

            return true;
        }

        /// <summary>
        /// Write a key, while <see cref="s.expect"/> has <see cref="Expect.Key"/>.
        /// <b>This method will output the <paramref name="value"/> literally if <paramref name="stringify"/> is false.</b>
        /// </summary>
        /// <remarks>
        /// No duplicates are checked.
        /// </remarks>
        /// <param name="value">Name of the key entry.</param>
        /// <returns>Whether if the write was successful.</returns>
        public virtual bool WriteLiteralKey(ReadOnlySpan<char> value, bool stringify = false)
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.Key) != Expect.Key)
            {
                Error = "Did not s.expect an object key";
                return false;
            }
            if (!stringify)
            {
                if (!allowOtherKeyTypes)
                {
                    Error = "Cannot write other key types outside of string, enable 'allowOtherKeyTypes'.";
                    return false;
                }

                s.MarkWrittenCustomKey();
            }

            PrepareValue();
            if (stringify) { Append('"'); }
            if (stringify) { AppendEscaped(value, asciiOnly); }
            else { Append(value); }
            if (stringify) { Append('"'); }
            Append(':');

            if (indentSize > 0)
            {
                Append(' ');

            }

            s.expect = Expect.Value;
            s.fmtExpect = FormatExpect.None;

            return true;
        }
        /// <summary>
        /// Write a key, while <see cref="s.expect"/> has <see cref="Expect.Key"/>.
        /// </summary>
        /// <remarks>
        /// Base implementation <b>does not check duplicate keys for the time being within the depth..</b>
        /// </remarks>
        /// <param name="name">Name of the key entry.</param>
        /// <returns>Whether if the write was successful.</returns>
        public bool WriteKey(ReadOnlySpan<char> name) => WriteLiteralKey(name, stringify: true);
        public bool WriteKeyNumber(float number, string format = "G")
        {
            static bool TryFormat(float n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            // Leaving prefix empty string will disable prefixes
            return WriteFormattedNumber(WriteTarget.Key, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public bool WriteKeyNumber(double number, string format = "G")
        {
            static bool TryFormat(double n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public bool WriteKeyLong(long number, string format = "G", string prefix = null)
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        public bool WriteKeyULong(ulong number, string format = "G", string prefix = null)
        {
            static bool TryFormat(ulong n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        public bool WriteKeyBool(bool value) => WriteLiteralKey(value ? "true" : "false", stringify: false);
        public bool WriteKeyNull() => WriteLiteralKey("null", stringify: false);

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
        public virtual bool WriteLiteralValue(ReadOnlySpan<char> data, bool stringify = false)
        {
            ref var s = ref S;
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.Key) == Expect.Key)
            {
                Error = "Expected writing object key, got WriteLiteral";
                return false;
            }

            PrepareValue();
            if (stringify)
            {
                Append('"');
                AppendEscaped(data, asciiOnly);
                Append('"');
            }
            else
            {
                Append(data);
            }

            if (s.depth <= 0)
            {
                // Value is top level
                s.ended = true;
                s.expect |= Expect.NotType;
            }
            else
            {
                var t = s.stack.PeekValue().type;
                switch (t)
                {
                    case SJType.Object:
                        s.expect = Expect.Key | Expect.Comma;
                        break;
                    case SJType.Array:
                        s.expect = Expect.Value | Expect.Comma;
                        break;
                    default:
                        Error = $"Invalid top level type {t}";
                        return false;
                }
                // Key or value which can be typed both
                s.expect &= ~Expect.NotType;
            }
            s.fmtExpect = FormatExpect.NewlineIndent;

            return true;
        }
        public virtual bool WriteString(ReadOnlySpan<char> value) => WriteLiteralValue(value, stringify: true);
        public bool WriteNumber(float number, string format = "G")
        {
            static bool TryFormat(float n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public bool WriteNumber(double number, string format = "G")
        {
            static bool TryFormat(double n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public bool WriteLong(long number, string format = "G", string prefix = null)
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public bool WriteULong(ulong number, string format = "G", string prefix = null)
        {
            static bool TryFormat(ulong n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public bool WriteBool(bool value) => WriteLiteralValue(value ? "true" : "false");
        public bool WriteNull() => WriteLiteralValue("null");
        /// <summary>
        /// Write a type prefix.
        /// </summary>
        /// <param name="type">Type literal or id. Should be a valid character matching <see cref="SJReader.IsTypeLiteralContinuation(char)"/>.</param>
        /// <param name="stringify">Write it as a string value (with '"' quotes) instead of a literal.</param>
        public virtual bool WriteType(ReadOnlySpan<char> type, bool stringify = false)
        {
            ref var s = ref S;
            if (!allowTypes)
            {
                Error = "Cannot write types on this writer, enable 'allowTypes'";
                return false;
            }
            if (s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return false;
            }
            if ((s.expect & Expect.NotType) == Expect.NotType)
            {
                Error = "Not expected type literal to be written";
                return false;
            }

            s.MarkWrittenType();
            PrepareType();
            Append('(');
            if (stringify)
            {
                Append('"');
                AppendEscaped(type, asciiOnly);
                Append('"');
            }
            else
            {
                // Validating ts string for some reason. Whatever
                if (IsInvalidTypeLiteral(type, out char invalid))
                {
                    Error = $"Invalid type literal 'char={invalid}', use stringify if invalid type charaters are needed.";
                    return false;
                }

                Append(type);
            }
            Append(')');
            s.expect |= Expect.NotType;

            return true;
        }
        public bool WriteType(long typeCode, string format = "G")
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Type, typeCode, format, null, TryFormat);
        }

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
            ref var s = ref S;
            if (!allowComments)
            {
                Error = "Cannot write comments on this writer, enable 'allowComments'";
                return false;
            }

            s.MarkWrittenComment();
            PrepareCommentValue(hasNextValue);

            Append("/*");
            for (int i = -1; i <= data.Length; i++)
            {
                if (i < 0 || i >= data.Length)
                {
                    if (pad != 0 && pad != '\r' && pad != '\n') { Append(pad); }
                    continue;
                }

                char c = data[i];
                if (c == '*' && (i < (data.Length - 1) ? data[i + 1] : '\0') == '/')
                {
                    // Very fun escape action (Write as '*\/' instead of '*/' which ends comment)
                    Append(c);
                    Append('\\');
                    continue;
                }

                Append(c);
                if (c == '\n')
                {
                    WriteIndent(s.depth);
                }
            }
            Append("*/");

            s.fmtExpect = FormatExpect.None;
            if (indentSize > 0)
            {
                s.fmtExpect = FormatExpect.NewlineIndent;
                if (newlineImmediately)
                {
                    Append('\n');
                    s.fmtExpect &= ~FormatExpect.Newline;
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
            ref var s = ref S;
            if (!allowComments)
            {
                Error = "Cannot write comments on this writer, enable 'allowComments'";
                return false;
            }
            if (s.depth > 0 && indentSize <= 0)
            {
                return WriteComment(data, prefixPad, hasNextValue);
            }

            s.MarkWrittenComment();
            PrepareCommentValue(hasNextValue);

            Append("//");
            for (int i = -1; i < data.Length; i++)
            {
                if (i < 0)
                {
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Append(prefixPad); }
                    continue;
                }

                char c = data[i];
                Append(c);
                if (c == '\n')
                {
                    WriteIndent(s.depth);
                    Append("//");
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Append(prefixPad); }
                }
            }

            s.fmtExpect = FormatExpect.NewlineIndent;
            // something something FormatExpect is done after Expect tokens and this is whitespace sensitive
            if (s.depth > 0 && (s.expect & Expect.Comma) == Expect.Comma)
            {
                Append('\n');
                s.fmtExpect &= ~FormatExpect.Newline;
            }

            return true;
        }

        // These are like "extensions"
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(float number, string format = "R") => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(double number, string format = "R") => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(long number, string format = "G", string prefix = null) => WriteLong(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Write(ulong number, string format = "G", string prefix = null) => WriteULong(number, format, prefix);
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

        // Currently only "string" keys are allowed on extensions.
        // Could do a fluent like thing, but do want to return the result as well..
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
        public bool WriteKV(ReadOnlySpan<char> key, bool value)
        {
            if (!WriteKey(key))
            {
                return false;
            }
            return WriteBool(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        public override string ToString() => $"[{base.ToString()}] state=({S}) error={Error}";

        // Code execution only - can be readonly
        protected ValueStack<State> _stateStack = ValueStack<State>.CreateWithMaxSize(128);
        public int StateCount => _stateStack.Count;
        /// <summary>
        /// Pushes a loadable stacked state.
        /// </summary>
        /// <returns>
        /// The index of the pushed state.
        /// For any other thing with it's own state, this can be used to write it into it's own index like:
        /// <br><c>result := <see cref="PushState"/>; myStack[result] = otherState;</c></br>
        /// </returns>
        public virtual int PushState()
        {
            var handle = _stateStack.Push(false);
            if (handle.IsStub) return -1;
            ref var next = ref handle.Value;

            // Copy the data to the entry in stateStack
            S.CopyTo(ref next);
            return _stateStack.Count - 1;
        }
        /// <summary>
        /// Loads a stacked state.
        /// </summary>
        /// <returns>
        /// The index of the loaded state.
        /// For any other thing with it's own state, this can be used to read from it's own index like:
        /// <br><c>result := <see cref="PopState"/>; otherState = myStack[result];</c></br>
        /// </returns>
        public virtual int PopState()
        {
            if (_stateStack.Count <= 0) return -1;
            _stateStack.PopValue().CopyTo(ref S);
            return _stateStack.Count;
        }
        /// <summary>
        /// Starts a <see cref="SJWriter"/> transaction, which will reset if this state is disposed.
        /// </summary>
        public StateScope UseState(bool popIfError = false) => new StateScope(this, popIfError);

        /// <summary>
        /// Resets the writer to the default state. This also removes any stacked state if present.
        /// </summary>
        /// <remarks>
        /// <br>Unlike the <see cref="SJReader"/>, this isn't required 
        /// to commit any data and can remove present data. Use a Flush method if
        /// data isn't commited as it's being written (ex: Stream sources)
        /// and close the writer instead or use alternative Reset methods.</br>
        /// </remarks>
        public virtual void Reset()
        {
            S.Reset();
            _stateStack.Clear(false);
        }
#pragma warning restore IDE0057 // ***** does not support System.Range
    }
}
