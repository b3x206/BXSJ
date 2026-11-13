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
                    default: throw new Exception($"Invalid Vector3 key : {key}");
                }
            }
            return result;
        }

        const string data = @"{ ""x"": 42.0, ""y"": -42.0, ""z"": 0.0 }";
        var reader = new SJReader(new SJStringData(data));

        for (SJReader.Value root = reader.Read(); !reader.S.ended; root = reader.Read())
        {
            // Can conditionally skip the irrelevant values here if it exists.
            Console.WriteLine($"Saved Position : {ReadVector3(reader, root)}");
        }
    }

    [TestMethod]
    public void UnescapeSnippet()
    {
        const string source = "Hello world! Back slash: \\, Quote: \"";
        string escape = SJEscape.Escape(source);
        string result = SJEscape.Unescape(escape);
        Console.WriteLine($"Escaped: <{escape}>, Unescaped: <{result}>");

        // -- Assertions
        Assert.AreEqual(source, result);
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
        var initialPosition = new Vector3(1f, 2f, 3f);
        var position = initialPosition;

        var writer = new SJStringWriter()
        {
            // Enable pretty printing
            indentSize = 4,
        };
        using (writer.Object())
        {
            writer.WriteKV("X", position.X);
            writer.WriteKV("Y", position.Y);
            writer.WriteKV("Z", position.Z);
        }

        // Now deserialize it
        position = Vector3.Zero;

        string data = writer.ReadData();
        var reader = new SJReader(new SJStringData(data));
        position = ReadVector3(reader, reader.Read());

        // Use ReadData to read the resulting string.
        Console.WriteLine($"Saved Position : {data}, Read Position : {position}, Equals : {initialPosition == position}");

        // -- Assertions
        Assert.AreEqual(initialPosition, position);
    }

    // - Migration 1.x -> 2.x
    // 1: Change that the compiler will error out for anyways. This is just to validate the code changes as I use rename identifier.
    [TestMethod(DisplayName = "Migration 1.x -> 2.x | 1: Move from SJ to BX.SJ")]
    public void Migration1xTo2x_1()
    {
        // Check if the BX.SJ assembly has anything in the SJ namespace
        Assert.DoesNotContain(v => v.Namespace?.StartsWith("SJ") ?? true, typeof(SJReader).Assembly.GetTypes(), "Must not have any namespace start with SJ or have null/root namespace");
    }
    [TestMethod(DisplayName = "Migration 1.x -> 2.x | 2: Handle SJType.Key")]
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
                var reader = new SJReader(new SJStringData(Data));
                for (var v = reader.Read(); !reader.S.ended; v = reader.Read())
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
#pragma warning disable MSTEST0058 // Do not use asserts in catch blocks
                    Assert.Fail("Must not throw according to the migration");
#pragma warning restore MSTEST0058 // Do not use asserts in catch blocks
                }
            }
        }
    }
    [TestMethod(DisplayName = "Migration 1.x -> 2.x | 3: Handle SJType.Comment")]
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
        var reader = new SJReader(new SJStringData(Data))
        {
            allowComments = true,
            // default behaviour captures comments now
        };

        // And this deserialization code is not aware of the default capture comments behaviour
        // ❌ : The Read will have SJType.Comment supplied into it!
        var root = reader.Read();
        Assert.Throws<ArgumentException>(() => Process(reader, root));
        reader.Reset();
        // ↓ Instead do this
        for (root = reader.Read(); !reader.S.ended; root = reader.Read())
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
    // *: Change that the compiler will error out for
#pragma warning restore IDE0059 // Unnecessary assignment of a value
}
