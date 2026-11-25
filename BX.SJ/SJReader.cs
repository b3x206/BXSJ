#pragma warning disable IDE0057 // ***** does not support System.Range
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
        //   * Empty = Used for empty object, array and string.
        //             {!} This can automate skipping the object/array, so use Peek
        //   * More numbers
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
    /// Class for Json compatible BXSJ readers.
    /// </summary>
    public sealed class SJReader : IDisposable
    {
        /// <summary>
        /// Exception thrown on an error case related to the BXSJ reader.
        /// </summary>
        public sealed class ReaderException : Exception
        {
            public ReaderException(SJReader reader) : base($"{reader.Error} at position {reader.S.current}")
            { }
            public ReaderException(string message) : base(message)
            { }
        }
        public struct StateStackScope : IDisposable
        {
            private readonly SJReader reader;
            private readonly bool popOnError;
            private bool disposed;

            public StateStackScope(SJReader reader, bool popOnError)
            {
                this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
                disposed = reader.PushState() < 0;
                this.popOnError = popOnError;
            }

            public void Dispose()
            {
                if (!disposed)
                {
                    if (!popOnError || reader.HasError) reader.PopState();

                    disposed = true;
                }
            }
        }

        public struct ValueInfo
        {
            public SJType type, prevType;
            public SJTypeVariant variant;
            public int start, end;
            /// <summary>
            /// <see cref="State.Depth"/> of the <see cref="reader"/> while this Value was being created.
            /// <br>If this is a <see cref="SJType.Object"/> or <see cref="SJType.Array"/>,
            /// this is one higher than the actual depth that this entry was started on.</br>
            /// </summary>
            public ushort depth;

            public readonly Value ToValue(ReadOnlySpan<char> c) => new Value(c) { i = this };
        }
        /// <summary>
        /// Intermediate value result from <see cref="Read"/>.
        /// </summary>
        public ref struct Value
        {
            // <= 32 bytes
            private readonly ReadOnlySpan<char> data;
            public ValueInfo i;

            public Value(ReadOnlySpan<char> data)
            {
                this.data = data;
                i = default;
                i.type = i.prevType = SJType.Error;
                i.variant = SJTypeVariant.None;
            }
            private Value(SJType stubType)
            {
                data = default;
                i = default;
                i.type = i.prevType = stubType;
                i.variant = SJTypeVariant.None;
            }

            public static Value Error() => new Value(SJType.Error);
            public static Value Null() => new Value(SJType.Null);

            public readonly int Length
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => i.end - i.start;
            }

            public readonly bool TrySlice(out ReadOnlySpan<char> s)
            {
                s = default;
                if (Length < 0 || Length > data.Length) return false;
                s = data.Slice(i.start, Length);
                return true;
            }
            public readonly ReadOnlySpan<char> TrySlice(ReadOnlySpan<char> fallback) // Reduce codepaths with this simple trick:
            {
                if (Length < 0 || Length > data.Length) return fallback;
                return data.Slice(i.start, Length);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> TrySlice() => TrySlice(fallback: i.type.ToString()); // Hope enum ToString is interned
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice()
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {i.type}) are not supported.");
                return data.Slice(i.start, Length);
            }

            public override readonly string ToString()
            {
                return $"[SJReader.Value] type:{i.type}, start:{i.start}, end:{i.end}, depth:{i.depth}";
            }
        }

        [Flags]
        public enum Expect
        {
            None = 0,
            Comma = 1 << 0,  // Used for checking duplicate and trailing commas
            Key = 1 << 1,    // Used for checking key type on key entries
            Colon = 1 << 2,  // Used for checking duplicate colons
            NotType = 1 << 3,
        }
        public struct State
        {
            public struct Stacked
            {
                public readonly SJType type;
                public int count;
                public readonly bool Invalid => type switch
                {
                    SJType.Object => false,
                    SJType.Array => false,
                    _ => true
                };

                public Stacked(SJType type)
                {
                    this.type = type switch
                    {
                        SJType.Object => type,
                        SJType.Array => type,
                        _ => throw new ArgumentException("Supplied stateful type must be Object or Array only", nameof(type))
                    };
                    count = 0;
                }

                public override readonly string ToString()
                {
                    if (Invalid) return "invalid stacked state";
                    else return $"type={type}, valueCount={count}";
                }
            }

            [EditorBrowsable(EditorBrowsableState.Never)]
            public string _error;
            public int Depth
            {
                readonly get => stack.Count;
                internal set => stack.Count = value;
            }
            public int current;
            public ValueInfo lastKey;
            public ValueInfo lastType;

            public uint readValueCount;
            public Expect expect;
            public bool ended;

            public SJValueStack<Stacked> stack;
            public readonly bool HasStackValues => Depth > 0;

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
                lastKey = lastType = default;
                ended = false;
                _error = null;
                readValueCount = 0;
            }
            public override readonly string ToString()
            {
                return $"depth={Depth}, top={{{stack.PeekValue()}}}";
            }
        }

        // - config
        /// <summary>
        /// Maximum read depth as generally recursion is used to read.
        /// <br>Set to zero to disable any depth checks.</br>
        /// </summary>
        public ushort maxDepth = SJConstants.MaxDepth;
        /// <summary>
        /// Maximum amount of stackable state. If set to zero or less, it can be stacked forever.
        /// </summary>
        public int MaxStateCount
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
        /// is not null will throw the S.currently existing error.</br>
        /// </summary>
        public bool ThrowOnError
        {
            get => _ThrowOnError;
            set
            {
                _ThrowOnError = value;
                if (_ThrowOnError && HasError)
                {
                    ThrowError();
                }
            }
        }

        // - state
        public State S;
        private bool disposedValue;
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
        public bool HasError => !string.IsNullOrEmpty(Error);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T WithError<T>(in T value, string err)
        {
            Error = err;
            return value;
        }

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
        /// <summary>
        /// String inspector for token.
        /// <br>Outputs <paramref name="c"/> as <c>{c}/{(int)c}</c></br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string Tk(char c) => c == 0xFF ? "Any" : (c == '\0' ? "\\0/0" : $"{c}/{(int)c}");

        /// <summary>
        /// * If a literal is a valid substring of other, the larger string will be attempted to be captured
        /// <br>* An exact match to any value within <paramref name="readerLiterals"/> is expected.</br>
        /// </summary>
        private SJConstants.Literal GetLiteralType(ReadOnlySpan<char> data)
        {
            // This assumes that all literals essentially start with a different
            // character and do not have any valid substrings within each other
            var slice = data.Slice(S.current, Math.Min(5, data.Length - S.current));
            for (var i = SJConstants.Literal.False; i <= SJConstants.Literal.Null; i++)
            {
                if (slice.Equals(i.ToToken(), StringComparison.Ordinal)) return i;
            }
            return SJConstants.Literal.None;
        }

        /// <summary>
        /// Read a range of comment, starting from <see cref="State.S.current"/> for the reader data.
        /// <br>* The data source is expected to be started before reading.</br>
        /// </summary>
        /// <param name="variant">Variant of the comment type.</param>
        /// <returns>Start and ending range. If the block doesn't start with '/', 
        /// -1 is returned for start and end without setting error.</returns>
        private (int start, int end) GetCommentRange(ReadOnlySpan<char> data, out SJTypeVariant variant)
        {
            int start, end;
            variant = SJTypeVariant.None;

            // (/*|//)
            if (data[S.current] != '/') return (-1, -1);
            if (++S.current >= data.Length) return WithError((-1, -1), "Unexpected EOF, expected '/ or *' to start a comment");

            char ch = data[S.current];
            switch (ch)
            {
                case '/':
                    variant = SJTypeVariant.SingleComment;
                    break;
                case '*':
                    variant = SJTypeVariant.MultiComment;
                    break;
                default:
                    return WithError((-1, -1), $"Unexpected comment token '{Tk(ch)}', expected '/ or *' to start a comment.");
            }
            if (++S.current >= data.Length)
            {
                switch (variant)
                {

                }
            }

            // \s*(.*)\s*(*/|\n)
            int firstNonWs = start = S.current;
            int lastNonWs = S.current;
            int endState = 0, lastNonWsState = 0;
            do
            {
                switch (variant)
                {
                    case SJTypeVariant.SingleComment:
                        switch (ch)
                        {
                            case '\n':
                                goto _BreakOuter;
                            default:
                                break;
                        }
                        break;
                    case SJTypeVariant.MultiComment:
                        switch (ch)
                        {
                            case '*':
                                endState++;

                                switch (endState)
                                {
                                    case 1: // *
                                        lastNonWsState = lastNonWs;
                                        lastNonWs = S.current;
                                        continue;
                                }
                                break;
                            case '/':
                                endState++;

                                switch (endState)
                                {
                                    case 2: // */
                                        lastNonWs = lastNonWsState; // This is set to the revertable one
                                        goto _BreakOuter;
                                }
                                break;
                        }
                        break;
                    default:
                        Error = $"Unknown comment variant {variant}";
                        return (-1, -1);
                }
                endState = 0;
                if (!char.IsWhiteSpace(ch))
                {
                    if (firstNonWs == start)
                    {
                        firstNonWs = S.current;
                    }
                    lastNonWs = Data.Index;
                }
            }
            while (Data.TryNext(out ch));
        _BreakOuter:
            start = firstNonWs;  // Start is already inclusive
            end = lastNonWs + 1; // End has to be inclusive of the last non-ws character.
            switch (variant)
            {
                default:
                    break;
                case SJTypeVariant.MultiComment:
                    if (endState != 2)
                    {
                        Error = "Unclosed multi line comment, expected '*/'";
                        return (-1, -1);
                    }
                    break;
            }

            return (start, end);
        }
        /// <summary>
        /// Read a range of type, starting from <see cref="State.S.current"/> for the reader data.
        /// <br>* The data source is expected to be started before reading.</br>
        /// </summary>
        /// <param name="i">Target position of the pointer.</param>
        /// <returns>Start and ending range. If the block doesn't start with '(', -1 is returned for start and end.</returns>
        private (int start, int end) GetTypeRange(out SJTypeVariant variant)
        {
            int start; int end;
            variant = SJTypeVariant.None;

            if (S.current != '(') return (-1, -1);
            Data.Next();

            // Skip whitespace
            // Note : Whitespace is limited to non-unicode ones.
            // .. Though there are probably multi byte whitespace.
            // Eh. This isn't too important, we aren't iterating rune / code points.
            Data.SkipWhiteSpace();

            // Check for string literal starting with '"'
            if (S.current == '"')
            {
                start = Data.Index + 1;
                bool escaped = false;
                variant = SJTypeVariant.TypeString;

                while (true)
                {
                    if (!Data.TryNext(out byte ch))
                    {
                        Error = "Unclosed type literal string";
                        return (-1, -1);
                    }
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') break;
                }

                end = Data.Index;
                Data.Next();
            }
            else
            {
                start = Data.Index;
                variant = SJTypeVariant.TypeLiteral;

                byte c = S.current;
                while (IsTypeLiteralContinuation((char)c) && Data.TryNext(out c)) ;

                if (Data.Index == start)
                {
                    Error = "Empty type literal";
                    return (-1, -1);
                }
                end = Data.Index;
            }

            // Skip whitespace
            Data.SkipWhiteSpace();

            // Must end with ')'
            if (S.current != ')')
            {
                Error = "Unclosed type literal";
                return (-1, -1);
            }

            return (start, end);
        }

        /// <summary>
        /// Check last <see cref="Stacked"/> on the stack according to the rules.
        /// <br>It also validates <paramref name="v"/> and can mutate that value as well</br>
        /// </summary>
        /// <returns><see langword="true"/> if the state is valid, otherwise you should yield back to top.</returns>
        private bool UpdateState(ref Value v)
        {
            bool hasStacked = S.HasStackValues;
            if (!hasStacked)
            {
                S.expect |= Expect.NotType; // Any type of entry 
                return true;
            }

            if (v.i.type == SJType.Comment)
            {
                return true;
            }

            ref State.Stacked cs = ref S.stack.PeekValue();
            switch (cs.type)
            {
                case SJType.Object:
                    if ((S.expect & Expect.Key) == Expect.Key)
                    {
                        if (v.i.type != SJType.String)
                        {
                            if (allowOtherKeyTypes)
                            {
                                switch (v.i.type)
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
                        v.i.prevType = v.i.type;
                        v.i.type = SJType.Key;
                        S.lastKey = v.i;

                        S.expect &= ~Expect.Key;
                        S.expect |= Expect.Colon;
                    }
                    else
                    {
                        cs.count++;
                        S.expect &= ~Expect.Colon;
                        S.expect |= Expect.Comma | Expect.Key;
                    }
                    break;
                case SJType.Array:
                    cs.count++;
                    S.expect |= Expect.Comma;
                    break;
                default:
                    Error = $"Invalid S.current state type {cs.type}";
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Throws the S.currently stored error. It does not check whether if an error is available.
        /// </summary>
        /// <exception cref="ReaderException"></exception>
        public void ThrowError()
        {
            throw new ReaderException(this);
        }
        private void CheckDisposed()
        {
            if (disposedValue) throw new InvalidOperationException("Attempt to use a disposed SJReader.");
        }
        public void CheckVersion()
        {
            if (Data.Version != LastDataVersion) Reset();
        }

        // Base sj.h
        /// <summary>
        /// Top level reader for a single non-recursive lexable generic value.
        /// </summary>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public Value Read(ReadOnlySpan<char> data)
        {
            CheckDisposed();
            CheckVersion();

            var result = new Value(data);
            S.ended = HasError;
        _Top:
            if (HasError)
            {
                result.i.type = SJType.Error;
                result.i.start = result.i.end = 0;
                result.i.depth = 0;
                return result;
            }
            if (maxDepth > 0 && S.Depth > maxDepth)
            {
                Error = $"Exceeded maximum depth {maxDepth}";
                goto _Top;
            }
            if (!Data.MoveNext())
            {
                // Reaching end of document is no longer an error, but it should return an empty "error" value.
                if (S.readValueCount <= 0)
                {
                    Error = "Empty data"; // Except when there is no values
                    goto _Top;
                }

                result.i.type = SJType.Error;
                result.i.start = result.i.end = Data.Index;
                result.i.depth = 0;
                S.ended = true;
                if (S.Depth > 0)
                {
                    Error = S.stack.PeekValue().type switch
                    {
                        SJType.Object => "Unclosed object",
                        SJType.Array => "Unclosed array",
                        _ => "Unclosed value with invalid stack type"
                    };
                }
                return result;
            }

            char ch = (char)S.current;
            // Skip : White Space
            if (char.IsWhiteSpace(ch))
            {
                // This may end us up in a place where the last token has been consumed.
                // So return whether if we can move next or not alongside the positions moved.
                // Note : This may cause whitespace only documents to be considered valid
                //        This isn't a quirk I want, so count the amount of values captured.
                if (!Data.TrySkipWhiteSpace())
                {
                    goto _Top;
                }
                ch = (char)S.current;
            }
            result.i.start = Data.Index;

            // Validate / Parse : Comments
            if (ch == '/')
            {
                if (!allowComments)
                {
                    Error = "Unexpected '/', enable comments through 'allowComments'";
                    goto _Top;
                }

                result.i.type = SJType.Comment;
                (result.i.start, result.i.end) = GetCommentRange(out result.i.variant);
                if (HasError || result.i.start < 0 || result.i.end <= 0)
                {
                    goto _Top;
                }
            }
            // Parse: Type (literal or something else). Treated sort of like a comment.
            else if (ch == '(')
            {
                if (!allowTypes)
                {
                    Error = "Unexpected '(', enable types through 'allowTypes'";
                    goto _Top;
                }
                if (S.notExpectingType)
                {
                    Error = "Was not expecting a duplicate type definition";
                    goto _Top;
                }

                result.i.type = SJType.Type;
                (result.i.start, result.i.end) = GetTypeRange(out result.i.variant);
                S.notExpectingType = true;
                S.lastType = result;
                if (HasError || result.i.start < 0 || result.i.end <= 0)
                {
                    goto _Top;
                }
            }
            // Validate : Object == KV Colon, Array == Comma
            else if (ch == ':' || ch == ',')
            {
                if (S.HasStackValues)
                {
                    ref var st = ref S.stack.PeekValue();
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
                        S.notExpectingType = false; // Type entry can be written after ':' for the value type.
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
            else if ((S.stack.PeekValue().expect & Expect.Colon) == Expect.Colon)
            {
                // Colons are strict, they are seperator for key/value
                Error = $"Unexpected '{Tk(ch)}', expected ':'";
                goto _Top;
            }
            // Parse : Array | Object End (must override comma)
            else if (ch == '}' || ch == ']')
            {
                result.i.type = SJType.End;
                // Validate last state and update "topmost S.current state"
                var st = S.stack.PopValue();
                if (S.Depth < 0 || st.type != ((ch == '}') ? SJType.Object : SJType.Array))
                {
                    Error = (ch == '}') ? "Stray '}'" : "Stray ']'";
                    goto _Top;
                }
                if (st.count > 0)
                {
                    if ((st.expect & Expect.Comma) != Expect.Comma && !allowTrailingComma)
                    {
                        Error = "Trailing comma character for the last value before end";
                        goto _Top;
                    }
                }
                result.i.end = Data.Index;
            }
            // Validate: Commas
            else if ((this.S.stack.PeekValue().expect & Expect.Comma) == Expect.Comma)
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
                result.i.type = ch == '{' ? SJType.Object : SJType.Array;
                var prevHandle = S.stack.Peek();
                S.stack.PushValue(new State.Stacked(result.i.type, ch == '{' ? Expect.Key : Expect.None));
                prevHandle = prevHandle.Renew(ref S.stack);

                result.i.depth = (ushort)S.Depth;
                S.notExpectingType = false; // Technically a value entry, just does not update last state.

                if (!UpdateState(ref prevHandle.Value, ref result))
                {
                    goto _Top;
                }
            }
            // Parse : Number (technically, hex and binary is supported if prefixed with 0x)
            else if (char.IsDigit(ch) || ch == '-')
            {
                // Finite automata? What "for"! heh
                result.i.type = SJType.Number;
                do
                {
                    ch = (char)Data.Peek();
                    if (!IsNumberContinuation(ch, out var foundOn))
                    {
                        break;
                    }

                    // Not super accurate, but will do.
                    // Theoretically we can check number literal validity this way,
                    // but whatever that's many more state to the larpmata.
                    // The hex and scientific notation can be confused, so this is checked.
                    switch (result.i.variant)
                    {
                        case SJTypeVariant.None:
                            switch (foundOn)
                            {
                                case SJTypeVariant.HexNumber:
                                    result.i.variant = SJTypeVariant.HexNumber;
                                    break;
                                case SJTypeVariant.ScientificNumber:
                                    if (result.i.start == Data.Index) goto case SJTypeVariant.HexNumber;
                                    result.i.variant = SJTypeVariant.ScientificNumber;
                                    break;

                                default:
                                    result.i.variant = foundOn;
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
                                    result.i.variant = SJTypeVariant.HexNumber;
                                    break;
                            }
                            break;
                    }
                }
                while (Data.MoveNext());
                result.i.end = S.current;

                if (!UpdateState(ref result))
                {
                    goto _Top;
                }
            }
            // Parse : String
            else if (ch == '"')
            {
                result.i.type = SJType.String;
                result.i.start = Data.Index + 1; // Skip '"'

                bool escape = false;
                while (true)
                {
                    if (!Data.TryNext(out ch))
                    {
                        Error = "Unclosed string";
                        goto _Top;
                    }
                    if (escape) escape = false;
                    else if (ch == '\\') escape = true;
                    else if (ch == '"') break;
                }
                result.i.end = Data.Index;

                if (!UpdateState(ref result))
                {
                    goto _Top;
                }
            }
            // Parse : null | true | false | any supported literal
            else
            {
                // what in the strcmp()
                switch (GetLiteralType())
                {
                    case SJConstants.Literal.True:
                    case SJConstants.Literal.False:
                        result.i.type = SJType.Bool;
                        break;
                    case SJConstants.Literal.Null:
                        result.i.type = SJType.Null;
                        break;

                    default:
                        Error = $"Unknown token '{Tk(ch)}' or truncated literal";
                        goto _Top;

                }

                // Position points to anything or it ended
                result.i.end = S.current;

                if (!UpdateState(ref result))
                {
                    goto _Top;
                }
            }

            // This part is only reached if a valid value is captured.
            S.readValueCount++;
            return result;
        }
        // [!] Do not assign any state value on any below method as it's being inferred on Read() and other state management methods.
        /// <summary>
        /// Moves <see cref="State.Depth"/> and <see cref="State.S.current"/> until a depth.
        /// </summary>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void DiscardUntil(int depth)
        {
            CheckVersion();
            ref var s = ref S;
            var value = Value.Null();
            while (!s.ended && s.Depth != depth && value.i.type != SJType.Error)
            {
                value = Read();
            }
        }
        /// <summary>
        /// Iterate <see cref="SJType.Array"/> typed <paramref name="value"/> with entry type info.
        /// </summary>
        /// <param name="type">Type of the entry. This is either <see cref="EntryType.Value"/> or <see cref="EntryType.None"/>.</param>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IterateArrayEntries(Value value, out Value result)
        {
            CheckVersion();
            if (value.i.type != SJType.Array)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.i.depth);
            result = Read();

            if (result.i.type == SJType.Comment)
            {
                return true;
            }

            switch (result.i.type)
            {
                case SJType.Error:
                case SJType.End:
                    result = Value.Error(this);
                    return false;

                default:
                    return true;
            }
        }
        /// <summary>
        /// Iterate <see cref="SJType.Array"/> typed <paramref name="value"/>.
        /// </summary>
        /// <remarks>
        /// <b>This skips any <see cref="SJType.Comment"/> or <see cref="SJType.Type"/> blocks. If you want to capture those 
        /// while reading an array, use <see cref="IterateArrayEntries(Value, out Value)"/> instead.</b>
        /// </remarks>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IterateArray(Value value, out Value result)
        {
            while (IterateArrayEntries(value, out var v))
            {
                switch (v.i.type)
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
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IterateObjectEntries(Value value, out Value result)
        {
            CheckVersion();
            if (value.i.type != SJType.Object)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.i.depth);
            result = Read();

            switch (result.i.type)
            {
                case SJType.Error:
                case SJType.End:
                    result = Value.Error(this);
                    return false;

                default:
                    break;
            }
            return true;
        }
        /// <summary>
        /// Iterate <see cref="SJType.Object"/> typed <paramref name="object"/> value.
        /// </summary>
        /// <remarks>
        /// <b>This skips any <see cref="SJType.Comment"/> or <see cref="SJType.Type"/> blocks. If you want to capture those 
        /// while reading an object, use <see cref="IterateArrayEntries(Value, out Value)"/> instead.</b>
        /// <br>Calling <see cref="IterateObject(Value, out Value, out Value)"/> with an existing key 
        /// value will use that key value and poll for it's value, unless the value for that key was already read, 
        /// which in this case, this method will return the next key/value.
        /// Otherwise, this method will return the previous key/next unread value (if exists)</br>
        /// </remarks>
        /// <returns>Whether if more data is available to read from <paramref name="object"/>.</returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IterateObject(Value @object, out Value key, out Value value)
        {
            bool gotKey = false; key = default;
            while (IterateObjectEntries(@object, out Value result))
            {
                if (HasError) goto _Fail;

                switch (result.i.type)
                {
                    case SJType.Error:
                    case SJType.End:
                        goto _Fail;

                    // Ignored
                    case SJType.Comment:
                    case SJType.Type:
                        continue;

                    case SJType.Key:
                        key = result;
                        gotKey = true;
                        continue;

                    default:
                        if (!gotKey) key = S.lastKey;
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
        /// <param name="values">Count of values seen. If the <paramref name="v"/> type is <see cref="SJType.Object"/>, this is equal to the keys count.</param>
        /// <param name="other">Count of other entries (comments) seen.</param>
        /// <returns>
        /// Whether if the peek was successful.
        /// If this returns <see langword="false"/> then either the <paramref name="v"/>
        /// is erroreneous or <paramref name="expectCount"/> is exceeded.
        /// </returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool PeekEntryCount(Value v, out int values, out int other, int expectCount = -1, bool popStateEvenIfError = false)
        {
            CheckDisposed();
            CheckVersion();

            switch (v.i.type)
            {
                case SJType.Array:
                case SJType.Object:
                    break;
                default:
                    values = other = -1;
                    return false;
            }

            values = other = 0;

            PushState();
            bool result = true;
            try
            {
                DiscardUntil(v.i.depth);
                Value innerValue = Read();
                for (; innerValue.i.type != SJType.End; DiscardUntil(v.i.depth), innerValue = Read())
                {
                    switch (innerValue.i.type)
                    {
                        case SJType.Error:
                            return false;

                        case SJType.Type:
                        case SJType.Comment:
                            other++;
                            break;

                        case SJType.Key:
                            // could write expected count, but if SJ is functioning
                            // as intended this should not have any problems.
                            break;
                        default:
                            values++;
                            break;
                    }

                    if (expectCount >= 0 && (values > expectCount))
                    {
                        result = false;
                        break;
                    }
                }
            }
            finally
            {
                if (popStateEvenIfError || !HasError) PopState();
            }

            return result;
        }

        public static explicit operator bool(SJReader r) => !r.HasError;

        private SJValueStack<State> _stateStack = SJValueStack<State>.CreateWithMaxSize(SJConstants.MaxDepth);
        public int StateCount => _stateStack.Count;
        /// <summary>
        /// Pushes a loadable stacked state.
        /// </summary>
        /// <returns>
        /// The index of the pushed state.
        /// For any other thing with it's own state managing a SJReader, this can be used to write it into it's own index like:
        /// <br><c>result := <see cref="PushState"/>; myStack[result] = otherState;</c></br>
        /// </returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public int PushState()
        {
            CheckDisposed();

            if (_StData == null)
            {
                Error = "Data source does not support stacking, it must implement stackability interface.";
                return -1;
            }

            // Currently, the state stack will reset and a clean state will be pushed instead.
            // Although the CheckVersion would be only reached in cases where the data within the data interface is modified.
            // Immutability would be "great", but then there is the problem of being able to
            // reuse the data, which is what I want rather than to allocate objects every read.
            CheckVersion();

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
        /// For any other thing with it's own state managing a SJReader, this can be used to read from it's own index like:
        /// <br><c>result := <see cref="PopState"/>; otherState = myStack[result];</c></br>
        /// </returns>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public int PopState()
        {
            CheckDisposed();

            if (_StData == null)
            {
                Error = "Data source does not support stacking, it must implement stackability interface.";
                return -1;
            }

            CheckVersion();

            if (_stateStack.Count <= 0) return -1;
            _stateStack.PopValue().CopyTo(ref S);

            int pos = _stateStack.Count;
            _StData.PopState();
            return pos;
        }
        /// <summary>
        /// Starts a <see cref="SJWriter"/> transaction, which will reset if this state is disposed.
        /// </summary>
        /// <exception cref="ReaderException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public StateStackScope UseState(bool popIfError = false) => new StateStackScope(this, popIfError);

        /// <summary>
        /// Resets the reader to the default state. This also removes any stacked state if present.
        /// </summary>
        /// <remarks>
        /// <br><b>This shouldn't discard loaded data and only reset the state.</b>
        /// (should have called it flush, but SJReader does not commit any data?)</br>
        /// </remarks>
        /// <exception cref="InvalidOperationException"></exception>
        public void Reset()
        {
            CheckDisposed();
            S.Reset();
            Data.Reset();
            LastDataVersion = Data.Version;
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
                }

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
#pragma warning restore IDE0057 // ***** does not support System.Range
