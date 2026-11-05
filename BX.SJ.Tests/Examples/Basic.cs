namespace BX.SJ.Tests.Examples
{
    sealed class Basic
    {
        public static void TMain(string[] args)
        {
            // You can read "root level" values. That is : bool, null, string and number.
            // Numbers are always seperated with '.'
            var datas = new string[] { "true", "false", "null", @"""Hello world!""", "123.456" };
            for (int i = 0; i < datas.Length; i++)
            {
                var data = datas[i];
                var reader1 = new SJStringReader(data);

                SJReader.Value value = reader1.Read();
                Console.WriteLine($"type : {value.type}, data : {new string(value.Slice())}");
            }

            // Arrays and strings can be iterated as shown
            var arrayData = "[1, 2, 3, 4, 5, 6]";
            var reader = new SJStringReader(arrayData);
            SJReader.Value root = reader.Read();
            while (reader.IterateArray(root, out SJReader.Value value))
            {
                Console.WriteLine($"type : {value.type}, data : {new string(value.Slice())}");
            }

            var objectData = @"{ ""a"": 1, ""b"": 2, ""c"": 3 }";
            reader.Data = objectData; // The reader will reset automatically
            while (reader.IterateObject(root, out SJReader.Value key, out SJReader.Value value))
            {
                Console.WriteLine($"key   | type : {key.type}, data : {new string(key.Slice())}");
                Console.WriteLine($"value | type : {value.type}, data : {new string(value.Slice())}");
            }

            // To recurse these, you can pass the resulting value into a
            // "reader function" if it's SJType.Object or SJType.Array
        }
    }
}
