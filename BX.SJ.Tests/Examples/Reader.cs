#pragma warning disable IDE0005
using System;
using BX.SJ;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Basic demonstration of <see cref="SJReader"/>, showing the logic beneath reading.
    /// </summary>
    sealed class Reader
    {
        public static void TMain(string[] args)
        {
            // You can read "root level" values. That is : bool, null, string and number.
            // Numbers are always seperated with '.'
            var datas = new string[] { "true", "false", "null", @"""Hello world!""", "123.456", "3.7e+8" };
            for (int i = 0; i < datas.Length; i++)
            {
                var data = datas[i];
                var reader1 = new SJReader(new SJStringData(data));

                SJReader.Value value = reader1.Read();
                Console.WriteLine($"type : {value.type}, data : {new string(value.Slice())}");
            }

            // Arrays and strings can be iterated as shown
            const string arrayData = "[1, 2, 3, 4, 5, 6]";
            var readerData = new SJStringData(arrayData);
            var reader = new SJReader(readerData);
            // This captures the root value. If there are multiple (can happen with non-standard ext)
            // It is always suggested to use a for loop and read like this to skip other values.:
            for (SJReader.Value root1 = reader.Read(); !reader.S.ended; root1 = reader.Read())
            {
                if (root1.type == SJType.Array) while (reader.IterateArray(root1, out SJReader.Value value))
                {
                    Console.WriteLine($"type : {value.type}, data : {new string(value.Slice())}");
                }
            }

            const string objectData = @"{ ""a"": 1, ""b"": 2, ""c"": 3 }";
            readerData.Data = objectData;        // The reader will reset automatically when this is set.
            SJReader.Value root = reader.Read(); // Get a new root value to read. Since we know that this string does not have other roots, this can be done.
            while (reader.IterateObject(root, out SJReader.Value key, out SJReader.Value value))
            {
                // BXSJ object keys are always promoted to a seperate "Key" type.
                // You can read the underlying inferred type by using "prevType", it is generally String.
                Console.WriteLine($"key   | type : {key.type}, real type : {key.prevType}, data : {new string(key.Slice())}");
                // Values stay the same regardless of their types.
                Console.WriteLine($"value | type : {value.type}, data : {new string(value.Slice())}");
            }

            // Extensions related:
            /// These iterators ignore the non-standard non-values (if support for those are enabled) like comments.
            /// As an alternative, you can use <see cref="SJReader.IterateObjectEntries(SJReader.Value, out SJReader.Value)"/>
            /// or <see cref="SJReader.IterateArrayEntries(SJReader.Value, out SJReader.Value)"/>. They behave identically,
            /// but the object one does not return a captured key (it's the caller's responsibility, or use <see cref="SJReader.State.lastEntryKey"/>).

            /// To recurse these, you can pass the resulting value into a
            /// "reader function" if it's <see cref="SJType.Object"/> or <see cref="SJType.Array"/>.
            /// ---
            /// While recursing, your code must be aware of <see cref="SJType.End"/>, <see cref="SJType.Comment"/>,
            /// <see cref="SJType.Type"/> and others as you cannot rely on someone setting the SJReader settings correctly.
            /// Check ReaderJSC and ReaderEx on how to correctly read such non-standard documents.
        }
    }
}
#pragma warning restore IDE0005
