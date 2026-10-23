using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Inferred type of the read JSON.
    /// </summary>
    public enum SJType
    {
        Error,

        Number,
        Key,
        String,
        Bool,
        Null,

        Object,
        Array,
        End,

        Comment,
    }

    /// <summary>
    /// Base class for <i>most</i> JSON readers.
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// using BX.SJ;
    /// using System;
    /// 
    /// // An example class would look like this:
    /// // (In fact, this is just SJStringReader)
    /// public sealed class SJExampleReader : SJReader
    /// {
    ///     private string _Data;
    ///     public string Data
    ///     {
    ///         [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ///         get => _Data;
    ///         set
    ///         {
    ///             if (string.Equals(value, _Data, StringComparison.Ordinal))
    ///             {
    ///                 return;
    ///             }
    /// 
    ///             _Data = value;
    ///             Reset();
    ///         }
    ///     }
    ///
    ///     public SJExampleReader()
    ///     { }
    ///     public SJExampleReader(string data)
    ///     {
    ///         _Data = data;
    ///     }
    /// 
    ///     // This is used as a limiter.
    ///     public override int Length => _Data?.Length ?? 0;
    ///     // Because I ported the pointer stuff as-is, some parts of the parser may read off by one.
    ///     // It is recommended to do a bound check and return EOF of your choice
    ///     public override char At(int i) => i >= 0 && i < Length ? _Data[i] : '\0';
    ///     // Because each value slice is evaluated lazily, the data ranges must persist and should be representable easily as a range (making arbitrary Streams much harder)
    ///     // Note that there isn't much of a reason to do this, if you read the data as soon as it's received from the SJReader.
    ///     public override ReadOnlySpan<char> Slice(int start, int length) => string.IsNullOrEmpty(_Data) ? ReadOnlySpan<char>.Empty : _Data.AsSpan(start, length);
    /// }
    /// ]]>
    /// </example>
    public abstract class SJReader
    {
        // TODO:
        // * Generic class (with the character unit type), or to avoid boxing,
        //   byte version that does ASCII (ignores UTF-8, but should work as all JSON tokens are within ascii)

        /// <summary>
        /// Exception thrown on an error case while reading JSON.
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
        /// Value result from <see cref="Read"/>.
        /// </summary>
        public struct Value
        {
            public readonly SJReader reader;
            public SJType type;
            public int start, end;
            /// <summary>
            /// Object typed value depth of this value. This is used for <see cref="DiscardUntil(int)"/>.
            /// <br><b>Only applicable when type is <see cref="SJType.Object"/>, 
            /// <see cref="SJType.Array"/> or <see cref="SJType.End"/>!</b></br>
            /// </summary>
            public int objectDepth;
            /// <summary>
            /// <see cref="SJReader.depth"/> of the <see cref="reader"/> while this Value was being created.
            /// </summary>
            public readonly int depth;

            public Value(SJReader reader)
            {
                this.reader = reader;
                type = SJType.Error;
                start = end = 0;
                objectDepth = 0;
                depth = reader?.depth ?? 0;
            }
            public Value(SJReader reader, SJType type, int start, int end, int objectDepth, int depth)
            {
                this.reader = reader;
                this.type = type;
                this.start = start;
                this.end = end;
                this.objectDepth = objectDepth;
                this.depth = depth;
            }

            public static Value Error() => new Value(null, SJType.Error, -1, -1, -1, -1);
            public static Value Error(SJReader reader) => new Value(reader, SJType.Error, -1, -1, -1, reader?.depth ?? -1);
            public static Value Null() => new Value(null, SJType.Null, -1, -1, -1, -1);
            public static Value Null(SJReader reader) => new Value(reader, SJType.Null, -1, -1, -1, reader?.depth ?? -1);

            public readonly int Length
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => end - start;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly char At(int i) => reader.At(start + i);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice() => reader.Slice(start, end - start);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start)
            {
                Debug.Assert(start <= Length);
                return reader.Slice(this.start + start, Length - start);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<char> Slice(int start, int length)
            {
                Debug.Assert((start + length) <= Length);
                return reader.Slice(this.start + start, length);
            }

            public override readonly string ToString()
            {
                return $"[SJReader.Value] type:{type}, start:{start}, end:{end}, depth:{objectDepth}";
            }
        }

        [Flags]
        public enum Expect { None = 0, Comma = 1 << 0, Key = 1 << 1, Colon = 1 << 2 }
        public struct State
        {
            public SJType type;
            public int valueCount;
            public Expect expect;
            public readonly bool invalid;
            public static State Invalid => new State(true);

            private State(bool invalid)
            {
                type = SJType.Error;
                valueCount = 0;
                expect = Expect.None;
                this.invalid = invalid;
            }
            public State(SJType type, Expect expect)
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
        }

        // config
        /// <summary>
        /// Maximum read depth as generally recursion is used to read.
        /// </summary>
        public int maxDepth = 128;
        /// <summary>
        /// <br>Allows comments on JSON file : </br><br/>
        /// <br>// &lt;-- single line</br>
        /// <br>/* &lt;-- until end token --&gt; */</br>
        /// </summary>
        public bool allowComments = false;
        /// <summary>
        /// Allows primitive key types (<see cref="SJType.Number"/>, etc.)
        /// <br>Recursive key types such as <see cref="SJType.Object"/> are not supported.</br>
        /// </summary>
        public bool allowOtherKeyTypes = false;
        /// <summary>
        /// Allow trailing comma on array or objects.
        /// </summary>
        public bool allowTrailingComma = false;
        /// <summary>
        /// Captured JSON comments are skipped
        /// <br>(they are not pushed as JSON values from <see cref="Read"/> method)</br>
        /// <br><b>Caution:</b> If you set this <see langword="false"/>, the document must be 
        /// read differently compared to the basic method, as EOF validation and other things 
        /// are done implicitly (eg: getting next value, because comments can be anywhere) depending on this value.
        /// </br>
        /// </summary>
        public bool captureComments = false;
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
        public int depth = 0;   // "Current depth" tracker. Used with the so called "stack"
        public int current = 0; // "Current index" tracker
        protected const int DefaultStackSize = 8;
        protected State[] _stateStack = new State[DefaultStackSize];
        private State _stubState = State.Invalid;
        public bool HasState => depth > 0;
        public virtual int PushState(State s)
        {
            if ((_stateStack?.Length - 1) < depth)
            {
                Array.Resize(ref _stateStack, _stateStack.Length <= 0 ? DefaultStackSize : _stateStack.Length * 2);
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
        /// <summary>
        /// Whether if the reader reached end. If you want to validate end of JSON document,
        /// you should do this if <see cref="captureComments"/> is <see langword="true"/>:
        /// <c>
        /// <br>for (var value = reader.Read(); !reader.Ended; value = reader.Read())</br>
        /// <br>{</br>
        /// <br>switch (value.type) // ... processing code, if type is comment next "Read" must be called again to proceed ... </br>
        /// <br>// Also, errors must be handled correctly. If <see cref="Read"/> is called while in error state, this field won't be touched.</br>
        /// <br>}</br>
        /// </c>
        /// </summary>
        public bool ended;

        /// <summary>
        /// Last key read on a object key/value pair. If a non <see cref="SJType.Key"/>
        /// <see cref="Value"/> is returned while this is set, this value is the key that is for it.
        /// </summary>
        public Value LastEntryKey { get; protected set; }
        protected string _Error = string.Empty;
        /// <summary>
        /// The error string detailing why the reader failed.
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
        /// <br>Used for the <see cref="Value"/>'s <see cref="Value.Slice"/></br>
        /// </summary>
        public abstract ReadOnlySpan<char> Slice(int start, int length);

        /// <summary>
        /// <see cref="SJType.Number"/> continuation check, Note that this reader 
        /// does no parsing or unescaping, so number validation must be done by external code.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static bool IsNumberContinuation(char c) => (c >= '0' && c <= '9') || c == 'e' || c == 'E' || c == '.' || c == '-' || c == '+';
        /// <summary>
        /// String inspector for token.
        /// <br>Outputs <paramref name="c"/> as <c>{c}/{(int)c}</c></br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static string Tk(char c) => c == '\0' ? "\\0/0" : $"{c}/{(int)c}";

        /// <summary>
        /// Validates and skips comment. This ignores the <see cref="allowComments"/> and parses the comment.
        /// <br><b>Note : </b> This method only skips and validates comments, if the character at <see cref="current"/> is '/'.
        /// It will fail on white space intentionally, but won't set error (because it isn't comment).
        /// You should check <c>At(current) == '/' &amp;&amp; <see cref="SkipComment"/></c></br>
        /// </summary>
        /// <returns>Whether if the comment block was valid.</returns>
        protected bool SkipComment()
        {
            (int start, int end) = GetCommentRange(true);
            if (start >= 0 && end >= 0)
            {
                current = end;
                return true;
            }

            return false;
        }
        /// <summary>
        /// Read a range of comments, starting from <see cref="current"/> for the reader data.
        /// </summary>
        /// <param name="setError">Set <see cref="Error"/> if the comment block is invalid.</param>
        /// <returns>Start and ending range. If the block isn't a comment, -1 is returned for both regions</returns>
        protected (int start, int end) GetCommentRange(bool setError)
        {
            int start = current, end = current;
            if (start < Length)
            {
                // I should validate the current character twice, for external uses.
                char ch = At(end);
                if (ch == '/')
                {
                    char next = At(++end);
                    if (next == '/')
                    {
                        while (ch != '\n')
                        {
                            if (end >= Length)
                            {
                                // Ended with // .... <EOF
                                return (start, end);
                            }

                            ch = At(++end);
                        }

                        // include '\n'
                        end += 1;
                        return (start, end);
                    }
                    else if (next == '*')
                    {
                        while (!(ch == '*' && next == '/'))
                        {
                            if (end >= Length)
                            {
                                if (setError)
                                {
                                    Error = $"Expected */, got '{Tk(ch)}+{Tk(next)}'";
                                }
                                return (-1, -1);
                            }

                            ch = At(++end);
                            next = At(end + 1);
                        }

                        // include '*/'
                        end += 2;
                        return (start, end);
                    }
                    else
                    {
                        if (setError)
                        {
                            Error = $"Unexpected comment token '{Tk(next)}', expected '/ or *'";
                        }
                        return (-1, -1);
                    }
                }
                else
                {
                    // Not skipping while on a comment block
                    return (-1, -1);
                }
            }

            // Nothing to skip, but not an "error"
            return (-1, -1);
        }
        /// <summary>
        /// Check remaining tokens after the JSON document is finished. (depth == 0)
        /// <br>Does parsing of comments if <see cref="allowComments"/></br>
        /// </summary>
        /// <returns>
        /// <see langword="false"/> if the ending has invalid characters. <see langword="true"/> if the document 
        /// ending is valid <b>or the code must yield, if <see cref="captureComments"/>.</b>
        /// </returns>
        protected bool UpdateEnding()
        {
            if (depth < 0)
            {
                return false;
            }

            // assert EOF or only whitespace if depth is zero.
            for (; depth == 0 && current < Length; current++)
            {
                char ch = At(current);

                if (allowComments && ch == '/')
                {
                    if (!captureComments)
                    {
                        if (!SkipComment())
                        {
                            Error = $"[end] {Error}";
                            return false;
                        }
                    }
                    else
                    {
                        // Yield for parsing the comment line as current points to == '/',
                        // as it's "not EOF" and "more data available"
                        return true;
                    }
                }
                else if (!char.IsWhiteSpace(ch))
                {
                    Error = $"Unexpected '{Tk(ch)}' after end of JSON document";
                    return false;
                }
            }

            // more data available.
            return true;
        }

        /// <summary>
        /// Check last <see cref="State"/> on the stack according to the rules.
        /// <br>It also validates <paramref name="v"/> and can mutate that value as well</br>
        /// </summary>
        /// <returns><see langword="true"/> if the state is valid, otherwise you should yield back to top.</returns>
        protected virtual bool UpdateState(ref State cs, ref Value v)
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
                        if (v.type != SJType.String && (!allowOtherKeyTypes || v.type == SJType.Object || v.type == SJType.Array))
                        {
                            Error = "Expected object key as valid Key type";
                            return false;
                        }
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
        /// If <b>no new state is pushed</b> to the stack on <see cref="Read"/>, you don't need to track state and can use this method instead.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="true"/> if no state exists, meaning there isn't any state to check.
        /// </remarks>
        /// <returns><see langword="true"/> if the state is valid, otherwise you should yield back to top.</returns>
        protected bool UpdateLastState(ref Value v)
        {
            if (!HasState)
            {
                return true;
            }

            return UpdateState(ref PeekState(), ref v);
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
        /// Top level JSON read, <see cref="SJType.Object"/> and <see cref="SJType.Array"/> requires special iteration.
        /// </summary>
        public virtual Value Read()
        {
            var result = new Value(this);
            ended = !string.IsNullOrEmpty(Error);
        _Top:
            if (!string.IsNullOrEmpty(Error))
            {
                result.type = SJType.Error;
                result.start = result.end = 0;
                result.objectDepth = -1;
                return result;
            }
            if (Length <= 0)
            {
                Error = "Empty data";
                goto _Top;
            }
            if (current >= Length)
            {
                // Reaching end of document is no longer an error, but it should return an empty "error" value.
                result.type = SJType.Error;
                result.start = result.end = current;
                result.objectDepth = -1;
                ended = true;
                if (depth > 0)
                {
                    Error = PeekState().type switch { SJType.Object => "Unclosed object", SJType.Array => "Unclosed array", _ => "Unclosed with invalid stack type" };
                }
                return result;
            }
            if (maxDepth > 0 && depth > maxDepth)
            {
                Error = $"Exceeded maximum depth {maxDepth}";
                goto _Top;
            }

            result.start = current;
            char ch = At(current);
            // Skip : White Space
            if (char.IsWhiteSpace(ch))
            {
                current++;
                goto _Top;
            }
            // Validate / Parse : Comments
            else if (allowComments && ch == '/')
            {
                if (!captureComments)
                {
                    SkipComment();
                }
                else
                {
                    result.type = SJType.Comment;
                    (result.start, result.end) = GetCommentRange(true);
                    if (string.IsNullOrEmpty(Error) && result.start >= 0 && result.end > 0)
                    {
                        current = result.end;
                        return result;
                    }
                }
                goto _Top;
            }
            // Validate : Object == KV Colon, Array == Comma
            else if (ch == ':' || ch == ',')
            {
                if (HasState)
                {
                    ref State s = ref PeekState();
                    if (ch == ':')
                    {
                        if (s.type != SJType.Object)
                        {
                            Error = "Unexpected ':' in top level array";
                            goto _Top;
                        }
                        else if ((s.expect & Expect.Colon) != Expect.Colon)
                        {
                            Error = "Unexpected ':', exceeding required";
                            goto _Top;
                        }

                        s.expect &= ~Expect.Colon;
                    }
                    else if (ch == ',')
                    {
                        // both Array and Object get commas
                        if ((s.expect & Expect.Comma) != Expect.Comma)
                        {
                            Error = "Unexpected ',', exceeding required";
                            goto _Top;
                        }

                        s.expect &= ~Expect.Comma;
                    }
                    else
                    {
                        // Unreachable
                        Error = $"ch '{Tk(ch)}' was somehow mutated on comma/colon check and no longer pleases any condition";
                        goto _Top;
                    }
                }
                else
                {
                    Error = "Unexpected ':' or ',' in top level nothing";
                }

                current++;
                goto _Top;
            }
            else if ((PeekState().expect & Expect.Colon) == Expect.Colon)
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
                State s = PopState();
                if (depth < 0 || s.type != ((ch == '}') ? SJType.Object : SJType.Array))
                {
                    Error = (ch == '}') ? "Stray '}'" : "Stray ']'";
                    goto _Top;
                }
                if (s.valueCount > 0 && (!allowTrailingComma && (s.expect & Expect.Comma) != Expect.Comma))
                {
                    Error = "Trailing comma character for the last value before end";
                    goto _Top;
                }

                ch = At(++current);
                result.end = current;

                // UpdateState here is not needed because the upper state will be checked later
                if (!UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Validate: Commas
            else if ((PeekState().expect & Expect.Comma) == Expect.Comma)
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
                ref State prev = ref PeekState();
                PushState(new State(result.type, ch == '{' ? Expect.Key : Expect.None));

                result.objectDepth = depth;
                ch = At(++current);

                if (!UpdateState(ref prev, ref result))
                {
                    goto _Top;
                }
            }
            // Parse : Number
            else if (char.IsDigit(ch) || ch == '-')
            {
                result.type = SJType.Number;
                while (current < Length && IsNumberContinuation(At(current)))
                {
                    ch = At(current++);
                }
                result.end = current;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : String
            else if (ch == '"')
            {
                ch = At(++current);
                result.type = SJType.String;
                result.start = current;

                while (true)
                {
                    if (current >= Length)
                    {
                        Error = "Unclosed string";
                        goto _Top;
                    }
                    // Do have to handle the current escape here
                    if (ch == '"' && (current > 0 && At(current - 1) != '\\'))
                    {
                        break;
                    }

                    if (ch == '\\' || current < Length)
                    {
                        ch = At(++current);
                    }
                }
                result.end = current++;

                if (!UpdateLastState(ref result) || !UpdateEnding())
                {
                    goto _Top;
                }
            }
            // Parse : null | true | false 
            else if (ch == 'n' || ch == 't' || ch == 'f')
            {
                result.type = (ch == 'n') ? SJType.Null : SJType.Bool;
                int remaining = Length - current;
                if (Slice(current, Math.Min(remaining, 4)).Equals("null", StringComparison.Ordinal))
                {
                    current += 4;
                }
                else if (Slice(current, Math.Min(remaining, 4)).Equals("true", StringComparison.Ordinal))
                {
                    current += 4;
                }
                else if (Slice(current, Math.Min(remaining, 5)).Equals("false", StringComparison.Ordinal))
                {
                    current += 5;
                }
                else
                {
                    Error = $"Unknown token '{Tk(ch)}' or truncated literal";
                    goto _Top;
                }
                result.end = current;

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
        /// Moves <see cref="depth"/> and <see cref="current"/> until a depth.
        /// </summary>
        protected void DiscardUntil(int depth)
        {
            var value = Value.Null();
            while (!ended && this.depth != depth && value.type != SJType.Error)
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
            ref State current = ref PeekState();
            result = Read();
            if (result.type == SJType.Comment)
            {
                return true;
            }

            current.valueCount++;
            if (result.type != SJType.Error && result.type != SJType.End)
            {
                return true;
            }
            return false;
        }
        /// <summary>
        /// Iterate <see cref="SJType.Array"/> typed <paramref name="value"/>.
        /// </summary>
        /// <remarks>
        /// <b>This skips <see cref="SJType.Comment"/> blocks (for compatibility) regardless 
        /// of <see cref="captureComments"/>' value. If you want to capture comments 
        /// while reading a JSON object, use lower level method of
        /// <see cref="IterateArrayEntries(Value, out EntryType, out Value)"/> instead.</b>
        /// </remarks>
        /// <returns>Whether if more data is available to read from <paramref name="value"/>.</returns>
        public bool IterateArray(Value value, out Value result)
        {
            while (IterateArrayEntries(value, out var v))
            {
                if (v.type != SJType.Comment)
                {
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
            if (result.type == SJType.Error || result.type == SJType.End)
            {
                result = Value.Error(this);
                return false;
            }
            if (result.type == SJType.Comment)
            {
                return true;
            }

            // SJType.Key is just promoted SJType.String
            if (result.type == SJType.Key)
            {
                LastEntryKey = result;
            }
            return true;
        }
        /// <summary>
        /// Iterate <see cref="SJType.Object"/> typed <paramref name="object"/> value.
        /// </summary>
        /// <remarks>
        /// <b>This skips <see cref="SJType.Comment"/> blocks regardless of <see cref="captureComments"/>' value.
        /// If you want to capture comments while reading a JSON object, use lower level method of
        /// <see cref="IterateObjectEntries(Value, out EntryType, out Value)"/> instead.</b>
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
                if (!string.IsNullOrEmpty(Error) || result.type == SJType.Error || result.type == SJType.End)
                {
                    key = value = Value.Error(this);
                    return false;
                }

                switch (result.type)
                {
                    case SJType.Comment:
                        continue;

                    case SJType.Key:
                        key = result;
                        gotKey = true;
                        continue;

                    default:
                        if (!gotKey)
                        {
                            key = LastEntryKey;
                        }
                        value = result;
                        return true;
                }
            }
            // did not find next Value, or IterateObject returned error.
            key = value = Value.Error(this);
            return false;
        }
        /// <summary>
        /// Peeks entry count. You do not have to iterate this method, 
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
            if (v.type != SJType.Array || v.type != SJType.Object)
            {
                keys = values = other = -1;
                return false;
            }

            keys = values = other = 0;

            // TODO : Save state, old SJ only had a "current" pointer, this one has stacks, so maybe copying the state stack is required?
            //        For this instance, since we aren't going to traverse towards inside explicitly, it is safe to save state like this.
            int prevCurrent = current;
            int prevDepth = depth;
            State prevState = PeekState();
            // get a ref to the current state, before starting
            ref State currentState = ref PeekState();

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

                if (keys > expectCount || values > expectCount)
                {
                    result = false;
                    break;
                }
            }

            current = prevCurrent;
            depth = prevDepth;
            currentState = prevState;
            return result;
        }

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

            line = column = 1;
            for (int i = 0; i < current; i++)
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
        /// Reset the read state.
        /// </summary>
        /// <remarks>
        /// <br><b>This shouldn't discard data and only reset the state.</b>(should have called it flush award)</br>
        /// </remarks>
        public virtual void Reset()
        {
            current = depth = 0;
            LastEntryKey = default;
            Error = null;
            ended = false;
        }

        public override string ToString()
        {
            return $"[SJReader] current={current}, depth={depth}, error={Error}";
        }
    }
}
