#pragma warning disable IDE0057 // ***** does not support System.Range
using BX.SJ.Collections;
using System;
using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Base class for <i>most</i> Json-compatible writers.
    /// </summary>
    /// <remarks>
    /// * Can write incompatible Json if not careful. This can be checked through <see cref="S"/> 
    /// or by validating against a strict <see cref="SJReader"/>.
    /// </remarks>
    public sealed class SJWriter : ISJStateStackable, IDisposable
    {
        /// <summary>
        /// Exception thrown on an error case while reading JSON.
        /// </summary>
        public class WriteException : Exception
        {
            public WriteException(SJWriter writer) : base($"{writer.Error} | at char={writer.Data.Count}")
            { }
            public WriteException(string message, int count) : base($"{message} | at char={count}")
            { }
            public WriteException(string message) : base(message)
            { }
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

            public SJValueStack<Stacked> stack;
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
                stack.Clear(false);

                expect = Expect.None;
                fmtExpect = FormatExpect.None;
                ended = false;
                _error = null;
                WrittenComment = WrittenCustomKey = WrittenCustomValue = WrittenType = WrittenTrailingComma = false;
            }

            public override readonly string ToString()
            {
                return $"depth={depth} top={{{stack.PeekValue()}}}";
            }
        }

        public readonly struct ObjectScope : IDisposable
        {
            private readonly SJWriter writer;
            private readonly bool success;

            public ObjectScope(SJWriter writer)
            {
                this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
                writer.BeginObject();
                success = !writer.HasError;
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
                writer.BeginArray();
                success = !writer.HasError;
            }

            public void Dispose()
            {
                if (success)
                {
                    writer.EndArray();
                }
            }
        }

        public const int DefaultMaxDepth = 128;
        public const string DefaultNumberFormat = "G";

        // settings
        /// <summary>
        /// The indent size to use. If this is 0 or less, no pretty printing is done.
        /// </summary>
        public int indentSize = 0;
        /// <summary>
        /// Maximum size for <see cref="writeStack"/>
        /// </summary>
        public ushort maxDepth = DefaultMaxDepth;
        /// <summary>
        /// Maximum amount of stackable state. If set to zero or less, it can be stacked forever.
        /// </summary>
        public int MaxStateCount
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
        private bool _ThrowOnError = false;
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
        private bool disposedValue = false;
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
        public bool HasError => !string.IsNullOrEmpty(Error);

        // helper
        // To avoid using ArrayPool, which is maybe slower in case of small heap buffers.
        // Though resizing will allocate garbage and I will add a fallback.
        private const int LikelyMaxIntFormatSize = 66; // prefix + binary
        private char[] _formatBuffer = new char[LikelyMaxIntFormatSize];
        /// <summary>
        /// Returns the <see cref="_formatBuffer"/> as <paramref name="chars"/>.
        /// If <see cref="maxFormatBufferSize"/> is exceeded, returns from <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="chars">Either a slice of the <see cref="_formatBuffer"/> or <paramref name="rented"/>.</param>
        /// <param name="rented">Rented array from <see cref="ArrayPool{T}.Shared"/>.
        /// You must return this to the aforementioned pool to not leak memory if it's not <see langword="null"/>.</param>
        /// <param name="size">Size to get the buffer in.</param>
        private bool GetFormatBuffer(ref Span<char> chars, out char[] rented, int size)
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

        private ISJDataWriter _Data;
        private ISJStateStackable _StData;
        public int LastDataVersion { get; private set; }
        public ISJDataWriter Data
        {
            get => _Data;
            set
            {
                if (ReferenceEquals(value, _Data)) return;

                _Data = value ?? throw new ArgumentNullException(nameof(value));
                _StData = _Data as ISJStateStackable;

                Reset();
            }
        }

        public SJWriter(ISJDataWriter writer)
        {
            Data = writer;
        }

        /// <summary>
        /// Throws the currently stored error. It does not check whether if an error is available.
        /// </summary>
        /// <exception cref="WriteException"></exception>
        public void ThrowError()
        {
            throw new WriteException(this);
        }
        private void CheckDisposed()
        {
            if (disposedValue) throw new InvalidOperationException("Attempt to write to a disposed SJWriter.");
        }
        private bool CheckState()
        {
            CheckDisposed();
            CheckVersion();

            return !HasError;
        }
        private bool CheckWritableState(bool checkEnded = true, bool checkDepth = true, bool emitError = true)
        {
            if (!CheckState()) return false;
            ref var s = ref S;
            if (checkEnded && s.ended)
            {
                if (emitError) Error = "Attempt to write a value to a ended state writer";
                return false;
            }
            if (checkDepth && (maxDepth > 0 && s.depth >= maxDepth))
            {
                if (emitError) Error = $"Exceeded max depth {maxDepth}";
                return false;
            }

            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SJWriter Self() => this;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SJWriter Self(string error)
        {
            Error = error;
            return Self();
        }
        public void CheckVersion()
        {
            if (Data != null && Data.Version != LastDataVersion) Reset();
        }

        // The base writers
        private static readonly Action<SJWriter, char> selfAppend = (writer, c) => writer.Data.Write(c);
        private void WriteIndent(int depth)
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
                        Data.Write(indent);
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
                    Data.Write(indent);
                }
            }
        }
        private void AppendEscaped(ReadOnlySpan<char> data, bool asciiOnly)
        {
            SJEscape.Escape(this, selfAppend, data, asciiOnly);
        }
        private void PrepareFormat()
        {
            ref var s = ref S;
            if (s.fmtExpect == FormatExpect.None) return;
            if (indentSize <= 0)
            {
                // On zero depth, newlines are allowed again for "single line comment" related reasons.
                if (S.depth <= 0 && (s.fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Data.Write('\n'); }
                s.fmtExpect = FormatExpect.None;
                return;
            }

            if ((s.fmtExpect & FormatExpect.Newline) == FormatExpect.Newline) { Data.Write('\n'); }
            if ((s.fmtExpect & FormatExpect.Indent) == FormatExpect.Indent) { WriteIndent(S.depth); }

            s.fmtExpect = FormatExpect.None;
        }
        private void PrepareValue()
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
                    Data.Write(',');
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
        private void PrepareCommentValue(bool expectValue)
        {
            ref var s = ref S;
            if (expectValue && s.depth > 0)
            {
                if ((s.expect & Expect.Comma) == Expect.Comma)
                {
                    Data.Write(',');

                    s.expect &= ~Expect.Comma;
                }
            }

            PrepareFormat();
        }
        private void PrepareType()
        {
            ref var s = ref S;
            if (s.depth > 0)
            {
                if ((s.expect & Expect.Comma) == Expect.Comma)
                {
                    Data.Write(',');

                    s.expect &= ~Expect.Comma;
                }
            }

            // Needed regardless as format is "postfix" to the previous value.
            PrepareFormat();
        }
        private void PrepareEndValue(int prevIndex)
        {
            ref var s = ref S;
            if (indentSize > 0 && prevIndex > 0)
            {
                Data.Write('\n');
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
        private static bool IsUnsupportedIntegerFormat(ReadOnlySpan<char> format)
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
        private static bool IsInvalidTypeLiteral(ReadOnlySpan<char> literal, out char invalid)
        {
            int checkSt = 0;
            for (int i = 0; i < literal.Length; i++)
            {
                char c = literal[i];
                switch (checkSt)
                {
                    case 0: // before string
                        if (SJReader.IsTypeLiteralContinuation(c) || c == '"') checkSt = c == '"' ? 2 : 1;
                        else if (!char.IsWhiteSpace(c))
                        {
                            invalid = c;
                            return false;
                        }
                        break;
                    case 1: // inside literal
                        if (char.IsWhiteSpace(c)) checkSt = 3;
                        else if (!SJReader.IsTypeLiteralContinuation(c))
                        {
                            invalid = c;
                            return false;
                        }
                        break;
                    // why check string? string is already checked in SJEscape
                    case 2: // inside string
                        if (c == '"' && (literal[i - 1] != '\\')) checkSt = 3;
                        break;
                    case 3: // outside string
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

            // empty literals / spaces aren't valid
            bool r = literal.Length > 0 && checkSt <= 0;
            invalid = r ? '\0' : ' ';
            return r;
        }
        private static int CleanupCharacters(ref Span<char> s, ReadOnlySpan<char> cs)
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
        private SJWriter WriteFormattedNumber<T>(
            WriteTarget target, T number, ReadOnlySpan<char> format, ReadOnlySpan<char> prefix, TryFormatHandler<T> tryFormat,
            bool checkEnded = true
        )
        {
            if (!CheckState()) return Self();
            ref var s = ref S;
            if (checkEnded && s.ended)
            {
                Error = "Attempt to write into a finished JSONWriter";
                return Self();
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

        public SJWriter BeginObject()
        {
            ref var s = ref S;
            if (!CheckWritableState()) return Self();
            if ((s.expect & Expect.Key) == Expect.Key) return Self("Expected writing object key");

            PrepareValue();
            Data.Write('{');

            s.stack.Push(SJType.Object);

            s.expect = Expect.Key;
            s.fmtExpect = FormatExpect.NewlineIndent;

            return Self();
        }
        public SJWriter EndObject()
        {
            ref var s = ref S;
            if (!CheckWritableState())                         /**/ return Self();
            if ((s.expect & Expect.Value) == Expect.Value)     /**/ return Self("Expected writing object value");
            if ((s.expect & Expect.NotType) == Expect.NotType) /**/ return Self("Expected a Key or Value after type literal");
            if ((s.expect & Expect.Key) != Expect.Key && (s.expect & Expect.Comma) != Expect.Comma)
            {
                if (!allowTrailingComma) return Self("Trailing comma written. The comment writer may cause this, if end of content is specified wrongfully.");
                else                /**/ s.MarkWrittenTrailingComma();
            }
            if (s.stack.PeekValue().type != SJType.Object)     /**/ return Self($"Expected top level to have Object type, got '{s.stack.PeekValue().type}'");

            // Can end object
            s.expect = Expect.None;

            int prevIndex = s.stack.PopValue().index;
            ref var top = ref s.stack.PeekValue();

            if (s.depth <= 0)
            {
                Console.WriteLine("Object Set ended as depth is 0");
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
                        return Self($"Invalid top state type {top.type}");
                }
                if (top.index > 0) s.expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            s.fmtExpect = FormatExpect.NewlineIndent;

            Data.Write('}');

            return Self();
        }
        public SJWriter BeginArray()
        {
            ref var s = ref S;
            if (!CheckWritableState()) return Self();
            if ((s.expect & Expect.Key) == Expect.Key) return Self("Expected writing object key");

            PrepareValue();
            Data.Write('[');

            s.expect = Expect.None;
            s.fmtExpect = FormatExpect.NewlineIndent;

            s.stack.Push(SJType.Array);
            return Self();
        }
        public SJWriter EndArray()
        {
            ref var s = ref S;
            if (!CheckWritableState())                         /**/ return Self();
            if ((s.expect & Expect.Key) == Expect.Key)         /**/ return Self("Expected writing object key");
            if ((s.expect & Expect.NotType) == Expect.NotType) /**/ return Self("Expected a Value after type literal");
            if ((s.expect & Expect.Comma) != Expect.Comma)
            {
                if (!allowTrailingComma) return Self("Trailing comma written. The comment writer may cause this, if end of content is specified wrongfully.");
                else                /**/ s.MarkWrittenTrailingComma();
            }
            if (s.stack.PeekValue().type != SJType.Array)     /**/ return Self($"Expected top level to have Array type, got '{s.stack.PeekValue().type}'");

            s.expect = Expect.None;

            int prevIndex = s.stack.PopValue().index;
            ref var top = ref s.stack.PeekValue();

            if (s.depth <= 0)
            {
                Console.WriteLine("Array Set ended as depth is 0");
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
                        return Self($"Invalid top state type {top.type}");
                }
                if (top.index > 0) s.expect |= Expect.Comma;
            }

            PrepareEndValue(prevIndex);
            s.fmtExpect = FormatExpect.NewlineIndent;

            Data.Write(']');

            return Self();
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
        public SJWriter WriteLiteralKey(ReadOnlySpan<char> value, bool stringify = false)
        {
            ref var s = ref S;
            if (!CheckWritableState())                 /**/ return Self();
            if ((s.expect & Expect.Key) != Expect.Key) /**/ return Self("Did not expect an object key");
            if (!stringify)
            {
                if (!allowOtherKeyTypes) return Self("Cannot write other key types outside of string, enable 'allowOtherKeyTypes'.");
                s.MarkWrittenCustomKey();
            }

            PrepareValue();
            if (stringify) /**/ { Data.Write('"'); }
            if (stringify) /**/ { AppendEscaped(value, asciiOnly); }
            else           /**/ { Data.Write(value); }
            if (stringify) /**/ { Data.Write('"'); }
            Data.Write(':');

            if (indentSize > 0)
            {
                Data.Write(' ');

            }

            s.expect = Expect.Value;
            s.fmtExpect = FormatExpect.None;

            return Self();
        }
        /// <summary>
        /// Write a key, while <see cref="s.expect"/> has <see cref="Expect.Key"/>.
        /// </summary>
        /// <remarks>
        /// Current implementation <b>does not check duplicate keys for the time being within the depth..</b>
        /// </remarks>
        /// <param name="name">Name of the key entry.</param>
        /// <returns>Whether if the write was successful.</returns>
        public SJWriter WriteKeyString(ReadOnlySpan<char> name) => WriteLiteralKey(name, stringify: true);
        /// <summary>
        /// Write custom number key. This is only allowed when <see cref="allowOtherKeyTypes"/> is set to <see langword="true"/>.
        /// </summary>
        public SJWriter WriteKeyNumber(float number, string format = DefaultNumberFormat)
        {
            static bool TryFormat(float n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            // Leaving prefix empty string will disable prefixes
            return WriteFormattedNumber(WriteTarget.Key, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyNumber(double number, string format = DefaultNumberFormat)
        {
            static bool TryFormat(double n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyInt(int number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(int n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyUInt(uint number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(uint n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyLong(long number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyULong(ulong number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(ulong n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Key, number, format, prefix, TryFormat);
        }
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyBool(bool value) => WriteLiteralKey(value ? SJReader.TrueLiteral : SJReader.FalseLiteral, stringify: false);
        /// <inheritdoc cref="WriteKeyNumber(float, string)"/>
        public SJWriter WriteKeyNull() => WriteLiteralKey(SJReader.NullLiteral, stringify: false);

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
        public SJWriter WriteLiteralValue(ReadOnlySpan<char> data, bool stringify = false)
        {
            ref var s = ref S;
            if (!CheckWritableState())                 /**/ return Self();
            if ((s.expect & Expect.Key) == Expect.Key) /**/ return Self("Expected writing object key, got WriteLiteral");

            PrepareValue();
            if (stringify)
            {
                Data.Write('"');
                AppendEscaped(data, asciiOnly);
                Data.Write('"');
            }
            else
            {
                Data.Write(data);
            }

            if (s.depth <= 0)
            {
                // Value is top level
                s.ended = true;
                Console.WriteLine("Set ended as literal is top level");
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
                        return Self($"Invalid top level type {t}");
                }
                // Key or value which can be typed both
                s.expect &= ~Expect.NotType;
            }
            s.fmtExpect = FormatExpect.NewlineIndent;

            return Self();
        }
        public SJWriter WriteString(ReadOnlySpan<char> value) => WriteLiteralValue(value, stringify: true);
        public SJWriter WriteNumber(float number, string format = DefaultNumberFormat)
        {
            static bool TryFormat(float n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public SJWriter WriteNumber(double number, string format = DefaultNumberFormat)
        {
            static bool TryFormat(double n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, ReadOnlySpan<char>.Empty, TryFormat);
        }
        public SJWriter WriteInt(int number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(int n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public SJWriter WriteUInt(uint number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(uint n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public SJWriter WriteLong(long number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public SJWriter WriteULong(ulong number, string format = DefaultNumberFormat, string prefix = null)
        {
            static bool TryFormat(ulong n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Value, number, format, prefix, TryFormat);
        }
        public SJWriter WriteBool(bool value) => WriteLiteralValue(value ? SJReader.TrueLiteral : SJReader.FalseLiteral);
        public SJWriter WriteNull() => WriteLiteralValue(SJReader.NullLiteral);

        /// <summary>
        /// Write a type prefix. This is only allowed if <see cref="allowTypes"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="type">Type literal or id. Should be a valid character matching <see cref="SJReader.IsTypeLiteralContinuation(char)"/>.</param>
        /// <param name="stringify">Write it as a string value (with '"' quotes) instead of a literal.</param>
        public SJWriter WriteType(ReadOnlySpan<char> type, bool stringify = false)
        {
            ref var s = ref S;
            if (!CheckWritableState()) return Self();
            if (!allowTypes) return Self("Cannot write types on this writer, enable 'allowTypes'");
            if ((s.expect & Expect.NotType) == Expect.NotType) return Self("Not expected type literal to be written");

            s.MarkWrittenType();
            PrepareType();
            Data.Write('(');
            if (stringify)
            {
                Data.Write('"');
                AppendEscaped(type, asciiOnly);
                Data.Write('"');
            }
            else
            {
                // Validating ts string for some reason. Whatever
                if (IsInvalidTypeLiteral(type, out char invalid))
                {
                    return Self($"Invalid type literal 'char={invalid}', use stringify if invalid type charaters are needed.");
                }

                Data.Write(type);
            }
            Data.Write(')');
            s.expect |= Expect.NotType;

            return Self();
        }
        /// <summary>
        /// <inheritdoc cref="WriteType(ReadOnlySpan{char}, bool)"/>
        /// </summary>
        public SJWriter WriteType(int typeCode, string format = DefaultNumberFormat)
        {
            static bool TryFormat(int n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Type, typeCode, format, null, TryFormat);
        }
        /// <summary>
        /// <inheritdoc cref="WriteType(ReadOnlySpan{char}, bool)"/>
        /// </summary>
        public SJWriter WriteType(long typeCode, string format = DefaultNumberFormat)
        {
            static bool TryFormat(long n, Span<char> dest, ReadOnlySpan<char> format, out int written) =>
                n.TryFormat(dest, out written, format, CultureInfo.InvariantCulture);
            return WriteFormattedNumber(WriteTarget.Type, typeCode, format, null, TryFormat);
        }

        /// <summary>
        /// Write a comment for JSON, starting with /* and ending with */. This is only allowed if <see cref="allowComments"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="data">Data inside the comment to write. It is padded with spaces by default.</param>
        /// <param name="pad">Padding left and right character to use. Pass 0 to not pad the <paramref name="data"/> with anything. Invalid characters (like '\n') also avoids padding.</param>
        /// <param name="hasNextValue">
        /// <br>To print "prettier", add a trailing comma or colon to the previous value. This will look nicer like</br>
        /// <c><br>"value": /* ... */\n</br></c>
        /// <br>instead of the usual but always valid <c>"value" /* ... */\n:</c></br>
        /// <br><b>Set this <see langword="true"/> with caution, as you can produce invalid results (trailing commas) with it if there is no next value.</b></br>
        /// </param>
        /// <param name="newlineImmediately">Append a \n character after writing multiline comment. Only works with pretty printing.</param>
        /// <returns>Whether if the write was successful.</returns>
        public SJWriter WriteComment(ReadOnlySpan<char> data, char pad = ' ', bool hasNextValue = false, bool newlineImmediately = false)
        {
            ref var s = ref S;
            if (!CheckState()) return Self();
            if (!allowComments) return Self("Cannot write comments on this writer, enable 'allowComments'");

            s.MarkWrittenComment();
            PrepareCommentValue(hasNextValue);

            Data.Write("/*");
            for (int i = -1; i <= data.Length; i++)
            {
                if (i < 0 || i >= data.Length)
                {
                    if (pad != 0 && pad != '\r' && pad != '\n') { Data.Write(pad); }
                    continue;
                }

                char c = data[i];
                if (c == '*' && (i < (data.Length - 1) ? data[i + 1] : '\0') == '/')
                {
                    // Very fun escape action (Write as '*\/' instead of '*/' which ends comment)
                    Data.Write(c);
                    Data.Write('\\');
                    continue;
                }

                Data.Write(c);
                if (c == '\n')
                {
                    WriteIndent(s.depth);
                }
            }
            Data.Write("*/");

            s.fmtExpect = FormatExpect.None;
            if (indentSize > 0)
            {
                s.fmtExpect = FormatExpect.NewlineIndent;
                if (newlineImmediately)
                {
                    Data.Write('\n');
                    s.fmtExpect &= ~FormatExpect.Newline;
                }
            }

            return this;
        }
        /// <summary>
        /// Write a comment for JSON, starting with // and ending with newline character. This is only allowed if <see cref="allowComments"/> is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// If not pretty printing and writing inside, this will call <see cref="WriteComment"/> instead. This is because it requires newlines.
        /// <br>If not pretty printing, but not writing inside, this will add a new line to the root.</br>
        /// </remarks>
        /// <param name="hasNextValue">
        /// <br>To print "prettier", add a trailing comma or colon to the previous value. This will look nicer like</br>
        /// <c><br>["value", // ...\n ... </br></c>
        /// <br>instead of the usual <c>["value" // ...\n,</c></br>
        /// <br><b>Set this <see langword="true"/> with caution, as you can produce invalid results (trailing commas) with it if there is no next value.</b></br>
        /// </param>
        /// <param name="data">Data inside the comment to write. It is padded with spaces by default.</param>
        /// <param name="prefixPad">Padding prefix character to use. Pass 0 to not pad the <paramref name="data"/> with anything. Invalid characters (like '\n') also avoids padding.</param>
        /// <returns>Whether if the write was successful.</returns>
        public SJWriter WriteCommentLine(ReadOnlySpan<char> data, char prefixPad = ' ', bool hasNextValue = false)
        {
            ref var s = ref S;
            if (!CheckState()) return Self();
            if (!allowComments) return Self("Cannot write comments on this writer, enable 'allowComments'");
            if (s.depth > 0 && indentSize <= 0)
            {
                return WriteComment(data, prefixPad, hasNextValue);
            }

            s.MarkWrittenComment();
            PrepareCommentValue(hasNextValue);

            Data.Write("//");
            for (int i = -1; i < data.Length; i++)
            {
                if (i < 0)
                {
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Data.Write(prefixPad); }
                    continue;
                }

                char c = data[i];
                Data.Write(c);
                if (c == '\n')
                {
                    WriteIndent(s.depth);
                    Data.Write("//");
                    if (prefixPad != 0 && prefixPad != '\r' && prefixPad != '\n') { Data.Write(prefixPad); }
                }
            }

            s.fmtExpect = FormatExpect.NewlineIndent;
            // something something FormatExpect is done after Expect tokens and this is whitespace sensitive
            if (s.depth > 0 && (s.expect & Expect.Comma) == Expect.Comma)
            {
                Data.Write('\n');
                s.fmtExpect &= ~FormatExpect.Newline;
            }

            return this;
        }

        // These are like "extensions"
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(float number, string format = DefaultNumberFormat) => WriteKeyNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(double number, string format = DefaultNumberFormat) => WriteKeyNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(int number, string format = DefaultNumberFormat, string prefix = null) => WriteKeyInt(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(uint number, string format = DefaultNumberFormat, string prefix = null) => WriteKeyUInt(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(long number, string format = DefaultNumberFormat, string prefix = null) => WriteKeyLong(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(ulong number, string format = DefaultNumberFormat, string prefix = null) => WriteKeyULong(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(bool value) => WriteKeyBool(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(ReadOnlySpan<char> value) => WriteKeyString(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter WriteKey(string value)
        {
            if (value is null) return WriteNull();
            else return WriteString(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(float number, string format = DefaultNumberFormat) => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(double number, string format = DefaultNumberFormat) => WriteNumber(number, format);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(int number, string format = DefaultNumberFormat, string prefix = null) => WriteInt(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(uint number, string format = DefaultNumberFormat, string prefix = null) => WriteUInt(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(long number, string format = DefaultNumberFormat, string prefix = null) => WriteLong(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(ulong number, string format = DefaultNumberFormat, string prefix = null) => WriteULong(number, format, prefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(bool value) => WriteBool(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(ReadOnlySpan<char> value) => WriteString(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SJWriter Write(string value)
        {
            if (value is null) return WriteNull();
            else return WriteString(value);
        }

        // Object/Array scope
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayScope Array() => new ArrayScope(this);
        public ArrayScope ArrayWithKey(ReadOnlySpan<char> key)
        {
            WriteKeyString(key);
            if (HasError)
            {
                // No operation is done if key fails.
                return new ArrayScope();
            }

            return new ArrayScope(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ObjectScope Object() => new ObjectScope(this);
        public ObjectScope ObjectWithKey(ReadOnlySpan<char> key)
        {
            WriteKeyString(key);
            if (HasError)
            {
                // No operation is done if key fails.
                return new ObjectScope();
            }

            return new ObjectScope(this);
        }

        /// <summary>
        /// Shows a simple preview of the current state.
        /// </summary>
        public override string ToString() => $"[{base.ToString()}] count={Data?.Count} state=({S}) error={Error}";

        public static explicit operator bool(SJWriter w) => !w.HasError;

        // Code execution only - can be readonly
        private SJValueStack<State> _stateStack = SJValueStack<State>.CreateWithMaxSize(128);
        public int StateCount => _stateStack.Count;
        /// <summary>
        /// Pushes a loadable stacked state.
        /// </summary>
        /// <returns>
        /// The index of the pushed state.
        /// For any other thing with it's own state, this can be used to write it into it's own index like:
        /// <br><c>result := <see cref="PushState"/>; myStack[result] = otherState;</c></br>
        /// </returns>
        public int PushState()
        {
            if (_StData == null)
            {
                Error = "Data source does not support stacking, it must implement stackability interface.";
                return -1;
            }

            CheckVersion();
            CheckDisposed();
            var handle = _stateStack.Push(false);
            if (handle.IsStub) return -1;

            int pos = _stateStack.Count - 1;
            ref var next = ref handle.Value;
            S.CopyTo(ref next); // Copy the data to the entry in stateStack

            // Other values
            _StData.PushState();
            return pos;
        }
        /// <summary>
        /// Loads a stacked state.
        /// </summary>
        /// <returns>
        /// The index of the loaded state.
        /// For any other thing with it's own state, this can be used to read from it's own index like:
        /// <br><c>result := <see cref="PopState"/>; otherState = myStack[result];</c></br>
        /// </returns>
        public int PopState()
        {
            if (_StData == null)
            {
                Error = "Data source does not support stacking, it must implement stackability interface.";
                return -1;
            }

            CheckVersion();
            CheckDisposed();
            if (_stateStack.Count <= 0) return -1;
            _stateStack.PopValue().CopyTo(ref S);

            int pos = _stateStack.Count;
            _StData.PopState();
            return pos;
        }

        /// <summary>
        /// Resets the writer to the default state. This also removes any stacked state if present.
        /// </summary>
        /// <remarks>
        /// <br>Unlike the <see cref="SJReader"/>, this isn't required 
        /// to commit any data and can remove present data. Use a Flush method if
        /// data isn't commited as it's being written (ex: Stream sources)
        /// and close the writer instead or use alternative Reset methods.</br>
        /// </remarks>
        public void Reset()
        {
            S.Reset();
            _Data.Reset();
            LastDataVersion = _Data.Version;
            _stateStack.Clear(false);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects)
                    Data.Dispose();
                }

                _Data = null;
                _StData = null;
                S = default;
                _stateStack = default;
                disposedValue = true;
            }
        }
        // ~SJWriter()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
#pragma warning restore IDE0057 // ***** does not support System.Range
