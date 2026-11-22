using System.Text;

namespace BX.SJ.Tests;

/// <summary>
/// Class used to recursively iterate the <see cref="SJReader"/> onto a <see cref="StringBuilder"/>.
/// </summary>
public static class ReaderTester
{
    public const int DefaultSbCapacity = 512;
    public const char BasicTypeSep = ':';
    public const char VariantTypeSep = '+';

    public static int RemoveStringsFrom(ref Span<char> s, params string[] strs)
    {
        // This is utterly overkill. And likely buggy. But it works from the limited testing.
        // > Mfw the testing suite needs tests
        int csSize = (strs.Length / 32) + 1;
        Span<int> candidates = csSize > 32 ? new int[csSize] : stackalloc int[csSize];
        static int GetSlice(int i) => i / 32;
        static bool GetCandidate(Span<int> cs, int i)
        {
            int index = GetSlice(i), bit = 1 << (i % 32);
            if (index < 0 || index >= cs.Length) return false;
            return (cs[index] & bit) != 0;
        }
        static void MarkCandidate(Span<int> cs, int i, bool v)
        {
            int index = GetSlice(i), bit = 1 << (i % 32);
            if (v) cs[index] |= bit;
            else cs[index] ^= bit; // Bit is a single flag
        }

        int matchLen = 0, fullMatchLen = 0;
        int marked = 0;
        const char ReplaceMarker = '\0';
        for (int i = 0, matching = 0, rfor = 0; i < s.Length; i++, matching = 0)
        {
            if (rfor > 0)
            {
                s[i] = ReplaceMarker;
                marked++;
                rfor--;
                continue;
            }

            // Loop could be further optimized by
            // * Creating a list of first chars for the first check (must be aware of the candidate positions)
            // * When there is a potential candidate, shift by TrailingZeroCount + target slice to
            //   jump to the candidate instead of checking the bits one by one
            // * No
            for (int j = 0; j < strs.Length; j++)
            {
                string str = strs[j];
                if (string.IsNullOrEmpty(str)) continue;
                switch (matchLen)
                {
                    case 0:
                        // On the start, all is candidate and there is no need to check the allowed candidates.
                        if (str[matchLen] == s[i])
                        {
                            if ((matchLen + 1) == str.Length) { fullMatchLen = matchLen + 1; }
                            else { MarkCandidate(candidates, j, true); matching++; }
                        }
                        break;
                    default:
                        if (GetCandidate(candidates, j))
                        {
                            if (matchLen < str.Length && str[matchLen] == s[i])
                            {
                                if ((matchLen + 1) == str.Length)
                                {
                                    fullMatchLen = matchLen + 1;
                                    MarkCandidate(candidates, j, false);
                                }
                                else
                                {
                                    matching++;
                                }
                            }
                            else
                            {
                                MarkCandidate(candidates, j, false);
                            }
                        }
                        break;
                }
            }
            switch (matching)
            {
                case 0:
                    switch (fullMatchLen)
                    {
                        case 0:
                            break;
                        // Nothing was matching, but there was a full match candidate.
                        default:
                            i -= (matchLen + 1);
                            rfor = fullMatchLen;
                            break;
                    }

                    fullMatchLen = matchLen = 0;
                    break;
                default:
                    matchLen++;
                    break;
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
    public static int AppendWithRemovedStrings<T>(T self, Action<T, ReadOnlySpan<char>> appendAction, ReadOnlySpan<char> s, params string[] strs)
    {
        // This is utterly overkill. And likely buggy. But it works from the limited testing.
        // > Mfw the testing suite needs tests
        int csSize = (strs.Length / 32) + 1;
        Span<int> candidates = csSize > 32 ? new int[csSize] : stackalloc int[csSize];
        static int GetSlice(int i) => i / 32;
        static bool GetCandidate(Span<int> cs, int i)
        {
            int index = GetSlice(i), bit = 1 << (i % 32);
            if (index < 0 || index >= cs.Length) return false;
            return (cs[index] & bit) != 0;
        }
        static void MarkCandidate(Span<int> cs, int i, bool v)
        {
            int index = GetSlice(i), bit = 1 << (i % 32);
            if (v) cs[index] |= bit;
            else cs[index] ^= bit; // Bit is a single flag
        }
        //static int GetFirstIndex(Span<int> cs)
        //{
        //    int n = 0, s = 0;
        //    for (s = 0; s < cs.Length; s++)
        //        if (cs[s] != 0)
        //        {
        //            n = cs[s];
        //            break;
        //        }
        //    if (n == 0) return -1;
        //
        //    int zeros = 32;
        //    {
        //        int x = n;
        //        if (x != 0)
        //        {
        //            if ((x & 0x0000FFFF) == 0) { zeros += 16; x >>= 16; }
        //            if ((x & 0x000000FF) == 0) { zeros += 8; x >>= 8; }
        //            if ((x & 0x0000000F) == 0) { zeros += 4; x >>= 4; }
        //            if ((x & 0x00000003) == 0) { zeros += 2; x >>= 2; }
        //            return zeros - (x & 1);
        //        }
        //    }
        //    return (s * sizeof(int) * 8) + zeros;
        //}

        // Oops! All integers. This is an inefficient Regex, but eh, at this point I'm having fun from suffering.
        int matchLen = 0, fullMatchLen = 0;
        int marked = 0, matching = 0;
        int sliceStart = 0;
        for (int i = 0; i < s.Length; i++, matching = 0)
        {
            // Loop could be further optimized by
            // * Creating a list of first chars for the first check (must be aware of the candidate positions)
            // * When there is a potential candidate, shift by TrailingZeroCount + target slice to
            //   jump to the candidate instead of checking the bits one by one
            // * No
            for (int j = 0; j < strs.Length; j++)
            {
                string str = strs[j];
                if (string.IsNullOrEmpty(str)) continue;
                switch (matchLen)
                {
                    case 0:
                        // On the start, all is candidate and there is no need to check the allowed candidates.
                        if (str[matchLen] == s[i])
                        {
                            if ((matchLen + 1) == str.Length) { fullMatchLen = matchLen + 1; }
                            else { MarkCandidate(candidates, j, true); matching++; }
                        }
                        break;
                    default:
                        if (GetCandidate(candidates, j))
                        {
                            if (matchLen < str.Length && str[matchLen] == s[i])
                            {
                                if ((matchLen + 1) == str.Length)
                                {
                                    fullMatchLen = matchLen + 1;
                                    MarkCandidate(candidates, j, false);
                                }
                                else
                                {
                                    matching++;
                                }
                            }
                            else
                            {
                                MarkCandidate(candidates, j, false);
                            }
                        }
                        break;
                }
            }
            switch (matching)
            {
                case 0:
                    switch (fullMatchLen)
                    {
                        case 0:
                            break;
                        // Nothing was matching, but there was a full match candidate.
                        default:
                            int last = (i - (matchLen + 1)); // May be something >= sliceStart
                            if (sliceStart < last) appendAction(self, s[sliceStart..last]);
                            sliceStart = i + 1;              // Start is inclusive
                            marked += fullMatchLen;
                            break;
                    }

                    fullMatchLen = matchLen = 0;
                    break;
                default:
                    matchLen++;
                    break;
            }
        }
        // Final append. Since sliceStart is inclusive, it is off by one if any matches exist to not include that character.
        if (sliceStart < s.Length) appendAction(self, s[sliceStart..]);

        return marked;
    }

    public static string GetTypePrefix(SJType type, SJTypeVariant v = SJTypeVariant.None)
    {
        return v switch
        {
            SJTypeVariant.None => type.ToString(),
            _ => $"{type}{VariantTypeSep}{v}",
        };
    }

    public static bool ReadWithHandler(SJReader reader, SJReader.Value root, Func<SJReader, SJReader.Value, bool>? valueHandler, bool throwError = true)
    {
        switch (root.type)
        {
            default:
            case SJType.Error:
                {
                    // Should be able to quit the loop. Note that Ended will also be marked on a case of error.
                    Console.WriteLine($"Error | Exiting {root} -- Ended:{reader.S.ended}, Error: '{reader.Error}'");
                    if (throwError)
                    {
                        reader.ThrowError();
                    }
                    break;
                }
            case SJType.End:
                {
                    // End is no longer an error, but it necessarily isn't the actual file ending.
                    // Something distinct to actual "end" is that the depth is set negative though.
                    if (reader.S.ended)
                    {
                        Assert.IsLessThanOrEqualTo(0, root.depth, "Depth must be zero if ended");
                    }
                    break;
                }

            case SJType.Array:
                {
                    while (reader.IterateArrayEntries(root, out var v))
                    {
                        if (!ReadWithHandler(reader, v, valueHandler, throwError)) return false;
                    }
                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
            case SJType.Object:
                {
                    while (reader.IterateObjectEntries(root, out var v))
                    {
                        if (!ReadWithHandler(reader, v, valueHandler, throwError)) return false;
                    }
                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
            case SJType.Comment:
                {
                    switch (root.variant)
                    {
                        case SJTypeVariant.SingleComment:
                        case SJTypeVariant.MultiComment:
                            break;
                        default:
                            if (throwError) throw new ArgumentException($"Invalid type variant for type : {root.variant}");
                            else break;
                    }
                    break;
                }
            case SJType.Type:
                {
                    switch (root.variant)
                    {
                        case SJTypeVariant.TypeLiteral:
                        case SJTypeVariant.TypeString:
                            break;
                        default:
                            if (throwError) throw new ArgumentException($"Invalid type variant for type : {root.variant}");
                            else break;
                    }
                    break;
                }
            case SJType.Key:
            case SJType.String:
            case SJType.Number:
            case SJType.Null:
            case SJType.Bool:
                {
                    break;
                }
        }

        return valueHandler?.Invoke(reader, root) ?? true;
    }
    public static void ReadWithHandler(SJReader reader, Func<SJReader, SJReader.Value, bool>? valueHandler, bool throwError = true)
    {
        for (var v = reader.Read(); !reader.S.ended; v = reader.Read()) ReadWithHandler(reader, v, valueHandler, throwError);
    }

    public static void ReadInnerTokens(StringBuilder sb, SJReader reader, SJReader.Value root, bool throwError = true)
    {
        switch (root.type)
        {
            default:
            case SJType.Error:
                {
                    // Should be able to quit the loop. Note that Ended will also be marked on a case of error.
                    Console.WriteLine($"Error | Exiting {root} -- Ended:{reader.S.ended}, Error: '{reader.Error}'");
                    if (throwError)
                    {
                        reader.ThrowError();
                    }
                    return;
                }
            case SJType.End:
                {
                    // End is no longer an error, but it necessarily isn't the actual file ending.
                    // Something distinct to actual "end" is that the depth is set negative though.
                    if (reader.S.ended)
                    {
                        Assert.IsLessThanOrEqualTo(0, root.depth, "Depth must be zero if ended");
                    }
                    break;
                }

            case SJType.Array:
                {
                    bool first = true;
                    sb.Append('[');
                    while (reader.IterateArrayEntries(root, out var v))
                    {
                        if (v.type == SJType.Comment)
                        {
                            ReadInnerTokens(sb, reader, v, throwError);
                            continue;
                        }

                        // Actual array entries
                        if (!first)
                            sb.Append(',');

                        ReadInnerTokens(sb, reader, v, throwError);

                        first = false;
                    }
                    sb.Append(']');
                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
            case SJType.Object:
                {
                    bool first = true;
                    sb.Append('{');

                    SJReader.Value k = SJReader.Value.Error();
                    while (reader.IterateObjectEntries(root, out var v))
                    {
                        switch (v.type)
                        {
                            case SJType.Comment:
                                ReadInnerTokens(sb, reader, v, throwError);
                                continue;
                            case SJType.Key:
                                k = v;
                                continue;
                            default:
                                if (!first)
                                    sb.Append(',');

                                // k must be valid.
                                ReadInnerTokens(sb, reader, k, throwError);
                                sb.Append(':');
                                ReadInnerTokens(sb, reader, v, throwError);

                                first = false;
                                continue;
                        }
                    }
                    sb.Append('}');
                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
            case SJType.Comment:
                {
                    // We are restoring the old behaviour.
                    switch (root.variant)
                    {
                        case SJTypeVariant.SingleComment:
                            sb.Append("// ").Append(root.Slice()).Append(Environment.NewLine);
                            break;
                        case SJTypeVariant.MultiComment:
                            sb.Append("/* ").Append(root.Slice()).Append(" */");
                            break;
                        default:
                            if (throwError) throw new ArgumentException($"Invalid type variant for type : {root.variant}");
                            else break;
                    }
                    break;
                }
            case SJType.Type:
                {
                    switch (root.variant)
                    {
                        case SJTypeVariant.TypeLiteral:
                            sb.Append('(').Append(root.Slice()).Append(')');
                            break;
                        case SJTypeVariant.TypeString:
                            sb.Append('(').Append('"').Append(root.Slice()).Append('"').Append(')');
                            break;
                        default:
                            if (throwError) throw new ArgumentException($"Invalid type variant for type : {root.variant}");
                            else break;
                    }
                    break;
                }
            case SJType.Key:
            case SJType.String:
                {
                    sb.Append('"').Append(root.Slice()).Append('"');
                    break;
                }
            case SJType.Number:
            case SJType.Null:
            case SJType.Bool:
                {
                    sb.Append(root.Slice());
                    break;
                }
        }
    }
    /// <summary>
    /// Only appends the captures of <see cref="SJReader.Value"/>, with seperator as '\n'.
    /// <br>Captures that may contain '\n' or '\r\n' are stripped.</br>
    /// <br>Optionally, these can be enabled for data inspection:</br>
    /// <br>* If <paramref name="indentCount"/> is set to be larger than -1, a single space indent will be done per depth.</br>
    /// <br>* If <paramref name="prefixTypes"/> is set the <see cref="SJType"/> (and <see cref="SJTypeVariant"/> 
    /// if it isn't none) will be prefixed like this: <c><see cref="SJType"/>+<see cref="SJTypeVariant"/></c> or <c><see cref="SJType"/></c></br>
    /// </summary>
    public static void ReadInnerBasic(
        StringBuilder sb, SJReader reader, SJReader.Value root,
        bool throwError = true, bool prefixTypes = false, int indentCount = -1
    )
    {
        switch (root.type)
        {
            default:
            case SJType.Error:
                {
                    // Should be able to quit the loop. Note that Ended will also be marked on a case of error.
                    Console.WriteLine($"Error | Exiting {root} -- Ended:{reader.S.ended}, Error: '{reader.Error}'");
                    if (throwError)
                    {
                        reader.ThrowError();
                    }
                    return;
                }
            case SJType.End:
                {
                    // End is no longer an error, but it necessarily isn't the actual file ending.
                    // Something distinct to actual "end" is that the depth is set negative though.
                    // This can happen if a comment is returned on the root object like "] // a" or "} // a"
                    if (reader.S.ended)
                    {
                        Assert.IsLessThanOrEqualTo(0, root.depth, "Depth must be zero if ended");
                    }
                    break;
                }

            case SJType.Type:
            case SJType.Key:
            case SJType.String:
            case SJType.Comment:
            case SJType.Number:
            case SJType.Null:
            case SJType.Bool:
                {
                    sb.Append(' ', Math.Max(0, indentCount));
                    if (prefixTypes) sb.Append(GetTypePrefix(root.type, root.variant));

                    AppendWithRemovedStrings(sb, static (self, cs) => self.Append(cs), root.Slice(), "\r", "\n");
                    break;
                }

            case SJType.Array:
                {
                    sb.Append(' ', Math.Max(0, indentCount));
                    if (prefixTypes) sb.Append(GetTypePrefix(root.type, root.variant));
                    bool first = false;

                    while (reader.IterateArrayEntries(root, out var v))
                    {
                        if (first)
                        {
                            sb.Append('\n');
                            first = true;
                        }
                        ReadInnerBasic(sb, reader, v, throwError, prefixTypes, indentCount < 0 ? -1 : indentCount + 1);
                        sb.Append('\n');
                    }

                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
            case SJType.Object:
                {
                    sb.Append(' ', Math.Max(0, indentCount));
                    if (prefixTypes) sb.Append(GetTypePrefix(root.type, root.variant));
                    bool first = false;

                    while (reader.IterateObjectEntries(root, out var v))
                    {
                        if (first)
                        {
                            sb.Append('\n');
                            first = true;
                        }
                        ReadInnerBasic(sb, reader, v, throwError, prefixTypes, indentCount < 0 ? -1 : indentCount + 1);
                        sb.Append('\n');
                    }

                    if (!string.IsNullOrEmpty(reader.Error))
                    {
                        goto case SJType.Error;
                    }
                    break;
                }
        }
    }

    // With reader
    public static void ReadWithTokens(SJReader reader, StringBuilder sb, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.ThrowOnError = throwError;
        sb.Clear();
        for (var root = reader.Read(); !reader.S.ended; root = reader.Read())
        {
            ReadInnerTokens(sb, reader, root, throwError);
        }
    }
    public static StringBuilder ReadWithTokens(SJReader reader, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var sb = new StringBuilder(DefaultSbCapacity);
        ReadWithTokens(reader, sb, throwError);
        return sb;
    }

    public static void Read(SJReader reader, StringBuilder sb, bool throwError = true, bool indents = false, bool typePrefix = false)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.ThrowOnError = throwError;
        sb.Clear();
        for (var root = reader.Read(); !reader.S.ended; root = reader.Read())
        {
            ReadInnerBasic(sb, reader, root, throwError, typePrefix, indents ? 0 : -1);
        }
    }
    public static StringBuilder Read(SJReader reader, bool throwError = true, bool indents = false, bool typePrefix = false)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var sb = new StringBuilder(DefaultSbCapacity);
        Read(reader, sb, throwError, indents, typePrefix);
        return sb;
    }

    // With data provider
    public static StringBuilder ReadWithTokens<TData>(TData data, bool jsc = false)
        where TData : ISJDataReader
    {
        using var r = new SJReader(data);
        r.allowComments = jsc;
        return ReadWithTokens(r);
    }
    public static StringBuilder Read<TData>(TData data, bool jsc = false, bool indents = false, bool typePrefix = false)
        where TData : ISJDataReader
    {
        using var r = new SJReader(data);
        r.allowComments = jsc;
        return Read(r, indents: indents, typePrefix: typePrefix);
    }
    public static StringBuilder ReadBXSJWithTokens<TData>(TData data,
        bool comments = true, bool otherKeys = true, bool trailingComma = false, bool types = true
    ) where TData : ISJDataReader
    {
        using var r = new SJReader(data);
        r.allowComments = comments;
        r.allowOtherKeyTypes = otherKeys;
        r.allowTrailingComma = trailingComma;
        r.allowTypes = types;
        return ReadWithTokens(r);
    }
    public static StringBuilder ReadBXSJ<TData>(
        TData data, bool indents = false, bool typePrefix = false,
        bool comments = true, bool otherKeys = true, bool trailingComma = false, bool types = true
    ) where TData : ISJDataReader
    {
        using var r = new SJReader(data);
        r.allowComments = comments;
        r.allowOtherKeyTypes = otherKeys;
        r.allowTrailingComma = trailingComma;
        r.allowTypes = types;
        return Read(r, indents: indents, typePrefix: typePrefix);
    }
}
