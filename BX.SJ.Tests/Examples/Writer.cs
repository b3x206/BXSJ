namespace BX.SJ.Tests.Examples
{
    sealed class Writer
    {
        public static void TMain(string[] args)
        {
            var writer = new SJStringWriter()
            {
                // Enable pretty printing. Setting this 0 or less will disable pretty printing.
                indentSize = 4,
                // Don't throw on error for now
                ThrowOnError = false,
            };
            // Writing any of the following in the root is possible, but:
            writer.Write(123.123);
            // Since the depth is zero, the finish flag is set. Meaning it's no longer to write anything later.
            // (because ThrowOnError is false, these will silently fail, setting writer.Error only and returning false)
            writer.Write(123123123L);
            writer.Write(123123123UL);
            writer.Write("Hello!");
            using (writer.Array()) { }  // These just set error, but return a valid IDisposable. Disposing does nothing
            using (writer.Object()) { }

            // .. erroreneous writes will require a reset, when not throwing.
            // Each "Write" method returns a boolean, it will return false.
            writer.Reset();
            // Let's throw exceptions for the rest of the example.
            writer.ThrowOnError = true;

            // Arrays are written like this:
            using (writer.Array()) // Or writer.BeginArray();
            {
                writer.Write(1);
                writer.Write(2);
                writer.Write(3);
                writer.Write(4);
                writer.Write(5);
            } // writer.EndArray();
              // ↑ The array will end automatically once you go out of using scope.
            Console.WriteLine($"Result Array : {writer.ReadData()}");

            // To not throw here, reset before writing another root level object..
            writer.Reset();
            using (writer.Object()) // Or writer.BeginObject();
            {
                // You must write key the following way:
                // 1 (recommended) :
                writer.WriteKey("key");
                // 2 (does the same thing as WriteKey if a key is needed):
                // writer.WriteString("key");
                // 3 (not recommended, if your key is null object you will get an error instead):
                // writer.Write("key");
                // Note that there isn't a built in check for duplicate keys.
                // > You should be careful with it, or modify/override WriteKey to track that.
                // ---
                // Then write your value. You can recurse to create tree structures, but I will just write a basic "value" instead
                writer.Write("my nice value");
                // If you end your object without writing a "value" after a "key" was written, you will cause an error so beware.
                // You can also alternatively use:
                writer.WriteKV("other key", "other nice value");
                // ↑ Though you should make sure that you are in a object or
                //   not expecting a value before doing this.

                // It is possible to nest objects. It will also be auto indented too with pretty printing:
                using (writer.ObjectKV("numbers that i like"))
                {
                    using (writer.ArrayKV("in childhood"))
                    {
                        writer.Write(3.141592); // Larp pro max
                        writer.Write(2); // It's nice
                        writer.Write(5); // Was my first favourite
                        writer.Write(8); // Also good
                    }

                    // Now I fail math. But dw I don't hate it, because I'm not a mathmetician
                    writer.WriteKey("currently");
                    writer.WriteNull();
                }
            } // writer.EndObject();
              // ↑ The object will end automatically once you go out of using scope.

            // Output the resulting JSON like this: (ReadData)
            Console.WriteLine($"Result Object : {writer.ReadData()}");
        }
    }
}
