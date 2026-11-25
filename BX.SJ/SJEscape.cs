using System;
using System.Diagnostics;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Provides the basic Json string escaping algorithm.
    /// </summary>
    /// <remarks>
    /// It is designed for quote strings like <c>"Hello world \b\f\n\r\t, &lt;=\u001F (control chars), \\ and \"."</c>
    /// <br>Allows ascii only escapes like <c>"Merhaba d\\u00FCnya! \\uD83D\\uDFE2, \\u00A9\\uFE0F"</c></br>
    /// </remarks>
    public static class SJEscape
    {
        public delegate void AppendAction<in TSelf>(TSelf self, ReadOnlySpan<char> slice);

        private static readonly byte[] hex2IntTable;
        private enum EscapeTableAction : byte
        {
            None = 0,                  // No escape
            // ...                     // Anything as the target char value, within sbyte.MaxValue
            Self = sbyte.MaxValue + 1, // Escape as \{self}
            Hex                        // Escape as \u{4 Digit Codepoint}
        }
        private static readonly EscapeTableAction[] escapeTable;
        static SJEscape()
        {
            // Hex2IntTable
            const byte AsciiTableMax = sbyte.MaxValue + 1;
            hex2IntTable = new byte[AsciiTableMax];
            Array.Fill(hex2IntTable, byte.MaxValue); // Because 0 is a valid value, but anything >= 16 isn't.
            for (int i = '0'; i <= '9'; i++) hex2IntTable[i] = (byte)(i - '0');
            for (int i = 'a'; i <= 'f'; i++) hex2IntTable[i] = (byte)(10 + (i - 'a'));
            for (int i = 'A'; i <= 'f'; i++) hex2IntTable[i] = (byte)(10 + (i - 'A'));

            // EscapeTable
            escapeTable = new EscapeTableAction[AsciiTableMax];
            // JSON only cares escaping certain control ranges, not the unicode stuff
            for (int i = 0; i <= 0x1F; i++) escapeTable[i] = EscapeTableAction.Hex;
            // The other escapes
            escapeTable[(byte)'\\'] = EscapeTableAction.Self;
            escapeTable[(byte)'"'] = EscapeTableAction.Self;
            escapeTable[(byte)'\b'] = (EscapeTableAction)'b';
            escapeTable[(byte)'\f'] = (EscapeTableAction)'f';
            escapeTable[(byte)'\n'] = (EscapeTableAction)'n';
            escapeTable[(byte)'\r'] = (EscapeTableAction)'r';
            escapeTable[(byte)'\t'] = (EscapeTableAction)'t';

            // For now, Unescape doesn't seem to need table outside of the hex2int
            // Batch processing multiple chars (like 4) at once from a ROSpan<char> could be faster,
            // but it would make this code less comprehensible.
        }

        private static void To4DigitHex(char c, int index, ref Span<char> chars)
        {
            Debug.Assert((chars.Length - index) >= 4);
            const string Digits = "0123456789ABCDEF";
            for (int i = 0, j = 12; i < 4; i++, j -= 4)
            {
                chars[i + index] = Digits[((c >> j) & 0xF)];
            }
        }
        private static void To4DigitHex(char c, ref Span<char> chars) => To4DigitHex(c, 0, ref chars);
        private static void SbAppendSpan(StringBuilder sb, ReadOnlySpan<char> c) => sb.Append(c);

        /// <summary>
        /// Escapes <paramref name="content"/> according to JSON. 
        /// Does not surround <paramref name="content"/> with '"' character, but escapes for that.
        /// </summary>
        /// <param name="appendAction">Action called when a character is to be appended into the arbitrary buffer.</param>
        /// <param name="content">Content to escape, this mustn't have broken surrogate pairs.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <returns>Count of written characters.</returns>
        public static int Escape<TSelf>(TSelf self, AppendAction<TSelf> appendAction, ReadOnlySpan<char> content, bool asciiOnly = false)
        {
            if (appendAction is null)
            {
                throw new ArgumentNullException(nameof(appendAction));
            }

            // Span -> Span copying is much faster as it's just a
            // simple tight loop of the range + the appendable content as opposed to bunch of branching.
            // If it's only escapes, perf is identical to the char by char copy.
            int start = 0;
            int count = 0;
            Span<char> appendable = stackalloc char[6];
            Span<char> escape = appendable.Slice(0, 2);
            Span<char> hex = appendable.Slice(2);

            // https://www.ietf.org/rfc/rfc4627.txt
            int i;
            for (i = 0; i < content.Length; i++)
            {
                char cur = content[i];
                if (cur < escapeTable.Length)
                {
                    switch (escapeTable[cur])
                    {
                        case EscapeTableAction.None:
                            break;

                        default:
                            if (i > start)
                            {
                                appendAction(self, content.Slice(start, i));
                                count += i - start;
                            }
                            start = i + 1;

                            escape[0] = '\\';
                            escape[1] = (char)escapeTable[cur];
                            appendAction(self, escape);
                            count += 2;
                            continue;

                        case EscapeTableAction.Self:
                            if (i > start)
                            {
                                appendAction(self, content.Slice(start, i));
                                count += i - start;
                            }
                            start = i + 1;

                            escape[0] = '\\';
                            escape[1] = cur;
                            appendAction(self, escape);
                            count += 2;
                            continue;

                        case EscapeTableAction.Hex:
                            if (i > start)
                            {
                                appendAction(self, content.Slice(start, i));
                                count += i - start;
                            }
                            start = i + 1;

                            // write as : \uXXXX
                            escape[0] = '\\';
                            escape[1] = 'u';
                            To4DigitHex(cur, ref hex);
                            appendAction(self, appendable);
                            count += 6;
                            continue;
                    }
                }
                else if (asciiOnly)
                {
                    if (i > start)
                    {
                        appendAction(self, content.Slice(start, i));
                        count += i - start;
                    }
                    start = i + 1;

                    // write as : \uXXXX
                    escape[0] = '\\';
                    escape[1] = 'u';
                    To4DigitHex(cur, ref hex);
                    appendAction(self, appendable);
                    count += 6;
                    continue;
                }
            }

            // Any other slice
            if (i > start)
            {
                appendAction(self, content.Slice(start, i));
                count += i - start;
            }

            return count;
        }
        /// <inheritdoc cref="Escape{TSelf}(TSelf, Action{TSelf, char}, ReadOnlySpan{char}, bool)"/>
        public static string Escape(ReadOnlySpan<char> content, bool asciiOnly = false)
        {
            var sb = new StringBuilder(content.Length + (content.Length / 2)); // Content is "shorter"
            Escape(sb, SbAppendSpan, content, asciiOnly);
            return sb.ToString();
        }
        /// <inheritdoc cref="Escape{TSelf}(TSelf, Action{TSelf, char}, ReadOnlySpan{char}, bool)"/>
        public static string Escape(string content, bool asciiOnly = false) =>
            Escape(content.AsSpan(), asciiOnly);

        /// <summary>
        /// Unescapes <paramref name="content"/> with escapes.
        /// </summary>
        /// <remarks>
        /// This is more permissive on what it accepts, as it will accept invalid escapes like "\\k" and unescape it like "k".
        /// <br>If you want to reject invalid escapes, you can use <paramref name="allowInvalidEscapes"/> as false.</br>
        /// <br>By default, this ignores any control characters within the string.</br>
        /// </remarks>
        /// <param name="appendAction">Action called when a character is to be appended into the arbitrary buffer.</param>
        /// <param name="content">Content to unescape.</param>
        /// <returns>Count of written characters.</returns>
        public static int Unescape<TSelf>(TSelf self, AppendAction<TSelf> appendAction, ReadOnlySpan<char> content, bool allowInvalidEscapes = true)
        {
            if (appendAction is null)
            {
                throw new ArgumentNullException(nameof(appendAction));
            }

            Span<char> ueChars = stackalloc char[1];
            int start = 0;
            int count = 0;
            int i;
            // This method is "more lenient" about incorrect escape characters.
            for (i = 0; i < content.Length; i++)
            {
                char cur = content[i];
                if (cur == '\\')
                {
                    if (++i >= content.Length)
                    {
                        if (!allowInvalidEscapes) throw new ArgumentException("Invalid escape sequence, expected a control character at the end.", nameof(content));
                    }
                    else
                    {
                        cur = content[i];
                    }

                    if (start > i)
                    {
                        appendAction(self, content.Slice(start, i));
                        count += i - start;
                    }
                    start = i + 1;

                    switch (cur)
                    {
                        case '"':
                        case '\\':
                            ueChars[0] = cur;                            
                            break;
                        case 'b':
                            ueChars[0] = '\b';                            
                            break;
                        case 'f':
                            ueChars[0] = '\f';                            
                            break;
                        case 'n':
                            ueChars[0] = '\n';                            
                            break;
                        case 'r':
                            ueChars[0] = '\r';
                            break;
                        case 't':
                            ueChars[0] = '\t';
                            break;

                        case 'u':
                            {
                                // Strictly expect 4 digits
                                const int TargetDelta = 5; // length(u1234)
                                char result = '\0';
                                int hStart = i++;

                                while (i < content.Length && (i - hStart) < TargetDelta)
                                {
                                    cur = content[i];

                                    // character is within the range
                                    if (cur >= hex2IntTable.Length) break;

                                    // whether if digit is valid
                                    int digit = hex2IntTable[cur];
                                    if (digit > 0xF) break;

                                    result = (char)((result << 4) | digit);
                                    i++;
                                }

                                if ((i - hStart) == TargetDelta)
                                {
                                    // UTF-16 point
                                    ueChars[0] = (char)(result & char.MaxValue);

                                    // Go back to the previous character as it will be skipped otherwise
                                    i--;
                                }
                                // otherwise escape \u to u
                                else
                                {
                                    if (!allowInvalidEscapes) throw new ArgumentException("Invalid unicode escape sequence", nameof(content));

                                    // cur isn't guaranteed to be 'u'
                                    i = hStart;
                                    ueChars[0] = 'u';
                                }

                                break;
                            }

                        default:
                            if (!allowInvalidEscapes) throw new ArgumentException($"Bad control character '{cur}'", nameof(content));
                            ueChars[0] = cur;
                            break;
                    }

                    appendAction(self, ueChars);
                    count++;
                }
            }

            if (start > i)
            {
                appendAction(self, content.Slice(start, i));
                count += i - start;
            }

            return count;
        }
        /// <inheritdoc cref="Unescape{TSelf}(TSelf, Action{TSelf, char}, ReadOnlySpan{char}, bool)"/>
        public static string Unescape(ReadOnlySpan<char> content, bool allowInvalidEscapes = true)
        {
            var sb = new StringBuilder(content.Length); // Content is "longer"
            Unescape(sb, SbAppendSpan, content, allowInvalidEscapes);
            return sb.ToString();
        }
        /// <inheritdoc cref="Unescape{TSelf}(TSelf, Action{TSelf, char}, ReadOnlySpan{char}, bool)"/>
        /// <returns><see cref="string.Empty"/> if <paramref name="content"/> is null or empty, otherwise the escaped string.</returns>
        public static string Unescape(string content, bool allowInvalidEscapes = true)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            return Unescape(content.AsSpan(), allowInvalidEscapes);
        }
    }
}
