using System.Diagnostics;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Extended demonstration for <see cref="SJWriter"/>, with it's non standard bxsj features.
    /// </summary>
    sealed class WriterEx
    {
        public static void TMain(string[] args)
        {
            var writer = new SJStringWriter()
            {
                indentSize = 4,
                ThrowOnError = false,
                // These extra options allow writing invalid json, but valid bxsj. All of these will be demoed in order.
                allowComments = true,
                allowTrailingComma = true,
                allowOtherKeyTypes = true,
                allowTypes = true
            };
            Debug.Assert(writer.CanReadData, "Data must be readable to be able to present the final non standard data");

            // Comments can be written anywhere in document. Comments write newlines regardless of the indent size.
            // Multiline comments can contain \n and other characters, but they aren't indented to the depth automatically.
            // ↓ Will write "/*_Interoperability machine broke_*/". A '\n' will be appended after regardless of indentSize. 
            writer.WriteComment("Interoperability machine broke", pad: '_', newlineImmediately: false);
            // ↓ Will write "//↑ jSON 😭\n"
            writer.WriteCommentLine("↑ jSON 😭", prefixPad: '\0'); // Prefix pad cannot be \r, \n or \0. It is silently ignored and no pad is done
            Console.WriteLine(writer.ReadData());

            // Comment lines can be written after the document has ended:
            writer.Write(1234.1234f);
            if (writer.WriteComment("↑ not jSON 😭") && writer.S.ended)
            {
                Console.WriteLine("Written comment after the writer ended, wow.");
            }
            else
            {
                Debug.Fail("Fail. We are testing the code after all.");
                return;
            }

            // The state will have it's "written comment" marked:
            Console.WriteLine($"Has written comment: {writer.S.WrittenComment}");
            Console.WriteLine($"Final state: {writer.ReadData()}");
            writer.Reset();

            // The trailing comma only occurs on one condition : A comment does not defer the comma per user's input. Let's demo this with an array.
            using (writer.Array())
            {
                writer.WriteLong(1);
                writer.WriteLong(2);
                writer.WriteLong(3);
                using (writer.Object())
                {
                    writer.WriteKV("don't", "mind me");
                    writer.WriteComment("yeah don't it's normal", hasNextValue: true);
                }
                writer.WriteLong(4);
                writer.WriteCommentLine("Unexpectedly, the dev lied to us!", hasNextValue: true);
            } // The "allowTrailingComma" check will be done here.
            Console.WriteLine($"Has trailing comma: {writer.S.WrittenTrailingComma}");
            Console.WriteLine($"Final state: {writer.ReadData()}");
            writer.Reset();

            // Other key types allowed are any SJ type that isn't recursive.
            using (writer.Object())
            {
                // ↓ "true: 1"
                writer.WriteKeyBool(true); // Writes literal
                writer.WriteLong(1);
                // ↓ "false: 0"
                writer.WriteKeyBool(false);
                writer.WriteLong(0);
                // ↓ "1234: "yes it's 1234""
                writer.WriteKeyLong(1234);
                writer.WriteString("yes it's 1234");
                // ↓ "0xDEADBEEF: "rip beef""
                writer.WriteKeyULong(0xDEADBEEF, "X");
                writer.WriteString("rip beef");
                // ↓ "42.67f: \n[{indent}42,{indent}\n67\n{indent-1}]"
                writer.WriteKeyNumber(42.67f);
                using (writer.Array()) { writer.WriteLong(42); writer.WriteLong(67); }
                // [!] Only supported on .NET 7+
                // ↓ "0b1000011: true"
                // writer.WriteKeyULong(0b1000011, "B");
                // writer.WriteBool(true);
            }
            Console.WriteLine($"Has custom key: {writer.S.WrittenCustomKey}");
            Console.WriteLine($"Final state: {writer.ReadData()}");
            writer.Reset();

            // Type prefixes can be written as extra metadata.
            // They do nothing outside of storing extra data about the next Key or Value entry.
            using (writer.Object())
            {
                // Custom types:
                // ↓ "smolNumber": (Int16)-32767
                writer.WriteKey("smolNumber");
                writer.WriteType(nameof(Int16));
                writer.WriteLong(ushort.MinValue);

                // ↓ "prettyColor": (Color)[81, 43, 212]
                writer.WriteKey("prettyColor");
                writer.WriteType("Color");
                using (writer.Array())
                {
                    writer.WriteNumber(81);
                    writer.WriteNumber(43);
                    writer.WriteNumber(212);
                }

                // Assume that there's a type enum with type codes
                // ↓ "mappedType": (0xC0DE){ ... }
                writer.WriteKey("mappedType");
                writer.WriteType(0xC0DE, "X");
                using (writer.Object())
                {
                    writer.WriteKV("privateKey", 4);
                    writer.WriteKV("security", true);
                    writer.WriteKV("dontHack", "yes on");
                }

                // Types can be applied to keys and values as well
                // ↓ (CowKey)0xBEEF: (WeightTon)1E+24
                writer.WriteType("CowKey");
                writer.WriteKeyLong(0xBEEF, "X");
                writer.WriteType("WeightTon");
                writer.WriteNumber(1e+24);

                // Types with invalid characters can be stringified (with escapes)
                writer.WriteKey("cSharpObject");
                writer.WriteType(typeof(object).AssemblyQualifiedName, true);
                writer.WriteNull(); // :shrug:
            }
            Console.WriteLine($"Has type literals (or types): {writer.S.WrittenType}");
            Console.WriteLine($"Final state: {writer.ReadData()}");

            // // Note : If errors are ignored, SJWriter keeps writing :P
            // // Unwinding an erroreneous implicit stack is easier with exceptions, hence that's why I added support for it.
            // throw new Exception("Type writing fails miserably");
        }
    }
}
