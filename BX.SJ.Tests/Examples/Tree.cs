#pragma warning disable CA1510 // Use ArgumentNullException throw helper
#pragma warning disable IDE0090 // Initialization can be simplified
#pragma warning disable IDE0005
#pragma warning disable IDE0057
#nullable disable

using BX.SJ;
using System;
using System.Globalization;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// A basic tree structure. Used for testing, but also available in the Examples
    /// <br>Shows the capabilities of using both <see cref="SJWriter"/> and <see cref="SJReader"/>.</br> 
    /// </summary>
    sealed class SJTree
    {
        // Since the SJ value is lazily evaluated, we should copy the data here too.
        public SJType type;

        public string stringValue;
        public bool boolValue;
        public double numberValue;
        public Lazy<List<SJTree>> arrayValue = new Lazy<List<SJTree>>();
        // ↓ Keys are assumed to be "string", but they can be anything else as well.
        //   So string is used as a catch-all, until recursive keys are possible.
        public Lazy<Dictionary<string, SJTree>> objectValue = new Lazy<Dictionary<string, SJTree>>();

        // Note : Does not check cyclic references, but those can be handled with a Dictionary<SJTree, string /* for path */>
        //        with a reference to the "node" that will hold a references table.
        public static SJTree FromJSON(SJReader reader)
        {
            var t = new SJTree();
            WriteFrom(t, reader);
            return t;
        }
        public static void WriteFrom(SJTree tree, SJReader reader)
        {
            if (tree is null)
            {
                throw new ArgumentNullException(nameof(tree));
            }
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }
            if (!string.IsNullOrEmpty(reader.Error))
            {
                // Either reset the reader, or throw an argument exception.
                throw new ArgumentException($"Given SJReader has an error : {reader.Error}", nameof(reader));
            }

            for (var root = reader.Read(); !reader.S.ended; root = reader.Read())
            {
                WriteFromInternal(tree, reader, root);
            }
        }
        private static void WriteFromInternal(SJTree tree, SJReader reader, SJReader.Value root)
        {
            tree.type = root.type;

            switch (root.type)
            {
                default:
                case SJType.Error:
                    {
                        // Throw the error with one of the following methods
                        // 1. reader.ThrowOnError = true;               → Mutates the reader. Could be undesired if exception is being caught.
                        // 2. throw new SJReader.ReadException(reader); → Throws error without changing 
                        // 3. reader.ThrowError();                      → Less verbose. Throws regardless of "Error" being null/empty or "ThrowOnError".
                        reader.ThrowError();
                        break;
                    }
                case SJType.Null:
                case SJType.End:
                case SJType.Type:
                case SJType.Comment:
                    break;

                case SJType.Array:
                    {
                        while (reader.IterateArray(root, out var v))
                        {
                            var node = new SJTree();
                            WriteFromInternal(node, reader, v);
                            tree.arrayValue.Value.Add(node);
                        }

                        if (!string.IsNullOrEmpty(reader.Error))
                        {
                            goto case SJType.Error;
                        }
                        break;
                    }
                case SJType.Object:
                    {
                        while (reader.IterateObject(root, out SJReader.Value k, out SJReader.Value v))
                        {
                            var node = new SJTree();
                            WriteFromInternal(node, reader, v);
                            tree.objectValue.Value[SJEscape.Unescape(k.Slice())] = node;
                        }

                        if (!string.IsNullOrEmpty(reader.Error))
                        {
                            goto case SJType.Error;
                        }
                        break;
                    }

                case SJType.String:
                    {
                        // If unescaping the string is desired, you can use SJEscape.Unescape(string)
                        tree.stringValue = SJEscape.Unescape(root.Slice());
                        break;
                    }
                case SJType.Number:
                    {
                        // This may not work with hexadecimal numbers, so try to parse according to prefix
                        var s = root.Slice();
                        bool n = false;
                        if (s.Length > 2)
                        {
                            switch ((s[0], char.ToLower(s[1])))
                            {
                                case ('0', 'x'):
                                    tree.numberValue = long.Parse(s.Slice(2), NumberStyles.HexNumber);
                                    n = true;
                                    break;
                                case ('0', 'b'):
                                    tree.numberValue = long.Parse(s.Slice(2), NumberStyles.BinaryNumber);
                                    n = true;
                                    break;
                                // C# does not have this. Maybe enable if they add it, or use a custom impl.
                                // It shouldn't be too hard, BXSJ uses all numbers as invariant culture.
                                // case ('0', 'o'):
                                    // tree.numberValue = long.Parse(s.Slice(2), NumberStyles.OctalNumber);
                                    // n = true;
                                    // break;
                            }
                        }
                        if (!n)
                        {
                            tree.numberValue = double.Parse(root.Slice(), NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture);
                        }
                        break;
                    }
                case SJType.Bool:
                    {
                        tree.boolValue = char.ToLower(root.Slice()[0]) == 't';
                        break;
                    }
            }
        }

        public static string ToJSON(SJTree tree) => ToJSON(tree, new SJStringWriter()
        {
            ThrowOnError = true
        });
        public static string ToJSON(SJTree tree, SJWriter writer)
        {
            WriteTo(tree, writer);
            return writer.ReadData() ?? "";
        }
        public static void WriteTo(SJTree tree, SJWriter writer)
        {
            switch (tree.type)
            {
                case SJType.Number:
                    writer.WriteNumber(tree.numberValue);
                    break;
                case SJType.String:
                    writer.WriteString(tree.stringValue);
                    break;
                case SJType.Bool:
                    writer.WriteBool(tree.boolValue);
                    break;
                case SJType.Null:
                    writer.WriteNull();
                    break;
                case SJType.Object:
                    if (tree.objectValue.IsValueCreated)
                    {
                        using (writer.Object())
                        {
                            foreach (var kv in tree.objectValue.Value)
                            {
                                writer.WriteKey(kv.Key);
                                WriteTo(kv.Value, writer);
                            }
                        }
                    }
                    else
                    {
                        goto case SJType.Null;
                    }
                    break;
                case SJType.Array:
                    if (tree.arrayValue.IsValueCreated)
                    {
                        using (writer.Array())
                        {
                            foreach (var v in tree.arrayValue.Value)
                            {
                                WriteTo(v, writer);
                            }
                        }
                    }
                    else
                    {
                        goto case SJType.Null;
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Invalid node type '{tree.type}', a node cannot be key, type or anything else.");
            }
        }

        public override string ToString()
        {
            // Preview tree as JSON, though should do this if the object isn't that large.
            return ToJSON(this, new SJStringWriter()
            {
                ThrowOnError = true,
                indentSize = 4
            });
        }
    }
}

#nullable restore
#pragma warning restore IDE0005
#pragma warning restore CA1510
#pragma warning restore IDE0090
#pragma warning disable IDE0057
