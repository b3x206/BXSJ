using System.Diagnostics;

namespace BX.SJ.Tests.Examples
{
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
                Debug.Assert(false, "Fail. We are testing the code after all.");
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
                writer.WriteLong(4);
                writer.WriteCommentLine("Well, this is weird, the dev lied to us!", hasNextValue: true);
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

            // TODO : Do the types
            throw new NotImplementedException("Type writing is not yet done");
        }
    }
}
