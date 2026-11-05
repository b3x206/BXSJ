using System.Text;

namespace BX.SJ.Tests.Examples
{
    internal sealed class Printer
    {
        static void ReadInner(StringBuilder sb, SJReader reader, SJReader.Value root)
        {
            switch (root.type)
            {
                default:
                case SJType.Error:
                    {
                        reader.Location(out int l, out int c);
                        Console.WriteLine($"ReadInner: Error '{reader.Error}' at 'line={l}, column={c}'");
                        // ^ This will output multiple times, you can add a global flag if that is undesired.
                        // Or you can throw like : reader.ThrowError();
                        break;
                    }
                case SJType.Comment:
                    {
                        // Discard comments explicitly, if capturing those and don't care about it.
                        break;
                    }

                case SJType.Array:
                    {
                        bool first = true;
                        sb.Append('[');
                        while (reader.IterateArray(root, out var array))
                        {
                            if (!first)
                                sb.Append(',');

                            ReadInner(sb, reader, array);

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
                        while (reader.IterateObject(root, out var k, out var v))
                        {
                            if (!first)
                                sb.Append(',');

                            ReadInner(sb, reader, k);
                            sb.Append(':');
                            ReadInner(sb, reader, v);

                            first = false;
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
                        // If unescaping the string is desired, you can use SJEscape.Unescape(string)
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

        public static void TMain(string[] args)
        {
            string data = args.Length > 1 ? args[1] : 
                "{ \"foo\": [\"bar\", \"baz\"], \"idk\": 42, \"mango\": { \"6\": 7 } }";
            var reader = new SJStringReader(data);

            SJReader.Value root = reader.Read();
            var sb = new StringBuilder(512);
            ReadInner(sb, reader, root);

            Console.WriteLine(sb);
        }
    }
}
