using System;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// // This example shows how you can recursively read from a SJReader
    /// using BX.SJ;
    /// using System.Text;
    /// 
    /// var data = "{ \"foo\": [\"bar\", \"baz\"], \"idk\": 42, \"mango\": { \"6\": 7 } }";
    /// var reader = new SJStringReader(data);
    /// 
    /// var sb = new StringBuilder(512);
    /// for (SJReader.Value root = reader.Read(); !reader.ended; root = reader.Read())
    /// {
    ///     ReadInner(sb, reader, root);
    /// }
    /// 
    /// static void ReadInner(StringBuilder sb, SJReader reader, SJReader.Value root)
    /// {
    ///     switch (root.type)
    ///     {
    ///         default:
    ///         case SJType.Error:
    ///             {
    ///                 reader.Location(out int l, out int c);
    ///                 Console.WriteLine($"ReadInner: Error '{reader.Error}' at 'line={l}, column={c}'");
    ///                 // ^ This will output multiple times, you can add a global flag if that is undesired.
    ///                 break;
    ///             }
    ///         case SJType.End:
    ///         case SJType.Comment:
    ///             break;
    ///         case SJType.Array:
    ///             {
    ///                 bool first = true;
    ///                 sb.Append('[');
    ///                 while (reader.IterateArray(root, out var array))
    ///                 {
    ///                     if (!first)
    ///                         sb.Append(',');
    /// 
    ///                     ReadInner(sb, reader, array);
    /// 
    ///                     first = false;
    ///                 }
    ///                 sb.Append(']');
    ///                 if (!string.IsNullOrEmpty(reader.Error))
    ///                 {
    ///                     goto case SJType.Error;
    ///                 }
    ///                 break;
    ///             }
    ///         case SJType.Object:
    ///             {
    ///                 bool first = true;
    ///                 sb.Append('{');
    ///                 while (reader.IterateObject(root, out var k, out var v))
    ///                 {
    ///                     if (!first)
    ///                         sb.Append(',');
    /// 
    ///                     ReadInner(sb, reader, k);
    ///                     sb.Append(':');
    ///                     ReadInner(sb, reader, v);
    /// 
    ///                     first = false;
    ///                 }
    ///                 sb.Append('}');
    ///                 if (!string.IsNullOrEmpty(reader.Error))
    ///                 {
    ///                     goto case SJType.Error;
    ///                 }
    ///                 break;
    ///             }
    ///         case SJType.String:
    ///             {
    ///                 // If unescaping the string is desired, you can use SJEscape.Unescape(string)
    ///                 sb.Append('"').Append(root.Slice()).Append('"');
    ///                 break;
    ///             }
    ///         case SJType.Key:
    ///         case SJType.Number:
    ///         case SJType.Null:
    ///         case SJType.Bool:
    ///             {
    ///                 sb.Append(root.Slice());
    ///                 break;
    ///             }
    ///     }
    /// }
    /// ]]>
    /// </example>
    public sealed class SJStringReader : SJReader
    {
        private string _Data;
        public string Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Data;
            set
            {
                if (string.Equals(value, _Data, StringComparison.Ordinal))
                {
                    return;
                }

                _Data = value;
                Reset();
            }
        }

        public SJStringReader()
        { }
        public SJStringReader(string data)
        {
            _Data = data;
        }

        // This is used as a limiter.
        public override int Length => _Data?.Length ?? 0;
        // Because I ported the pointer stuff as-is, some parts of the parser may read off by one.
        // It is recommended to do an bound check and return EOF of your choice
        public override char At(int i) => i >= 0 && i < Length ? _Data[i] : '\0';
        // Because each value slice is evaluated lazily, the data ranges must persist and should be representable easily as a range (making arbitrary Streams much harder)
        // Note that there isn't much of a reason to do this, if you read the data as soon as it's received from the SJReader.
        public override ReadOnlySpan<char> Slice(int start, int length) =>
            string.IsNullOrEmpty(_Data) ? ReadOnlySpan<char>.Empty : _Data.AsSpan(start, length);
    }
}

