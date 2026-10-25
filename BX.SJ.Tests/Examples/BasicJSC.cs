namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Example for reading JsonC documents with comment capturing.
    /// <br>This enables the opt-in (since 2.x) feature of using </br>
    /// </summary>
    sealed class BasicJSC
    {
        static void InspectConfigData(SJReader reader, SJReader.Value root)
        {
            if (root.type != SJType.Object) throw new ArgumentException("Config node type must be object", nameof(root));

            string entrySum = "";
            SJReader.Value key = SJReader.Value.Error();
            while (reader.IterateObjectEntries(root, out var value))
            {
                switch (value.type)
                {
                    case SJType.Error:
                        return;

                    case SJType.Comment:
                        const string SummaryTk = "@summary";
                        var comment = value.Slice(2, value.Length - 2).Trim(); // Skip // or /*
                        if (comment.StartsWith(SummaryTk))
                        {
                            // Skip */
                            if (comment.EndsWith("*/"))
                            {
                                comment = comment.Slice(0, comment.Length - 2).Trim();
                            }

                            entrySum = new string(comment.Slice(SummaryTk.Length).Trim());
                        }
                        continue;
                    case SJType.Key:
                        key = value;
                        continue;

                    default:
                        Console.WriteLine($"Summary: {entrySum}\n  {new string(key.Slice())} = {new string(value.Slice())}");
                        continue;
                }
            }
        }

        public static void TMain(string[] args)
        {
            const string data = @"// The problem with comments is that they can appear anywhere.
// Which makes some things that were implicit, explicit now.
{
    // Comments in JSON doesn't make much sense actually. Just use ini
    /* @summary This field is 42, and actually you can put this within the JSON, but whatever.. */
    ""is42"": 42,
    /* @summary And this field is 67, you know why.. */
    ""is67"": 67 // its the short form content number! 
}
// And to validate EOF, do this (it was implicit with the last Read() on the old behaviour) ↓
";
            var reader = new SJStringReader(data)
            {
                allowComments = true,
                captureComments = true // Opt-in to the new behaviour.
            };

            for (var root = reader.Read(); !reader.ended; root = reader.Read())
            {
                // Skip root level comments if they are irrelevant
                if (root.type == SJType.Comment)
                    continue;

                InspectConfigData(reader, root);
            }
        }
    }
}
