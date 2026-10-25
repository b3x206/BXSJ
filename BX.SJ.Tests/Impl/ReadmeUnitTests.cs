using System.Globalization;
using System.Numerics;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class ReadmeUnitTests
{
#pragma warning disable IDE0059 // Unnecessary assignment of a value
    // - Main
    [TestMethod]
    public void ReaderSnippet()
    {
        static Vector3 ReadVector3(SJReader reader, SJReader.Value root)
        {
            Vector3 result = Vector3.Zero;

            // If the data schema is "known", you can just read like this:
            if (root.type != SJType.Object) throw new ArgumentException("Vector3 node type must be object", nameof(root));
            while (reader.IterateObject(root, out SJReader.Value key, out SJReader.Value value))
            {
                switch (char.ToLower(key.Slice()[0]))
                {
                    // CultureInfo stuff is if your country seperates decimals with ',' instead of '.'
                    case 'x': result.X = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    case 'y': result.Y = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    case 'z': result.Z = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    default: throw new Exception($"Invalid key : {key}");
                }
            }
            return result;
        }

        const string data = @"{ ""x"": 42.0, ""y"": -42.0, ""z"": 0.0 }";
        var reader = new SJStringReader(data);

        for (SJReader.Value root = reader.Read(); !reader.ended; root = reader.Read())
        {
            Console.WriteLine($"Saved Position : {ReadVector3(reader, root)}");
        }
    }
    [TestMethod]
    public void NewMethodReaderSnippet()
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

        const string data = @"// The problem with comments is that they can appear anywhere.
// Which makes some things that were implicit, explicit now.
{
    // Comments in JSON doesn't make much sense actually. Just use ini
    /* @summary This field is 42, and actually you can put this within the JSON, but whatever.. */
    ""is42"": 42,
    /* @summary And this field is 67, you know why.. */
    ""is67"": 67 // its the short form content number! 
}
// And to validate EOF, do this (it was implicit with the last Read()) ↓
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

    [TestMethod]
    public void UnescapeSnippet()
    {
        string result = SJEscape.Unescape("Hello world! Back slash:\\\\ Quote:\\\"");
        Console.WriteLine(result);
    }
    [TestMethod]
    public void WriterSnippet()
    {
        static Vector3 ReadVector3(SJReader reader, SJReader.Value root)
        {
            Vector3 result = Vector3.Zero;

            // If the data schema is "known", you can just read like this:
            if (root.type != SJType.Object) throw new ArgumentException("Vector3 node type must be object", nameof(root));
            while (reader.IterateObject(root, out SJReader.Value key, out SJReader.Value value))
            {
                switch (char.ToLower(key.Slice()[0]))
                {
                    // CultureInfo stuff is if your country seperates decimals with ',' instead of '.'
                    case 'x': result.X = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    case 'y': result.Y = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    case 'z': result.Z = float.Parse(value.Slice(), CultureInfo.InvariantCulture); break;
                    default: throw new Exception($"Invalid key : {key}");
                }
            }
            return result;
        }

        // Serialize an arbitrary Vector3
        var writer = new SJStringWriter()
        {
            // Enable pretty printing
            indentSize = 4,
        };
        var position = new Vector3(1f, 2f, 3f);
        using (writer.Object())
        {
            writer.WriteKV("X", position.X);
            writer.WriteKV("Y", position.Y);
            writer.WriteKV("Z", position.Z);
        }

        // Now deserialize it
        position = Vector3.Zero;
        string data = writer.ReadData();
        var reader = new SJStringReader(data);
        position = ReadVector3(reader, reader.Read());

        // Use ReadData to read the resulting string.
        Console.WriteLine($"Saved Position : {data}, Read Position : {position}");
    }

    // - Migration 1.x -> 2.x
    // 1: Change that the compiler will error out for anyways. This is just to validate the code changes as I use rename identifier.
    [TestMethod("Migration 1.x -> 2.x | 1: Move from SJ to BX.SJ")]
    public void Migration1xTo2x_1()
    {
        // Check if the BX.SJ assembly has anything in the SJ namespace
        Assert.IsFalse(typeof(SJReader).Assembly.GetTypes().Any(v => v.Namespace?.StartsWith("SJ") ?? true), "Must not have any namespace start with SJ or have null/root namespace");
    }
    [TestMethod("Migration 1.x -> 2.x | 2: Handle SJType.Key")]
    public void Migration1xTo2x_2()
    {
        const string Data = @"{ ""a"": ""b"" }";
        static void FailProcess(SJReader reader, SJReader.Value v)
        {
            switch (v.type)
            {
                default:
                    throw new ArgumentException($"Invalid type {v.type}", nameof(v));

                // ... cases for other SJTypes available in 1.x
                // ❌ : Will fail on an event of SJType.Key!
                case SJType.String:
                    Console.WriteLine(new string(v.Slice()));
                    break;
            }
        }
        static void PassProcess(SJReader reader, SJReader.Value v)
        {
            switch (v.type)
            {
                default:
                    throw new ArgumentException($"Invalid type {v.type}", nameof(v));

                // ... cases for other SJTypes available in 1.x
                // ✅ : Will not fail on an event of SJType.Key
                case SJType.Key:
                case SJType.String:
                    Console.WriteLine(new string(v.Slice()));
                    break;
            }
        }

        (Action<SJReader, SJReader.Value> Process, bool MustThrow)[] processes = [(FailProcess, true), (PassProcess, false)];
        foreach (var p in processes)
        {
            var (process, mustThrow) = p;
            try
            {
                var reader = new SJStringReader(Data);
                for (var v = reader.Read(); !reader.ended; v = reader.Read())
                {
                    switch (v.type)
                    {
                        case SJType.Object:
                            {
                                while (reader.IterateObject(v, out var key, out var val))
                                {
                                    process(reader, key);
                                    process(reader, val);
                                }
                                break;
                            }
                        case SJType.Array:
                            {
                                while (reader.IterateArray(v, out var val))
                                {
                                    process(reader, val);
                                }
                                break;
                            }
                    }
                }
                if (mustThrow)
                {
                    Assert.Fail("Must throw according to the migration");
                }
            }
            catch (ArgumentException)
            {
                if (!mustThrow)
                {
                    Assert.Fail("Must not throw according to the migration");
                }
            }
        }
    }
    [TestMethod("Migration 1.x -> 2.x | 3: Handle SJType.Comment")]
    public void Migration1xTo2x_3()
    {
        static void Process(SJReader reader, SJReader.Value v)
        {
            switch (v.type)
            {
                default:
                    throw new ArgumentException($"Invalid type {v.type}", nameof(v));

                // ... cases for other SJTypes available in 1.x
                // ❌ : Will fail on a case of SJType.Comment (and Key)!
                case SJType.Number:
                    Console.WriteLine($"Number: {new string(v.Slice())}");
                    break;
                case SJType.Array:
                    while (reader.IterateArray(v, out var av))
                    {
                        Process(reader, av);
                    }
                    break;
            }
        }

        const string Data = @"// I decided to allow comments for some reason
[1.0, 2.0]
// Oh well..
";
        var reader = new SJStringReader(Data)
        {
            allowComments = true,
            captureComments = true // Not knowing we are interacting with older code
        };

        Assert.ThrowsException<ArgumentException>(() =>
        {
            // And this deserialization code is not aware of "captureComments"
            // ❌ : The Read will have SJType.Comment supplied into it!
            var root = reader.Read();
            Process(reader, root);
        });
        reader.Reset();
        // ↓ Instead do this
        for (var root = reader.Read(); !reader.ended; root = reader.Read())
        {
            // ✅ : Discard comments when interacting with code that disallow or not expect it
            if (root.type == SJType.Comment) continue;
            Process(reader, root);
        }
    }
    // 4: Change that the compiler will error out for
    // 5: New feature that is tested elsewhere
    // 6: New feature that is tested elsewhere

    // - Migration 2.x -> 3.x
    // 1. Change that the compiler will error out for
    // 2. Change that the compiler will error out for
    // 3. Change that the compiler will error out for
#pragma warning restore IDE0059 // Unnecessary assignment of a value
}
