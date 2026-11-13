using BX.SJ.Collections;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Inferred type of the read data.
    /// <br>Custom types (if a declaration exists) are just given as a slice within <see cref="SJReader.Value"/>.</br>
    /// </summary>
    public enum SJType : ushort
    {
        Error,

        // Values
        Number,
        Key,
        String,
        Bool,
        Null,

        // Structure
        Object,
        Array,
        End,

        // Metadata
        Comment,
        Type,
    }
    /// <summary>
    /// Inferred type variant, containing more information about the value.
    /// </summary>
    public enum SJTypeVariant : int // ! May change in future versions.
    {
        // TODO :
        // * (3.1.x) More variant infers
        None,

        // Values
        ScientificNumber,
        FloatNumber,
        HexNumber,
        OctalNumber,

        // Metadata
        MultiComment,
        SingleComment,

        TypeLiteral,
        TypeString,
    }

    /// <summary>
    /// Base class for <i>most</i> Json compatible BXSJ readers.
    /// </summary>
    /// <example>
    /// // Refer to file:///./SJStringReader.cs
    /// </example>
    public sealed class SJReader : IDisposable
    {
        /// <summary>
        /// Exception thrown on an error case while reading BXSJ.
        /// </summary>
        public class ReadException : Exception
        {
            public ReadException(SJReader reader) : base($"{reader.Error} at position {reader.data.Position}")
            { }
            public ReadException(string message) : base(message)
            { }
        }
        /// <summary>
        /// Intermediate value result from <see cref="Read"/>.
        /// </summary>
        public struct Value
        {
            // <= 32 bytes
            public readonly SJReader reader;
            public SJType type, prevType;
            public SJTypeVariant variant;
            public int start, end;
            /// <summary>
            /// Object typed value depth of this value. This is used for <see cref="DiscardUntil(int)"/>.
            /// <br><b>Only applicable when type is <see cref="SJType.Object"/>, 
            /// <see cref="SJType.Array"/> or <see cref="SJType.End"/>, otherwise it's 0.</b></br>
            /// </summary>
            public ushort objectDepth;
            /// <summary>
            /// <see cref="SJReader.s.depth"/> of the <see cref="reader"/> while this Value was being created.
            /// </summary>
            public readonly ushort depth;

            public Value(SJReader reader)
            {
                this.reader = reader;
                type = prevType = SJType.Error;
                variant = SJTypeVariant.None;
                start = end = 0;
                objectDepth = 0;
                depth = (ushort)(reader?.S.depth ?? 0);
            }
            private Value(SJType stubType, SJReader reader = null)
            {
                this.reader = reader;
                type = prevType = stubType;
                variant = SJTypeVariant.None;
                start = end = 0;
                objectDepth = 0;
                depth = (ushort)(reader?.S.depth ?? 0);
            }

            public static Value Error() => new Value(SJType.Error);
            public static Value Error(SJReader reader) => new Value(SJType.Error, reader);
            public static Value Null() => new Value(SJType.Null);
            public static Value Null(SJReader reader) => new Value(SJType.Null, reader);

            public readonly int Length
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => end - start;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice()
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.data.SliceU16(start, end - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.data.SliceU16(this.start + start, Length - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start, int length)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.data.SliceU16(this.start + start, length);
            }

            public override readonly string ToString()
            {
                return $"[SJReader.Value] type:{type}, start:{start}, end:{end}, depth:{objectDepth}";
            }
        }
        public struct StateScope : IDisposable
        {
            private bool disposed;
            private readonly bool popIfError;
            private readonly SJReader reader;

            public StateScope(SJReader reader, bool popIfError)
            {
                this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

                disposed = false;
                this.popIfError = popIfError;
                reader.PushState();
            }

            public void Dispose()
            {
                if (disposed || reader == null) return;

                if (!popIfError || !string.IsNullOrEmpty(reader.Error))
                {
                    reader.PopState();
                }
                disposed = true;
            }
        }
        [Flags]
        public enum Expect
        {
            None = 0,
            Comma = 1 << 0,  // Used for checking duplicate and trailing commas
            Key = 1 << 1,    // Used for checking key type on key entries
            Colon = 1 << 2,  // Used for checking duplicate colons
        }
        public struct State
        {
            public struct Stacked
            {
                public readonly SJType type;
                public int valueCount;
                public Expect expect;
                public readonly bool Invalid => type switch
                {
                    SJType.Object => false,
                    SJType.Array => false,
                    _ => true
                };

                public Stacked(SJType type, Expect expect)
                {
                    this.type = type switch
                    {
                        SJType.Object => type,
                        SJType.Array => type,
                        _ => throw new ArgumentException("Supplied stateful type must be Object or Array only", nameof(type))
                    };
                    this.expect = expect;
                    valueCount = 0;
                }

                public override readonly string ToString()
                {
                    if (Invalid) return "invalid";
                    return $"type={type}, valueCount={valueCount}, expect={expect}";
                }
            }

            [EditorBrowsable(EditorBrowsableState.Never)]
            public string _error;
            public int depth
            {
                readonly get => stack.Count;
                internal set => stack.Count = value;
            }
            public Value lastEntryKey;
            public Value lastEntryType;

            public bool notExpectingType;
            public bool ended;

            public ValueStack<Stacked> stack;
            public readonly bool HasStackValues => depth > 0;

            public readonly void CopyTo(ref State s)
            {
                var prevStacked = s.stack;
                s = this;
                s.stack = prevStacked;

                s.stack.Clear(false);
                stack.CopyTo(ref s.stack);
            }

            public void Reset()
            {
                stack.Clear(false);
                lastEntryKey = default;
                ended = false;
                _error = null;
            }
            public override readonly string ToString()
            {
                return $"depth={depth}, top={{{stack.PeekValue()}}}";
            }
        }

        // - config
        public const int DefaultMaxDepth = 128;
        /// <summary>
        /// Maximum read depth as generally recursion is used to read.
        /// <br>Set to zero to disable any depth checks.</br>
        /// </summary>
        public ushort maxDepth = DefaultMaxDepth;
        /// <summary>
        /// Maximum amount of stackable state.
        /// </summary>
        public int maxStateStack
        {
            get => _stateStack.maxSize;
            set => _stateStack.maxSize = value;
        }
        /// <summary>
        /// <br>Allows comments on BXSJ file : </br><br/>
        /// <br><c>// &lt;-- single line, until \n</c></br>
        /// <br><c>/* &lt;-- until end token, not stacked --&gt; */</c></br>
        /// </summary>
        public bool allowComments = false;
        /// <summary>
        /// Allows primitive key types (<see cref="SJType.Number"/>, etc.)
        /// <br>Recursive key types such as <see cref="SJType.Object"/> are not supported.</br>
        /// <br></br>
        /// <br><c>{ 1234: "number", 0x1234: "hex number", null: "null", true: "true", false: "false" }</c> is considered valid</br>
        /// <br><c>{ {"a": "b"}: "object", ["a"]: "array" }</c> is <b>not <sup>yet</sup></b> considered valid.</br>
        /// </summary>
        public bool allowOtherKeyTypes = false;
        /// <summary>
        /// Allow trailing comma on last elements of array or objects.
        /// <br></br>
        /// <br><c>{ "k": [1,2,4,], "v": { "a": true, }, }</c> is considered valid.</br>
        /// </summary>
        public bool allowTrailingComma = false;
        /// <summary>
        /// Allow prefix type declarations before a value.
        /// The type declarations do nothing, they are extra metadata to be not confused with other values.
        /// <br>(The alternative would have been to embed these into a <see cref="SJType.String"/>, 
        /// but that can be "confused". Writer will also have tooling for this. This is useful to retain extra information.)</br>
        /// <br>The type declarations are optional, they are not needed for every entry.</br>
        /// <br></br>
        /// <br><c>{ "k": (TypeName)[1, 2, 3], "k2": (0x7463)"TypeCode.Int32", "k3": "aa" }</c></br>
        /// <br>Type literals can be like string. They don't need to be properly escaped (like strings) as SJReader is more tolerant on strings and only matches '"'.</br>
        /// <br><c>{ "k": ("Type name with everythin' including (\")")null, "k2": ("System.Array, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral")[1, 2, 3] }</c></br>
        /// <br>Type literals can exist for the root object.</br>
        /// <br><c>(MyType){ "foo": 42, "bar": ["baz", 2, "fizz"] }</c></br>
        /// </summary>
        public bool allowTypes = false;
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

        // - state
        private State _S;
        public ref State S => ref _S;
        /// <summary>
        /// The error string detailing why the reader failed.
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

        // - sources
        // (btw, Writer will be infinitely easier, just get ReadOnlySpan<char> and
        // do conversions yourself or write in memory as UTF-16 LE/BE whatever)
        // Smarter optimizations + less peeks can be done with this reader, I'm just directly porting it to 3.x
        public readonly ISJDataReader data;
        public int LastDataVersion { get; private set; }
        // An interesting extensibility idea, but not really for SJ.
        // public SortedList<ISJLexerHandler> Lexers { get; set; }

        /// <summary>
        /// <see cref="SJType.Number"/> continuation check, Note that this reader 
        /// does no validation of the number token so that should be done by external code if needed.
        /// <br>Accepts base 16 (hex) numbers. To store larger bases of integers or data as format, prefer <see cref="SJType.String"/></br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNumberContinuation(char c, out SJTypeVariant foundOn)
        {
            foundOn = SJTypeVariant.None;
            if ((c >= '0' && c <= '9')) return true;
            if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
            {
                foundOn = c switch
                {
                    'e' => SJTypeVariant.ScientificNumber,
                    'E' => SJTypeVariant.ScientificNumber,
                    _ => SJTypeVariant.HexNumber
                };
                return true;
            }
            switch (c)
            {
                case '-':
                case '+':
                    return true;
                case 'x':
                    foundOn = SJTypeVariant.HexNumber;
                    return true;
                case '.':
                    foundOn = SJTypeVariant.FloatNumber;
                    return true;
                case 'o':
                    foundOn = SJTypeVariant.OctalNumber;
                    return true;

                default:
                    return false;
            }
        }
        /// <summary>
        /// <see cref="SJType.Type"/> as literal (not string) continuation check.
        /// <br>String continuation should be checked similarly to <see cref="SJType.String"/></br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTypeLiteralContinuation(char c) => ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) || c switch
        {
            '-' => true,
            '_' => true,
            '.' => true,
            '=' => true,
            _ => false
        };
        public static bool StringEqualsToAscii(ReadOnlySpan<byte> bytes, string data)
        {
            if (bytes.Length != data.Length) return false;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != data[i]) return false;
            }
            return true;
        }
        /// <summary>
        /// String inspector for token.
        /// <br>Outputs <paramref name="c"/> as <c>{c}/{(int)c}</c></br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string Tk(char c) => c == 0xFF ? "Any" : (c == '\0' ? "\\0/0" : $"{c}/{(int)c}");

        public SJReader(ISJDataReader data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// Read a range of comment, starting from <see cref="State.current"/> for the reader data.
        /// </summary>
        /// <param name="variant">Variant of the comment type.</param>
        /// <returns>Start and ending range. If the block doesn't start with '/', 
        /// -1 is returned for start and end without setting error.</returns>
        private (int start, int end) GetCommentRange(out SJTypeVariant variant)
        {
            int start, end;
            variant = SJTypeVariant.None;

            // (/*|//)
            if (data.Ended || data.Current != '/') return (-1, -1);
            data.Next();

            char ch = (char)data.Current;
            switch (ch)
            {
                case '/':
                    variant = SJTypeVariant.SingleComment;
                    break;
                case '*':
                    variant = SJTypeVariant.MultiComment;
                    break;
                default:
                    Error = $"Unexpected comment token '{Tk(ch)}', expected '/ or *' to start a comment.";
                    return (-1, -1);
            }
            data.Next();

            // \s*<string>\s*(*/|\n)
            int firstNonWs = start = data.Index;
            int lastNonWs = data.Index;
            for (ch = (char)data.Current; !data.Ended; ch = (char)data.Next())
            {
                switch (variant)
                {
                    case SJTypeVariant.SingleComment:
                        switch (ch)
                        {
                            case '\n':
                                data.Next(); // Skip '\n'
                                goto _BreakOuter;
                            default:
                                if (!char.IsWhiteSpace(ch))
                                {
                                    if (firstNonWs == start) firstNonWs = data.Index;
                                    lastNonWs = data.Index;
                                }
                                break;
                        }
                        break;
                    case SJTypeVariant.MultiComment:
                        switch (ch)
                        {
                            case '*':
                                ch = (char)data.Next();
                                switch (ch)
                                {
                                    case '/': // */
                                        goto _BreakOuter;
                                    default:  // *[^]
                                        if (!char.IsWhiteSpace(ch))
                                        {
                                            if (firstNonWs == start) firstNonWs = data.Index;
                                            lastNonWs = data.Index;      // *[^]
                                        }
                                        else
                                        {
                                            if (firstNonWs == start) firstNonWs = data.Index - 1;
                                            lastNonWs = data.Index - 1;  // *\s
                                        }
                                        break;
                                }
                                break;
                            default:
                                if (!char.IsWhiteSpace(ch))
                                {
                                    if (firstNonWs == start) firstNonWs = data.Index;
                                    lastNonWs = data.Index;
                                }
                                break;
                        }
                        break;
                    default:
                        Error = $"Unknown comment variant {variant}";
                        return (-1, -1);
                }
            }
        _BreakOuter:
            start = firstNonWs;  // Start is already inclusive
            end = lastNonWs + 1; // End has to be inclusive
            switch (variant)
            {
                case SJTypeVariant.MultiComment:
                    if (data.Ended)
                    {
                        Error = "Unclosed multi line comment, expected '*/'";
                        return (-1, -1);
                    }
                    data.Next(); // Skip '/'
                    break;
            }

            return (start, end);
        }
        /// <summary>
        /// Read a range of type, starting from <see cref="State.current"/> for the reader data.
        /// </summary>
        /// <param name="i">Target position of the pointer.</param>
        /// <returns>Start and ending range. If the block doesn't start with '(', -1 is returned for start and end.</returns>
        private (int start, int end) GetTypeRange(out SJTypeVariant variant)
        {
            int start; int end;
            variant = SJTypeVariant.None;

            if (data.Ended || data.Current != '(') return (-1, -1);
            data.Next();

            // Skip whitespace
            // Note : Whitespace is limited to non-unicode ones.
            // .. Though there are probably multi byte whitespace.
            // Eh. This isn't too important, we aren't iterating rune / code points.
            data.SkipWhiteSpace();

            // Check for string literal starting with '"'
            if (!data.Ended && data.Current == '"')
            {
                data.Next(); // skip opening quote
                start = data.Index;
                bool escaped = false;
                variant = SJTypeVariant.TypeString;

                while (!data.Ended)
                {
                    byte c = data.Current;
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') break;
                    data.Next();
                }

                if (data.Ended || data.Current != '"')
                {
                    Error = "Unclosed type literal string";
                    return (-1, -1);
                }

                end = data.Index;
                data.Next();
            }
            else
            {
                start = data.Index;
                variant = SJTypeVariant.TypeLiteral;

                while (!data.Ended)
                {
                    byte c = data.Current;
                    if (!IsTypeLiteralContinuation((char)c)) break;
                    data.Next();
                }

                if (data.Index == start)
                {
                    Error = "Empty type literal";
                    return (-1, -1);
                }
                end = data.Index;
            }

            // Skip whitespace
            data.SkipWhiteSpace();

            // Must end with ')'
            if (data.Ended || data.Current != ')')
            {
                Error = "Unclosed type literal";
                return (-1, -1);
            }
            data.Next();

            return (start, end);
        }
        /// <summary>
        /// Check remaining tokens after the document is finished. (depth == 0)
        /// <br>Does parsing of comments if <see cref="allowComments"/></br>
        /// </summary>
        /// <returns>
        /// <see langword="false"/> if the ending has invalid characters. <see langword="true"/> if the document 
        /// ending is valid <b>or the code must yield, if <see cref="captureComments"/>.</b>
        /// </returns>
        private bool UpdateEnding()
        {
            ref var s = ref S;
            if (s.depth < 0)
            {
                return false;
            }

            // assert EOF or only whitespace if depth is zero.
            for (; s.depth == 0 && !data.Ended; data.Next())
            {
                byte ch = data.Current;

                if (allowComments && ch == '/')
                {
                    // Yield for parsing the comment line as current points to == '/',
                    // as it's "not EOF" and "more data available"
                    return true;
                }
                else if (char.IsWhiteSpace((char)ch))
                {
                    data.SkipWhiteSpace();
                }
                else
                {
                    Error = "Unexpected data after end of document";
                    return false;
                }
            }

            // more data available.
            return true;
        }

        /// <summary>
        /// Check last <see cref="Stacked"/> on the stack according to the rules.
        /// <br>It also validates <paramref name="v"/> and can mutate that value as well</br>
        /// </summary>
        /// <returns><see langword="true"/> if the state is valid, otherwise you should yield back to top.</returns>
        private bool UpdateState(ref State.Stacked cs, ref Value v)
        {
            if (cs.Invalid)
            {
                // Supplying "invalid" state means you probably put the stub state in.
                // Which means there was no state to update..
                return true;
            }
            if (v.type == SJType.Comment)
            {
                return true;
            }

            switch (cs.type)
            {
                case SJType.Object:
                    if ((cs.expect & Expect.Key) == Expect.Key)
                    {
                        if (v.type != SJType.String)
                        {
                            if (allowOtherKeyTypes)
                            {
                                switch (v.type)
                                {
                                    case SJType.Array:
                                    case SJType.Object:
                                        Error = "Recursive key types are not supported";
                                        return false;
                                }
                            }
                            else
                            {
                                Error = "Expected object key as valid Key type";
                                return false;
                            }
                        }
                        v.prevType = v.type;
                        v.type = SJType.Key;

                        cs.expect &= ~Expect.Key;
                        cs.expect |= Expect.Colon;
                    }
                    else
                    {
                        cs.valueCount++;
                        cs.expect &= ~Expect.Colon;
                        cs.expect |= Expect.Comma | Expect.Key;
                    }
                    break;
                case SJType.Array:
                    cs.valueCount++;
                    cs.expect |= Expect.Comma;
                    break;
                default:
                    Error = $"Invalid current state type {cs.type}";
                    return false;
            }

            return true;
        }
        /// <summary>
        /// If <b>no new state is pushed</b> to the stack on <see cref="Read"/> after reading a value,
        /// you don't need to track / mutate last state and can use this method instead.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="true"/> if no state exists, meaning there isn't any state to check.
        /// </remarks>
        /// <returns><see langword="true"/> if the state is valid, otherwise you should yield back to top.</returns>
        private bool UpdateLastState(ref Value v)
        {
            bool hasStacked = S.HasStackValues;
            S.notExpectingType = !hasStacked; // Expect only one type if not a stacked value entry.
            if (!hasStacked)
            {
                return true;
            }

            return UpdateState(ref S.stack.PeekValue(), ref v);
        }

        /// <summary>
        /// Throws the currently stored error. It does not check whether if an error is available.
        /// </summary>
        /// <exception cref="ReadException"></exception>
        public void ThrowError()
        {
            throw new ReadException(this);
        }
        private void CheckDisposed()
        {
            if (disposedValue) throw new InvalidOperationException("Attempt to read from a disposed SJReader.");
        }
        public void CheckVersion()
        {
            if (data.Version != LastDataVersion)
            {
                LastDataVersion = data.Version;
                Reset();
            }
        }

        // Base sj.h
        /// <summary>
        /// Top level reader for a single non-recursive lexable generic value.
        /// </summary>
        public Value Read()
        {
            CheckDisposed();
            CheckVersion();
            ref var s = ref S;
            var result = new Value(this);
            s.ended = !string.IsNullOrEmpty(Error);
        _Top:
            if (!string.IsNullOrEmpty(Error))
            {
                result.type = SJType.Error;
                result.start = result.end = 0;
                result.objectDepth = 0;
                return result;
            }
            if (data.Empty)
            {
                Error = "Empty data";
                goto _Top;
            }
            if (data.Ended)
            {
                // Reaching end of document is no longer an error, but it should return an empty "error" value.
                result.type = SJType.Error;
                result.start = result.end = data.Index;
                result.objectDepth = 0;
                s.ended = true;
                if (s.depth > 0)
                {
                    Error = s.stack.PeekValue().type switch
                    {
                        SJType.Object => "Unclosed object",
                        SJType.Array => "Unclosed array",
                        _ => "Unclosed value with invalid stack type"
                    };
                }
                return result;
            }
            if (maxDepth > 0 && s.depth > maxDepth)
            {
                Error = $"Exceeded maximum depth {maxDepth}";
                goto _Top;
            }

            result.start = data.Index;
            char ch = (char)data.Next();
            // Skip : White Space
            if (char.IsWhiteSpace(ch))
            {
                data.SkipWhiteSpace();
                goto _Top;
            }
            // Validate / Parse : Comments
            else if (ch == '/')
            {
                if (!allowComments)
                {
                    Error = "Unexpected '/', enable comments through 'allowComments'";
                    goto _Top;
                }

                result.type = SJType.Comment;
                (result.start, result.end) = GetCommentRange(out result.variant);
                if (string.IsNullOrEmpty(Error) && result.start >= 0 && result.end > 0)
                {
                    return result;
                }
                goto _Top;
            }
            // Parse: Type (literal or something else). Treated sort of like a comment.
            else if (ch == '(')
            {
                if (!allowTypes)
                {
                    Error = "Unexpected '(', enable types through 'allowTypes'";
                    goto _Top;
                }
                if (s.notExpectingType)
                {
                    Error = "Was not expecting a duplicate type definition";
                    goto _Top;
                }

                result.type = SJType.Type;
                (result.start, result.end) = GetTypeRange(out result.variant);
                if (string.IsNullOrEmpty(Error) && result.start >= 0 && result.end > 0)
                {
                    s.notExpectingType = true;
                    s.lastEntryType = result;
                    return result;
                }
                goto _Top;
            }
            // Validate : Object == KV Colon, Array == Comma
            else if (ch == ':' || ch == ',')
            {
                if (s.HasStackValues)
                {
                    ref var st = ref s.stack.PeekValue();
                    if (ch == ':')
                    {
                        if (st.type != SJType.Object)
                        {
                            Error = "Unexpected ':' in top level array";
                            goto _Top;
                        }
                        else if ((st.expect & Expect.Colon) != Expect.Colon)
                        {
                            Error = "Unexpected ':', exceeding required";
                            goto _Top;
                        }

                        st.expect &= ~Expect.Colon;
                        s.notExpectingType = false; // Type entry can be written after ':' for the value type.
                    }
                    else if (ch == ',')
                    {
                        // both Array and Object get commas
                        if ((st.expect & Expect.Comma) != Expect.Comma)
                        {
                            Error = "Unexpected ',', exceeding required";
                            goto _Top;
                        }

                        st.expect &= ~Expect.Comma;
                    }
                    else
                    {
                        // Unreachable
                        Error = $"ch '{Tk(ch)}' was somehow mutated on comma/colon check and no longer fulfills any condition";
                        goto _Top;
                    }
                }
                else
                {
                    Error = "Unexpected ':' or ',' in top level nothing";
                }

                goto _Top;
            }
            else if ((s.stack.PeekValue().expect & Expect.Colon) == Expect.Colon)
            {
                // Colons are strict, they are seperator for key/value
                Error = $"Unexpected '{Tk(ch)}', expected ':'";
                goto _Top;
            }
            // Parse : Array | Object End (must override comma)
            else if (ch == '}' || ch == ']')
            {
                result.type = SJType.End;
                // Validate last state and update "topmost current state"
                var st = s.stack.PopValue();
                if (s.depth < 0 || st.type != ((ch == '}') ? SJType.Object : SJType.Array))
                {
                    Error = (ch == '}') ? "Stray '}'" : "Stray ']'";
                    goto _Top;
                }
                if (st.valueCount > 0)
                {
                    if ((st.expect & Expect.Comma) != Expect.Comma && !allowTrailingComma)
                    {
                        Error = "Trailing comma character for the last value before end";
                        goto _Top;
                    }
                }

                result.end = data.Index;

                // UpdateState here is not needed because the upper state will be checked later
                if (!UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Validate: Commas
            else if ((S.stack.PeekValue().expect & Expect.Comma) == Expect.Comma)
            {
                // Commas are more flexible and can be found anywhere else,
                // however they are not required for object declarations,
                // but they are required for literals and object endings.
                Error = $"Unknown token '{Tk(ch)}', expected ','";
                goto _Top;
            }
            // Parse : Array | Object Start
            else if (ch == '{' || ch == '[')
            {
                result.type = ch == '{' ? SJType.Object : SJType.Array;
                var prevHandle = s.stack.Peek();
                s.stack.PushValue(new State.Stacked(result.type, ch == '{' ? Expect.Key : Expect.None));
                prevHandle = prevHandle.Renew(s.stack);

                result.objectDepth = (ushort)s.depth;
                s.notExpectingType = false; // Technically a value entry, just does not update last state.

                if (!UpdateState(ref prevHandle.Value, ref result))
                {
                    goto _Top;
                }
            }
            // Parse : Number (technically, hex and binary is supported if prefixed with 0x)
            else if (char.IsDigit(ch) || ch == '-')
            {
                // Finite automata? What "for"
                result.type = SJType.Number;
                for (ch = (char)data.Current;
                    !data.Ended && IsNumberContinuation(ch, out var foundOn);
                    ch = (char)data.Next()
                )
                {
                    // Not super accurate, but will do.
                    // Theoretically we can check number literal validity this way,
                    // but whatever that's many more state to the larpmata.
                    // The hex and scientific notation can be confused, so this is checked.
                    switch (result.variant)
                    {
                        case SJTypeVariant.None:
                            switch (foundOn)
                            {
                                case SJTypeVariant.HexNumber:
                                    result.variant = SJTypeVariant.HexNumber;
                                    break;
                                case SJTypeVariant.ScientificNumber:
                                    if (result.start == data.Index) goto case SJTypeVariant.HexNumber;
                                    result.variant = SJTypeVariant.ScientificNumber;
                                    break;

                                default:
                                    result.variant = foundOn;
                                    break;
                            }
                            break;
                        // If we are on a sci number, but we get hex data, it could be a number like E00F..
                        // Just having 1EEEEEEEEEE will make it a scientific number, in reality it's a hex number
                        // So repeated 'E' is not considered scientific notation.
                        case SJTypeVariant.ScientificNumber:
                            switch (foundOn)
                            {
                                case SJTypeVariant.ScientificNumber:
                                case SJTypeVariant.HexNumber:
                                    result.variant = SJTypeVariant.HexNumber;
                                    break;
                            }
                            break;
                    }
                }
                result.end = data.Position;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : String
            else if (ch == '"')
            {
                ch = (char)data.Next();
                result.type = SJType.String;
                result.start = data.Index;

                bool escape = false;
                while (true)
                {
                    if (data.Ended)
                    {
                        Error = "Unclosed string";
                        goto _Top;
                    }
                    if (escape) escape = false;
                    else if (ch == '\\') escape = true;
                    else if (ch == '"') break;
                    ch = (char)data.Next();
                }
                data.Next(); // Skip '"'
                result.end = data.Index;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : null | true | false | any supported literal
            else
            {
                // what in the strcmp()
                switch (ch)
                {
                    case 'n':
                        {
                            Span<byte> aData = stackalloc byte[4];
                            aData = aData.Slice(0, 3);
                            data.Next(aData);
                            result.type = SJType.Null;

                            if (!StringEqualsToAscii(aData, "ull"))
                            {
                                goto default;
                            }
                            break;
                        }
                    case 't':
                        {
                            Span<byte> aData = stackalloc byte[4];
                            aData = aData.Slice(0, 3);
                            data.Next(aData);
                            result.type = SJType.Bool;

                            if (!StringEqualsToAscii(aData, "rue"))
                            {
                                goto default;
                            }
                            break;
                        }
                    case 'f':
                        {
                            Span<byte> aData = stackalloc byte[4];
                            data.Next(aData);
                            result.type = SJType.Bool;

                            if (!StringEqualsToAscii(aData, "alse"))
                            {
                                goto default;
                            }

                            break;
                        }
                    // Fail
                    default:
                        Error = $"Unknown token '{Tk(ch)}' or truncated literal";
                        goto _Top;

                }

                result.end = data.Index; // Position points to anything

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }

            return result;
        }
        /// <summary>
        /// Moves <see cref="State.depth"/> and <see cref="State.current"/> until a depth.
        /// </summary>
        public void DiscardUntil(int depth)
        {
            CheckVersion();
            ref var s = ref S;
            var value = Value.Null();
            while (!s.ended && s.depth != depth && value.type != SJType.Error)
            {
                value = Read();
            }
        }
        /// <summary>
        /// Iterate <see cref="SJType.Array"/> typed <paramref name="value"/> with entry type info.
        /// </summary>
        /// <param name="type">Type of the entry. This is either <see cref="EntryType.Value"/> or <see cref="EntryType.None"/>.</param>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        public bool IterateArrayEntries(Value value, out Value result)
        {
            CheckVersion();
            if (value.type != SJType.Array)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.objectDepth);
            // * Must peek before Read()
            // * Must retain a handle for handling resize cases
            var stHandle = S.stack.Peek();
            result = Read();
            stHandle = stHandle.Renew(S.stack);

            if (result.type == SJType.Comment)
            {
                return true;
            }

            switch (result.type)
            {
                case SJType.Error:
                case SJType.End:
                    result = Value.Error(this);
                    return false;

                case SJType.Type:
                    S.lastEntryType = result;
                    return true;
                case SJType.Comment:
                    return true;
                default:
                    stHandle.Value.valueCount++;
                    return true;
            }
        }
        /// <summary>
        /// Iterate <see cref="SJType.Array"/> typed <paramref name="value"/>.
        /// </summary>
        /// <remarks>
        /// <b>This skips <see cref="SJType.Comment"/> blocks. If you want to capture comments 
        /// while reading an object, use lower level method of
        /// <see cref="IterateArrayEntries(Value, out Value)"/> instead.</b>
        /// </remarks>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        public bool IterateArray(Value value, out Value result)
        {
            while (IterateArrayEntries(value, out var v))
            {
                switch (v.type)
                {
                    case SJType.Comment:
                    case SJType.Type:
                        continue;
                    default:
                        result = v;
                        return true;
                }
            }

            result = Value.Error(this);
            return false;
        }
        /// <summary>
        /// Iterate <see cref="SJType.Object"/> typed <paramref name="value"/> with entry type info.
        /// </summary>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        public bool IterateObjectEntries(Value value, out Value result)
        {
            CheckVersion();
            if (value.type != SJType.Object)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.objectDepth);
            // * Must peek before Read()
            // * Must retain a handle for handling resize cases
            var stHandle = S.stack.Peek();
            result = Read();
            stHandle = stHandle.Renew(S.stack);

            switch (result.type)
            {
                case SJType.Error:
                case SJType.End:
                    result = Value.Error(this);
                    return false;

                case SJType.Type:
                    S.lastEntryType = result;
                    return true;
                case SJType.Comment:
                    return true;
                default:
                    stHandle.Value.valueCount++;
                    return true;
            }
        }
        /// <summary>
        /// Iterate <see cref="SJType.Object"/> typed <paramref name="object"/> value.
        /// </summary>
        /// <remarks>
        /// <b>This skips <see cref="SJType.Comment"/> blocks regardless.
        /// If you want to capture comments while reading an object, use lower level method of
        /// <see cref="IterateObjectEntries(Value, out Value)"/> instead.</b>
        /// <br>Calling <see cref="IterateObject(Value, out Value, out Value)"/> with an existing key 
        /// value will use that key value and poll for it's value, unless the value for that key was already read, 
        /// which in this case, this method will return the next key/value.
        /// Otherwise, this method will return the previous key/next unread value (if exists)</br>
        /// </remarks>
        /// <returns>Whether if more data is available to read from <paramref name="object"/>.</returns>
        public bool IterateObject(Value @object, out Value key, out Value value)
        {
            bool gotKey = false; key = default;
            while (IterateObjectEntries(@object, out Value result))
            {
                if (!string.IsNullOrEmpty(Error)) goto _Fail;

                switch (result.type)
                {
                    case SJType.Error:
                    case SJType.End:
                        goto _Fail;
                    case SJType.Comment:
                    case SJType.Type:
                        continue;

                    case SJType.Key:
                        key = result;
                        gotKey = true;
                        continue;

                    default:
                        if (!gotKey)
                        {
                            key = S.lastEntryKey;
                        }
                        value = result;
                        return true;
                }
            }

        _Fail:
            // did not find next Value, or IterateObject returned error.
            key = value = Value.Error(this);
            return false;
        }
        /// <summary>
        /// Peeks entry count. You do not have to iterate this method until it ends unlike the other ones.
        /// </summary>
        /// <param name="expectCount">
        /// Count of elements desired to be peeked.
        /// If left 0 or lower, all elements will be peeked.
        /// This does not include <paramref name="other"/> values.
        /// </param>
        /// <param name="v">Value to peek it's entry count.</param>
        /// <param name="keys">Count of keys seen.</param>
        /// <param name="values">Count of values seen.</param>
        /// <param name="other">Count of other entries (comments) seen.</param>
        /// <returns>
        /// Whether if the peek was successful.
        /// If this returns <see langword="false"/> then either the <paramref name="v"/>
        /// is erroreneous or <paramref name="expectCount"/> is exceeded.
        /// </returns>
        public bool PeekEntryCount(Value v, out int keys, out int values, out int other, int expectCount = -1)
        {
            CheckVersion();
            switch (v.type)
            {
                case SJType.Array:
                case SJType.Object:
                    break;
                default:
                    keys = values = other = -1;
                    return false;
            }

            keys = values = other = 0;

            // Stop PMO and start bankroll
            PushState();
            bool result = true;

            DiscardUntil(v.objectDepth);
            Value innerValue = Read();
            for (; innerValue.type != SJType.End; DiscardUntil(v.objectDepth), innerValue = Read())
            {
                switch (innerValue.type)
                {
                    case SJType.Error:
                        return false;

                    case SJType.Comment:
                        other++;
                        break;

                    case SJType.Key:
                        keys++;
                        break;
                    default:
                        values++;
                        break;
                }

                if (expectCount >= 0 && (keys > expectCount || values > expectCount))
                {
                    result = false;
                    break;
                }
            }

            PopState();

            return result;
        }

        private ValueStack<State> _stateStack = ValueStack<State>.CreateWithMaxSize(DefaultMaxDepth);
        private bool disposedValue;

        public int StateCount => _stateStack.Count;
        /// <summary>
        /// Pushes a loadable stacked state.
        /// </summary>
        /// <returns>
        /// The index of the pushed state.
        /// For any other SJReader with it's own state, this can be used to write it into it's own index like:
        /// <br><c>result := <see cref="PushState"/>; myStack[result] = otherState;</c></br>
        /// </returns>
        public int PushState()
        {
            CheckVersion();
            CheckDisposed();
            var handle = _stateStack.Push(false);
            if (handle.IsStub) return -1;

            int pos = _stateStack.Count - 1;
            ref var next = ref handle.Value;
            S.CopyTo(ref next); // Copy the data to the entry in stateStack

            // Other values
            data.PushState();
            return pos;
        }
        /// <summary>
        /// Loads a stacked state.
        /// </summary>
        /// <returns>
        /// The index of the loaded state.
        /// For any other SJReader with it's own state, this can be used to read from it's own index like:
        /// <br><c>result := <see cref="PopState"/>; otherState = myStack[result];</c></br>
        /// </returns>
        public int PopState()
        {
            CheckVersion();
            CheckDisposed();
            if (_stateStack.Count <= 0) return -1;
            _stateStack.PopValue().CopyTo(ref S);

            int pos = _stateStack.Count;
            data.PopState();
            return pos;
        }
        /// <summary>
        /// Starts a <see cref="SJWriter"/> transaction, which will reset if this state is disposed.
        /// </summary>
        public StateScope UseState(bool popIfError = false) => new StateScope(this, popIfError);

        /// <summary>
        /// Resets the reader to the default state. This also removes any stacked state if present.
        /// </summary>
        /// <remarks>
        /// <br><b>This shouldn't discard loaded data and only reset the state.</b>
        /// (should have called it flush, but SJReader does not commit any data?)</br>
        /// </remarks>
        public void Reset()
        {
            CheckDisposed();
            S.Reset();
            data.Reset();
            _stateStack.Clear();
        }

        public override string ToString()
        {
            return $"[SJReader] state=({S}), error={Error}";
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects)
                    if (data is IDisposable d) d.Dispose();
                }

                // Data = null;
                S = default;
                _stateStack = default;
                disposedValue = true;
            }
        }
        // ~SJReader()
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
