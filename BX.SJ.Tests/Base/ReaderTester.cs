using System.Numerics;
using System.Text;

namespace BX.SJ.Tests;

/// <summary>
/// Class used to recursively iterate the <see cref="SJReader"/> onto a <see cref="StringBuilder"/>.
/// </summary>
public static class ReaderTester
{
    public const int DefaultSbCapacity = 512;

    public static int RemoveCharactersFrom(ref Span<char> s, params string[] strs)
    {
        // This is utterly overkill. And likely buggy.
        int csSize = (strs.Length / 32) + 1;
        Span<int> candidates     = csSize > 32 ? new int[csSize] : stackalloc int[csSize];
        Span<int> fullCandidates = csSize > 32 ? new int[csSize] : stackalloc int[csSize];
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
            else cs[index] &= ~bit;
        }

        // Dawg wth are you doing
        int candidateRead = 0;
        int marked = 0;
        const char ReplaceMarker = '\0';
        for (int i = 0, rfor = 0; i < s.Length; i++)
        {
            if (rfor > 0)
            {
                s[i] = ReplaceMarker;
                marked++;
                rfor--;
                continue;
            }

            for (int j = 0; j < strs.Length; j++)
            {
                int matching = 0;
                string str = strs[j];
                switch (candidateRead)
                {
                    case 0:
                        if (str[candidateRead] == s[i])
                        {
                            MarkCandidate(candidates, j, true);
                            matching++;
                        }
                        switch (matching)
                        {
                            case 0:
                                break;
                            default:
                                candidateRead++;
                                break;
                        }
                        break;
                    default:
                        if (GetCandidate(candidates, j))
                        {
                            // full matching could mean the fact that there wouldn't be any strings  
                            if (candidateRead >= str.Length)
                            {
                                MarkCandidate(fullCandidates, j, true);
                                matching++;
                            }
                            else if (str[candidateRead] == s[i])
                            {
                                matching++;
                            }
                            else
                            {
                                MarkCandidate(candidates, j, false);
                            }
                        }
                        switch (matching)
                        {
                            case 0:
                                candidateRead = 0;
                                break;
                            case 1:
                                // Get the candidate BitOperations.TrailingZeroCount()


                                j--;
                                rfor = 1;
                                break;
                            default:
                                candidateRead++;
                                break;
                        }
                        break;
                }
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

    // Top level read must be seperated into "while (!reader.Ended)" and pump SJReader.Value roots
    public static void ReadInner(StringBuilder sb, SJReader reader, SJReader.Value root, bool throwError = true)
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
                        Assert.IsLessThanOrEqualTo(0, root.objectDepth, "Depth must be zero if ended");
                    }
                    break;
                }

            case SJType.Comment:
                {
                    // Reading comments require skipping (with explicit Read())
                    // as they can be placed anywhere within the document.
                    sb.Append(root.Slice());
                    break;
                }

            case SJType.Array:
                {
                    bool first = true;
                    sb.Append('[');
                    while (reader.IterateArrayEntries(root, out var v))
                    {
                        // Skipping comments on array is easier. Technically could also do
                        // the "ObjectEntry type" sieve for array as well.
                        if (v.type == SJType.Comment)
                        {
                            ReadInner(sb, reader, v, throwError);
                            continue;
                        }

                        // Actual array entries
                        if (!first)
                            sb.Append(',');

                        ReadInner(sb, reader, v, throwError);

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
                                ReadInner(sb, reader, v, throwError);
                                continue;
                            case SJType.Key:
                                k = v;
                                continue;
                            default:
                                if (!first)
                                    sb.Append(',');

                                // k must be valid.
                                ReadInner(sb, reader, k, throwError);
                                sb.Append(':');
                                ReadInner(sb, reader, v, throwError);

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
    /// </summary>
    public static void ReadInnerBasic(StringBuilder sb, SJReader reader, SJReader.Value root, bool throwError = true)
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
                        Assert.IsLessThanOrEqualTo(0, root.objectDepth, "Depth must be zero if ended");
                    }
                    break;
                }

            case SJType.Comment:
                {
                    sb.Append(root.Slice());
                    break;
                }

            case SJType.Array:
                {
                    bool first = true;
                    sb.Append('[');
                    while (reader.IterateArrayEntries(root, out var v))
                    {
                        // Skipping comments on array is easier. Technically could also do
                        // the "ObjectEntry type" sieve for array as well.
                        if (v.type == SJType.Comment)
                        {
                            ReadInner(sb, reader, v, throwError);
                            continue;
                        }

                        // Actual array entries
                        if (!first)
                            sb.Append(',');

                        ReadInner(sb, reader, v, throwError);

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
                                ReadInner(sb, reader, v, throwError);
                                continue;
                            case SJType.Key:
                                k = v;
                                continue;
                            default:
                                if (!first)
                                    sb.Append(',');

                                // k must be valid.
                                ReadInner(sb, reader, k, throwError);
                                sb.Append(':');
                                ReadInner(sb, reader, v, throwError);

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

    public static void Read(SJReader reader, StringBuilder sb, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.ThrowOnError = throwError;
        sb.Clear();
        for (var root = reader.Read(); !reader.S.ended; root = reader.Read())
        {
            ReadInner(sb, reader, root, throwError);
        }
    }
    public static StringBuilder Read(SJReader reader, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var sb = new StringBuilder(DefaultSbCapacity);
        Read(reader, sb, throwError);
        return sb;
    }
}
