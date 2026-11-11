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
        None,

        // TODO : (3.1.x) More variant infers
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
    public abstract class SJReader
    {
        public const int DefaultStackSize = 8;

        /// <summary>
        /// Exception thrown on an error case while reading BXSJ.
        /// </summary>
        public class ReadException : Exception
        {
            public ReadException(SJReader reader) : base($"{reader.Error}{(reader.Location(out int line, out int col) ? $" | at line={line} col={col}" : "")}")
            { }
            public ReadException(string message, int line, int col) : base($"{message} | at line={line} col={col}")
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
            public readonly char At(int i) => reader.At(start + i);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice()
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.Slice(start, end - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.Slice(this.start + start, Length - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start, int length)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return reader.Slice(this.start + start, length);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> SliceFrom(ReadOnlySpan<char> chars)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return chars.Slice(start, end - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> SliceFrom(ReadOnlySpan<char> chars, int start)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return chars.Slice(this.start + start, Length - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> SliceFrom(ReadOnlySpan<char> chars, int start, int length)
            {
                if (Length < 0) throw new InvalidOperationException($"Slicing negative sized entries (type {type}) are not supported.");
                return chars.Slice(this.start + start, length);
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
                public SJType type;
                public int valueCount;
                public Expect expect;
                public readonly bool invalid;
                public static Stacked Invalid => new Stacked(true);

                private Stacked(bool invalid)
                {
                    type = SJType.Error;
                    valueCount = 0;
                    expect = Expect.None;
                    this.invalid = invalid;
                }
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
                    invalid = false;
                }

                public override readonly string ToString()
                {
                    if (invalid) return "invalid";
                    return $"type={type}, valueCount={valueCount}, expect={expect}";
                }
            }

            /// <summary>
            /// Error should be managed through <see cref="SJReader"/>.
            /// <br>It can be used to clear state, if needed..</br>
            /// </summary>
            [EditorBrowsable(EditorBrowsableState.Never)]
            public string _error;
            public int depth;   // "Current depth" tracker. Used with the so called "stack"
            public int current; // "Current index" tracker
            /// <summary>
            /// Last key read on a object key/value pair. If a non <see cref="SJType.Key"/>
            /// <see cref="Value"/> is returned while this is set, this value is the key that is for it.
            /// </summary>
            /// <remarks>
            /// <see cref="IterateObject(Value, out Value, out Value)"/> uses this value to keep it's key variable, it is also used as a failsafe value.
            /// </remarks>
            public Value lastEntryKey;
            /// <summary>
            /// Last type read on key or value. Does not apply to comments.
            /// </summary>
            public Value lastEntryType;
            /// <summary>
            /// Used as a global state to check whether if type is expected. Resets when a value or key is encountered.
            /// </summary>
            public bool notExpectingType;

            /// <summary>
            /// Whether if the reader reached end. Correct usage is as shown:
            /// <c>
            /// <br>for (var value = reader.Read(); !reader.S.Ended; value = reader.Read())</br>
            /// <br>{</br>
            /// <br>switch (value.type) // ... processing code, if type is comment next "Read" must be called again to proceed ... </br>
            /// <br>// Also, errors must be handled correctly. If <see cref="Read"/> is called while in error state, this field won't be touched.</br>
            /// <br>}</br>
            /// </c>
            /// </summary>
            public bool ended;

            // This can be shared among readers
            public readonly bool HasStacked => depth > 0;
            private static Stacked _stubState = Stacked.Invalid;
            internal Stacked[] _stacked;

            public int Push(Stacked s)
            {
                if ((_stacked?.Length - 1) < depth)
                {
                    System.Array.Resize(ref _stacked, _stacked.Length <= 0 ? DefaultStackSize : _stacked.Length * 2);
                }
                int pushIndex = depth++;
                _stacked[pushIndex] = s;
                return pushIndex;
            }
            public readonly ref Stacked Peek()
            {
                if (!HasStacked)
                {
                    _stubState = Stacked.Invalid;
                    return ref _stubState;
                }
                return ref _stacked[depth - 1];
            }
            public ref Stacked Pop()
            {
                // Pop should throw as it mutates..
                if (!HasStacked) throw new InvalidOperationException("Cannot peek state while there is no state.");
                return ref _stacked[--depth];
            }

            public static State Create()
            {
                return new State()
                {
                    _stacked = new Stacked[DefaultStackSize]
                };
            }
            public readonly State Copy()
            {
                var s = new State
                {
                    _stacked = new Stacked[_stacked.Length]
                };
                CopyTo(ref s);
                return s;
            }
            public readonly void CopyTo(ref State s)
            {
                // Mutable references that are reused should be preserved
                // (_error is not mutable)
                Stacked[] stacked = s._stacked;
                s = this;
                s._stacked = stacked;
                _stacked.CopyTo((Span<Stacked>)s._stacked);
            }

            public void Reset()
            {
                current = depth = 0;
                lastEntryKey = default;
                ended = false;
                _error = null;
            }
            public override readonly string ToString()
            {
                return $"current={current}, depth={depth}, top={{{Peek()}}}";
            }
        }

        // config
        /// <summary>
        /// Maximum read depth as generally recursion is used to read.
        /// <br>Set to zero to disable any depth checks.</br>
        /// </summary>
        public ushort maxDepth = 128;
        /// <summary>
        /// Maximum amount of stackable state.
        /// </summary>
        public int maxStateStack = 128;
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
        private State _S = State.Create();
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

        /// <summary>
        /// Length of the data to read.
        /// </summary>
        public abstract int Length { get; }
        /// <summary>
        /// Get a character at <paramref name="i"/>.
        /// <br><b>Note:</b> This can receive OOB reads, generally at <paramref name="i"/> == <see cref="Length"/></br>
        /// <br>In this case, it is necessary to handle that exact condition with '\0' / EOF character of your choice.</br>
        /// <br>If reading from a stream, this should maybe seek within the index buffer or go back to the "approximate position" or refer to the underlying "string buffer"</br>
        /// </summary>
        public abstract char At(int i);
        /// <summary>
        /// Create a literal "char" slice, starting from <paramref name="start"/> with <paramref name="length"/> on the data source.
        /// <br>Used for the lazy <see cref="Value"/>'s <see cref="Value.Slice"/>.</br>
        /// </summary>
        public abstract ReadOnlySpan<char> Slice(int start, int length);

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
        protected static string Tk(char c) => c == '\0' ? "\\0/0" : $"{c}/{(int)c}";

        /// <summary>
        /// Read a range of comment, starting from <see cref="State.current"/> for the reader data.
        /// <br>The returned range contains the content of the comment rather than the entire comment block 
        /// to be consistent to the other typed ranges..</br>
        /// <br>The comment range is trimmed, to capture the whole comment remove 
        /// <paramref name="postfixSize"/> from start and add <paramref name="prefixSize"/> to end.</br>
        /// </summary>
        /// <param name="prefixSize">Size of the comment prefix token. This is generally 2.</param>
        /// <param name="postfixSize">Size of the comment postfix token. This is generally 1 or 2.</param>
        /// <param name="variant">Variant of multi comment.</param>
        /// <returns>Start and ending range. If the block doesn't start with '/', 
        /// -1 is returned for start and end without setting error.</returns>
        protected (int start, int end) GetCommentRange(out int prefixSize, out int postfixSize, out SJTypeVariant variant)
        {
            // Finite automata? What for?
            ref var s = ref S;
            int start = s.current, end = s.current;
            prefixSize = postfixSize = 0;
            variant = SJTypeVariant.None;
            if (start < Length)
            {
                char ch = At(end);
                if (ch == '/') // Redundant on the only usage as it checks, but is fine.
                {
                    prefixSize++;
                    end = ++start;
                    char next = At(end);

                    switch (next)
                    {
                        case '/':
                            {
                                variant = SJTypeVariant.SingleComment;
                                prefixSize++;
                                end = ++start;
                                ch = At(end);

                                bool word = false;
                                int whitespace = 0;
                                while (ch != '\n')
                                {
                                    if (end >= Length)
                                    {
                                        // Ended with // .... <EOF
                                        return (start, end);
                                    }

                                    if (!word)
                                    {
                                        if (char.IsWhiteSpace(ch))
                                        {
                                            start++;
                                            prefixSize++;
                                        }
                                        else word = true;
                                    }
                                    else
                                    {
                                        if (char.IsWhiteSpace(ch)) whitespace++;
                                        else whitespace = 0;
                                    }

                                    ch = At(++end);
                                }

                                // exclude + include '\s*'. end stops in '\n'
                                // CRLF is stripped implicitly by being a part of whitespace.
                                end -= whitespace;
                                postfixSize = 1 + whitespace;
                                return (start, end);
                            }
                        case '*':
                            {
                                variant = SJTypeVariant.MultiComment;
                                prefixSize++;
                                end = ++start;
                                ch = At(end);

                                bool word = false;
                                int whitespace = 0;
                                while (!(ch == '*' && next == '/'))
                                {
                                    if (end >= Length)
                                    {
                                        Error = $"Expected */, got '{Tk(ch)}+{Tk(next)}'";
                                        return (-1, -1);
                                    }

                                    if (!word)
                                    {
                                        if (char.IsWhiteSpace(ch))
                                        {
                                            start++;
                                            prefixSize++;
                                        }
                                        else word = true;
                                    }
                                    else
                                    {
                                        if (char.IsWhiteSpace(ch)) whitespace++;
                                        else whitespace = 0;
                                    }

                                    ch = At(++end);
                                    next = At(end + 1);
                                }

                                // exclude + include '*/'. end stops in '*/'
                                end -= whitespace;
                                postfixSize = 2 + whitespace;
                                return (start, end);
                            }
                        default:
                            Error = $"Unexpected comment token '{Tk(next)}', expected '/ or *' to start a comment.";
                            return (-1, -1);
                    }
                }
                else
                {
                    return (-1, -1);
                }
            }

            // Nothing to skip, but not an "error"
            return (-1, -1);
        }
        /// <summary>
        /// Read a range of type, starting from <see cref="State.current"/> for the reader data.
        /// </summary>
        /// <param name="i">Target position of the pointer.</param>
        /// <returns>Start and ending range. If the block doesn't start with '(', -1 is returned for start and end.</returns>
        protected (int start, int end) GetTypeRange(out int i, out SJTypeVariant variant)
        {
            int start; int end;
            i = S.current;
            variant = SJTypeVariant.None;

            if (i >= Length || At(i) != '(') return (-1, -1);
            i++;

            // Skip whitespace
            while (i < Length && char.IsWhiteSpace(At(i))) i++;

            // Check for string literal starting with '"'
            if (i < Length && At(i) == '"')
            {
                i++; // skip opening quote
                start = i;
                bool escaped = false;
                variant = SJTypeVariant.TypeString;

                while (i < Length)
                {
                    if (escaped)            escaped = false;
                    else if (At(i) == '\\') escaped = true;
                    else if (At(i) == '"')  break; 
                    i++;
                }

                if (i >= Length || At(i) != '"')
                {
                    Error = "Unclosed type literal string";
                    return (-1, -1);
                }

                end = i;
                i++; // skip closing quote
            }
            else
            {
                start = i;
                variant = SJTypeVariant.TypeLiteral;

                while (i < Length)
                {
                    if (!IsTypeLiteralContinuation(At(i))) break;
                    i++;
                }

                if (i == start)
                {
                    Error = "Empty type literal";
                    return (-1, -1);
                }
                end = i;
            }

            // Skip whitespace
            while (i < Length && char.IsWhiteSpace(At(i))) i++;

            // Must end with ')'
            if (i >= Length || At(i) != ')')
            {
                Error = "Unclosed type literal";
                return (-1, -1);
            }
            i++; // skip ')'

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
        protected bool UpdateEnding()
        {
            ref var s = ref S;
            if (s.depth < 0)
            {
                return false;
            }

            // assert EOF or only whitespace if depth is zero.
            for (; s.depth == 0 && s.current < Length; s.current++)
            {
                char ch = At(s.current);

                if (allowComments && ch == '/')
                {
                    // Yield for parsing the comment line as current points to == '/',
                    // as it's "not EOF" and "more data available"
                    return true;
                }
                else if (!char.IsWhiteSpace(ch))
                {
                    Error = $"Unexpected '{Tk(ch)}' after end of document";
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
        protected virtual bool UpdateState(ref State.Stacked cs, ref Value v)
        {
            if (cs.invalid)
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
        protected bool UpdateLastState(ref Value v)
        {
            bool hasStacked = S.HasStacked;
            S.notExpectingType = !hasStacked; // Expect only one type if not a stacked value entry.
            if (!hasStacked)
            {
                return true;
            }

            return UpdateState(ref S.Peek(), ref v);
        }

        /// <summary>
        /// Throws the currently stored error. It does not check whether if an error is available.
        /// </summary>
        /// <exception cref="ReadException"></exception>
        public virtual void ThrowError()
        {
            throw new ReadException(this);
        }

        // Base sj.h
        /// <summary>
        /// Top level reader for a single non-recursive lexable generic value.
        /// </summary>
        public virtual Value Read()
        {
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
            if (Length <= 0)
            {
                Error = "Empty data";
                goto _Top;
            }
            if (s.current >= Length)
            {
                // Reaching end of document is no longer an error, but it should return an empty "error" value.
                result.type = SJType.Error;
                result.start = result.end = s.current;
                result.objectDepth = 0;
                s.ended = true;
                if (s.depth > 0)
                {
                    Error = s.Peek().type switch
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

            result.start = s.current;
            char ch = At(s.current);
            // Skip : White Space
            if (char.IsWhiteSpace(ch))
            {
                s.current++;
                goto _Top;
            }
            // Validate / Parse : Comments
            else if (allowComments && ch == '/')
            {
                result.type = SJType.Comment;
                (result.start, result.end) = GetCommentRange(out int _, out int postfix, out result.variant);
                if (string.IsNullOrEmpty(Error) && result.start >= 0 && result.end > 0)
                {
                    s.current = result.end + postfix;
                    return result;
                }
                goto _Top;
            }
            // Parse: Type (literal or something else). Treated sort of like a comment.
            else if (allowTypes && ch == '(')
            {
                if (s.notExpectingType)
                {
                    Error = "Was not expecting a duplicate type definition";
                    goto _Top;
                }

                result.type = SJType.Type;
                (result.start, result.end) = GetTypeRange(out s.current, out result.variant);
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
                if (s.HasStacked)
                {
                    ref var st = ref s.Peek();
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

                s.current++;
                goto _Top;
            }
            else if ((s.Peek().expect & Expect.Colon) == Expect.Colon)
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
                var st = s.Pop();
                if (s.depth < 0 || st.type != ((ch == '}') ? SJType.Object : SJType.Array))
                {
                    Error = (ch == '}') ? "Stray '}'" : "Stray ']'";
                    goto _Top;
                }
                if (st.valueCount > 0)
                {
                    if ((st.expect & Expect.Comma) != Expect.Comma)
                    {
                        if (!allowTrailingComma)
                        {
                            Error = "Trailing comma character for the last value before end";
                            goto _Top;
                        }
                        else
                        {
                            // Set state to what is normal
                            st.expect |= Expect.Comma;
                        }
                    }
                }

                result.end = ++s.current;

                // UpdateState here is not needed because the upper state will be checked later
                if (!UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Validate: Commas
            else if ((S.Peek().expect & Expect.Comma) == Expect.Comma)
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
                ref var prev = ref s.Peek();
                s.Push(new State.Stacked(result.type, ch == '{' ? Expect.Key : Expect.None));

                result.objectDepth = (ushort)s.depth;
                s.current++;
                s.notExpectingType = false; // Technically a value entry, just does not update last state.

                if (!UpdateState(ref prev, ref result))
                {
                    goto _Top;
                }
            }
            // Parse : Number (technically, hex and binary is supported if prefixed with 0x)
            else if (char.IsDigit(ch) || ch == '-')
            {
                // Finite automata? What "for"
                result.type = SJType.Number;
                for (ch = At(s.current);
                    s.current < Length && IsNumberContinuation(ch, out var foundOn);
                    s.current++, ch = At(s.current)
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
                                    if (result.start == s.current) goto case SJTypeVariant.HexNumber;
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
                result.end = s.current;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : String
            else if (ch == '"')
            {
                ch = At(++s.current);
                result.type = SJType.String;
                result.start = s.current;

                while (true)
                {
                    if (s.current >= Length)
                    {
                        Error = "Unclosed string";
                        goto _Top;
                    }
                    // Do have to handle the current escape here
                    if (ch == '"' && (s.current > 0 && At(s.current - 1) != '\\'))
                    {
                        break;
                    }

                    if (ch == '\\' || s.current < Length)
                    {
                        ch = At(++s.current);
                    }
                }
                result.end = s.current++;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : null | true | false 
            else if (ch == 'n' || ch == 't' || ch == 'f')
            {
                result.type = (ch == 'n') ? SJType.Null : SJType.Bool;
                int remaining = Length - s.current;
                if (Slice(s.current, Math.Min(remaining, 4)).Equals("null", StringComparison.Ordinal))
                {
                    s.current += 4;
                }
                else if (Slice(s.current, Math.Min(remaining, 4)).Equals("true", StringComparison.Ordinal))
                {
                    s.current += 4;
                }
                else if (Slice(s.current, Math.Min(remaining, 5)).Equals("false", StringComparison.Ordinal))
                {
                    s.current += 5;
                }
                else
                {
                    Error = $"Unknown token '{Tk(ch)}' or truncated literal";
                    goto _Top;
                }
                result.end = s.current;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Fail
            else
            {
                Error = $"Unknown token '{Tk(ch)}'";
                goto _Top;
            }

            return result;
        }
        /// <summary>
        /// Moves <see cref="s.depth"/> and <see cref="s.current"/> until a depth.
        /// </summary>
        protected void DiscardUntil(int depth)
        {
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
            if (value.type != SJType.Array)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.objectDepth);
            result = Read();
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
                    goto default;
                default:
                    S.Peek().valueCount++;
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
            if (value.type != SJType.Object)
            {
                result = Value.Error(this);
                return false;
            }

            DiscardUntil(value.objectDepth);
            result = Read();
            switch (result.type)
            {
                case SJType.Error:
                case SJType.End:
                    result = Value.Error(this);
                    return false;

                case SJType.Type:
                    S.lastEntryType = result;
                    goto default;
                case SJType.Key:
                    S.lastEntryKey = result;
                    goto default;
                default:
                    S.Peek().valueCount++;
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

            // For this instance since we aren't going to traverse towards inside explicitly and
            // only change the current depth's state object, it should be safe to save / load state like this..
            ref var s = ref S;
            int prevCurrent = s.current;
            int prevDepth = s.depth;
            var prevState = s.Peek();

            // get a ref to the current state, before starting
            ref var currentState = ref s.Peek();

            DiscardUntil(v.objectDepth);
            Value innerValue = Read();
            bool result = true;
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

            s.current = prevCurrent;
            s.depth = prevDepth;
            currentState = prevState;
            return result;
        }

        protected State[] _stateStack = new State[DefaultStackSize];
        protected int _stateStackCount = 0;
        public int StackedStateCount => _stateStackCount;
        protected virtual void ResizeStateStack(int size)
        {
            System.Array.Resize(ref _stateStack, size);
        }
        /// <summary>
        /// Pushes a loadable stacked state.
        /// </summary>
        /// <returns>
        /// The index of the pushed state.
        /// For any other SJReader with it's own state, this can be used to write it into it's own index like:
        /// <br><c>result := <see cref="PushState"/>; myStack[result] = otherState;</c></br>
        /// </returns>
        public virtual int PushState()
        {
            if (maxStateStack > 0 && _stateStackCount >= maxStateStack) return -1;
            if (_stateStackCount >= _stateStack?.Length)
            {
                int size = Math.Max((_stateStack?.Length ?? 0) * 2, DefaultStackSize);
                if (maxStateStack > 0) size = Math.Min(maxStateStack, size);
                ResizeStateStack(size);
            }

            // Copy the data to the entry in stateStack
            ref var next = ref _stateStack[_stateStackCount];
            if ((next._stacked?.Length ?? -1) < S._stacked?.Length)
            {
                next._stacked = new State.Stacked[Math.Max(S._stacked?.Length ?? 0, DefaultStackSize)];
            }
            S.CopyTo(ref next);
            return _stateStackCount++;
        }
        /// <summary>
        /// Loads a stacked state.
        /// </summary>
        /// <returns>
        /// The index of the loaded state.
        /// For any other SJReader with it's own state, this can be used to read from it's own index like:
        /// <br><c>result := <see cref="PopState"/>; otherState = myStack[result];</c></br>
        /// </returns>
        public virtual int PopState()
        {
            if (_stateStackCount <= 0) return -1;

            // Should set by value to copy it instead of setting the reference.
            // No need to copy the existing array entries, it can be discarded.
            // ↑ Although this will allocate garbage instead of reusing the memory, so just copy it..
            ref var prev = ref _stateStack[--_stateStackCount];
            if ((S._stacked?.Length ?? -1) < prev._stacked?.Length)
            {
                S._stacked = new State.Stacked[Math.Max(prev._stacked?.Length ?? 0, DefaultStackSize)];
            }
            prev.CopyTo(ref S);
            return _stateStackCount;
        }
        /// <summary>
        /// Starts a <see cref="SJWriter"/> transaction, which will reset if this state is disposed.
        /// </summary>
        public StateScope UseState(bool popIfError = false) => new StateScope(this, popIfError);

        /// <summary>
        /// Get the current readout location.
        /// </summary>
        public virtual bool Location(out int line, out int column)
        {
            if (Length <= 0)
            {
                line = column = 0;
                return false;
            }

            int c = S.current;
            line = column = 1;
            for (int i = 0; i < c; i++)
            {
                char cur = At(i);
                if (cur == '\n')
                {
                    if ((i + 1) < Length)
                    {
                        column = 1;
                        line++;
                    }
                }
                else
                {
                    column++;
                }
            }

            return true;
        }
        /// <summary>
        /// Resets the reader to the default state. This also removes any stacked state if present.
        /// </summary>
        /// <remarks>
        /// <br><b>This shouldn't discard loaded data and only reset the state.</b>
        /// (should have called it flush, but SJReader does not commit any data?)</br>
        /// </remarks>
        public virtual void Reset()
        {
            S.Reset();
            _stateStackCount = 0;
        }

        public override string ToString()
        {
            return $"[SJReader] state=({S}), error={Error}";
        }
    }
}
