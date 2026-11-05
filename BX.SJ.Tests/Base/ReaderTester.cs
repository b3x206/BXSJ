using System.Text;

namespace BX.SJ.Tests;

/// <summary>
/// Class used to recursively iterate the <see cref="SJReader"/> onto a <see cref="StringBuilder"/>.
/// </summary>
public static class ReaderTester
{
    // Top level read must be seperated into "while (!reader.Ended)" and pump SJReader.Value roots
    public static void ReadInner(StringBuilder sb, SJReader reader, SJReader.Value root, bool throwError = true)
    {
        switch (root.type)
        {
            default:
            case SJType.Error:
                {
                    // Should be able to quit the loop. Note that Ended will also be marked on a case of error.
                    Console.WriteLine($"Error | Exiting {root} -- Ended:{reader.ended}, Error: '{reader.Error}'");
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
                    if (reader.ended)
                    {
                        Assert.IsTrue(root.objectDepth <= 0, "Depth must be zero if ended");
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
    public const int DefaultSbCapacity = 512;

    public static void Read(SJReader reader, StringBuilder sb, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.ThrowOnError = throwError;
        sb.Clear();
        for (var root = reader.Read(); !reader.ended; root = reader.Read())
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

    public static void ReadJSC(SJReader reader, StringBuilder sb, bool captureComments = false, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        sb.Clear();
        reader.allowComments = true;
        reader.captureComments = captureComments;
        reader.ThrowOnError = throwError;
        for (var root = reader.Read(); !reader.ended; root = reader.Read())
        {
            ReadInner(sb, reader, root, throwError);
        }
    }
    public static StringBuilder ReadJSC(SJReader reader, bool captureComments = false, bool throwError = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var sb = new StringBuilder(DefaultSbCapacity);
        ReadJSC(reader, sb, captureComments, throwError);
        return sb;
    }
}
