# BX.SJ

JSON compatible parser + writer for C#.

Loosely based on [sj.h](https://github.com/rxi/sj.h).

This branch (`devel`) is where development is done, before getting packed into "two files". Development of the repository is done by using `git worktree` on seperate directories. This branch can have incomplete code pushed into it, so use with caution! Only the `devel` commits that I tag are "complete"<sup>[Notes##3](#notes)</sup>.

## Notes

1. This will likely _never be_ a **"100% correct" parser**, also what it does with the data (outside of the JSON tokens) ultimately depends on what you read from the parsed chunks. <br>
  It also _may fail_ on correct or _may not fail_ on wrong sequences. **Use at your own risk.** <br>
  Known things:
  * More loose with number formatting, anything starting with digit and having a valid character is considered ok.

2. Because of
  * C#-ifying the code
  * Improving "correctness"
  * And adding comment support.
  * With more tests, as it will be (one of) the backbone(s) for my save system. <br>
  The code is **more complicated and less concise.**

3.  "Stable" SJ releases are the ones that are tagged with semver. <br>
_(all tests pass, benchmarks are consistent, slightly worse or slightly improved and it doesn't regress too much)_ <br>

## Examples

### Reader

Use the [`SJStringReader`](./SJ/SJStringReader.cs) class to get started.

```cs
// A basic object deserialization example. Non-recursive.
using BX.SJ;
using System;
using System.Numerics;
using System.Globalization;

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
```

If processing comments are desired, enable `SJReader.captureComments` and read document as shown: <br>
```cs
using BX.SJ;
using System;

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
    // Comments in JSON doesn't make much sense actually now that I have realized. Just use ini
    /* @summary This field is 42, and actually you can put this within the JSON, but whatever.. */
    ""is42"": 42,
    /* @summary And this field is 67, you know why.. */
    ""is67"": 67
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
```

---

Unlike the original SJ, there is also a string escaping implementation if desired:

```cs

// ...
switch (SJReader.Value.type)
{
    // ...
    case SJType.String:
    {
        string result = SJEscape.Unescape(SJReader.Value.Slice());
        // `SJEscape.Escape(string | ReadOnlySpan<char>)` is for the other way around...
        // If you want to allocate less, use SJEscape.Unescape with the append delegate.
        // In future I may add something like "Escaper" with it's state like "Encoder"
        break;
    }
}
```

This is used as the default string escape for the Writer.

### Writer

Use the [`SJStringWriter`](SJ/SJStringWriter.cs) class to get started.

```cs
using BX.SJ;
using System;
using System.Numerics;
using System.Globalization;

static Vector3 ReadVector3(SJReader reader, SJReader.Value root) { /*...*/ } // Copy the method from above Vector3 reader

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
```

### Benchmarks

* Read = Read a [small file (valid.json)](TestFiles/valid.json) 256 times
* ReadLarge = Read a [large file (5mb.json)](TestFiles/5mb.json) 32 times
* Write = Write few kb of JSON (`{ "random_key": "random_value", ... 512 entries }`) 256 times
* WriteLarge = Write ~500kb JSON (`{ "random_key": "random_value", ... 16384 entries }`) 32 times
* Baseline = Do the same action, just without processing.

Classes used are [`SJStringReader`](./SJ/SJStringReader.cs) and [`SJStringWriter`](SJ/SJStringWriter.cs)

```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
13th Gen Intel Core i7-13620H 2.92GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.421
  [Host]     : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 8.0.27 (8.0.27, 8.0.2726.22922), X64 RyuJIT x86-64-v3

```

#### ReaderBenchmarks

| Method             | Mean        | Error     | StdDev    | Ratio      | RatioSD  | Allocated | Alloc Ratio |
|--------------------|------------:|----------:|----------:|-----------:|---------:|----------:|------------:|
| ReadBaseline       |   0.0392 ms | 0.0001 ms | 0.0001 ms |       1.00 |     0.00 |         - |          NA |
| Read               |  36.6612 ms | 0.2419 ms | 0.2263 ms |     935.90 |     6.17 |         - |          NA |
|                    |             |           |           |            |          |           |             |
| ReadLargeBaseline  |   3.5105 ms | 0.0200 ms | 0.0177 ms |       1.00 |     0.01 |         - |          NA |
| ReadLarge          | 408.2533 ms | 1.4103 ms | 1.3192 ms |     116.30 |     0.67 |         - |          NA |

#### WriterBenchmarks

| Method             | Mean        | Error     | StdDev    | Ratio      | RatioSD  | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------:|----------:|----------:|-----------:|---------:|-------:|----------:|------------:|
| WriteBaseline      |   0.0005 ms | 0.0000 ms | 0.0000 ms |       1.00 |     0.01 | 0.0134 |     168 B |        1.00 |
| Write              |  11.0221 ms | 0.1107 ms | 0.1036 ms |  23,509.18 |   252.60 |      - |     168 B |        1.00 |
|                    |             |           |           |            |          |        |           |             |
| WriteLargeBaseline |   0.0001 ms | 0.0000 ms | 0.0000 ms |       1.00 |     0.00 | 0.0134 |     168 B |        1.00 |
| WriteLarge         |  45.7750 ms | 0.1747 ms | 0.1548 ms | 575,208.09 | 2,453.84 |      - |     168 B |        1.00 |


**Note:** I'm not really good at writing benchmarks and these were not tested in 100% optimal situation (hence the ratio being too large on some "benches").
Also, the speed could be worse than this as other code will run alongside it.

_I speculate that_ the lack of performance could be caused by excessive bound checking + redundant state for the Reader, the Escape that Writer does and other many factors (using netstandard2 build could also contribute too, maybe.)..

---

More examples are in the [`./SJ/Examples`](./SJ/Examples) directory. <br>
These files are not included for compiling, you can copy and paste it normally.

You can also use the [unit tests](./SJ.Tests) as examples too.

---

## But why?
* Because it's for older versions of Unity, Godot and other C# runtimes without good access to System.Text.Json or other JSON libraries.
* Newtonsoft and other behemoth JSON libraries (incl. the System.Text.Json) are not really friendly to game engine runtimes (eg. AOT, Assembly load/unload friendliness, etc.). Source generators solve this but it's generally not a simple "plug and play" solution (meaning I don't need it or it's out of scope for this repository).
* It's meant to be simple and stable but low level meaning it can be a pinned dependency. (Large updates will always increment versions and I will try my best to make first releases not need minor updates)
* And why is all of the state public? <br>
  \> I like not using reflection to access object's properties. Of course, it is "more dangerous" and prone to misuse, but it allows for better extensions and analysis (especially if you distribute it in a way hard to change the library like "dll" or "upm package").

## Changelog
### - 1.0.0
* Initial release as "SJ".
* Somewhat janky unit tests, implicit/automated validation of files that could be partially broken (is too permissive).
* But still functional and can technically parse large valid JSON documents and JSC (Comments not captured).
* Released with the same Reader and Writer.

### - 2.0.0
* Rename to "BXSJ"

**▶ SJReader**
* More explicit API (but migrating to explicitness will likely not break older code, except for the new Key type)
* Reader now asserts correctness better
* Stack management is explicit
* Can capture `SJType.Comment` blocks if comments are allowed (opt-in via `SJReader.captureComments = true`)

**▶ SJWriter**
* Writer now asserts correctness better
* Can write comments (opt-in via `SJWriter.allowComments = true`)

**▶ SJEscape**
* String escaping is now faster
* Removed UCS-2 style escaping surrogate pairs (for speed reasons)

**▶ Tests**
* Allow supplying of a custom class inheriting SJ classes
* Add more coverage
* Ensure the logic matches the usage and changes, doing logic now requires 

**▶ Benchmarks**
* Allow supplying of a custom class inheriting SJ classes
* Improve writer benchmark

The inner workings have been reworked partially..

<details>

<summary>• Migration from 1.x -> 2.x </summary>

1. **Everything has been moved from `SJ` to `BX.SJ` namespace.**
```cs
using SJ;    // ❌
using BX.SJ; // ✅
```

2. **Object keys now return an explicit value of `SJType.Key`!** <br>
   This is easy to migrate from, if your `switch` or `if` cases check for `SJType.String` as key, it can be simply interchanged to it such as:
```cs
static void Process(SJReader reader, SJReader.Value v)
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

// ...
while (reader.IterateObject(obj, out var k, out var v))
{
    // ↓ Will throw! However, if you do not care about the type of the key, 
    //   this isn't a problem as it will behave exactly like SJType.String.
    Process(reader, k);
    Process(reader, v);
}

// ↓ Instead do this
static void Process(SJReader reader, SJReader.Value v)
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
```
3. **Only applicable if you have used `SJReader.allowComments = true`, even with that there are cases where the old implicit behaviour is used.** <br>
   Because any `SJReader` may have it's `captureComments` set as `true`, (which may require explicit calls to `Read` while `!SJReader.ended`), 
   in a case where a "comment" is encountered on the root level, it will be not automatically skipped and will be pushed into your main reader function as a value.
   This may cause problems in such cases like:
```cs
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

// And this deserialization code is not aware of "captureComments"
// ❌ : The Read will have SJType.Comment supplied into it!
var root = reader.Read();
Process(reader, root);

// ↓ Instead do this
for (var root = reader.Read(); !reader.ended; root = reader.Read())
{
    // ✅ : Discard comments when interacting with code that disallow or not expect it
    if (root.type == SJType.Comment) continue;
    Process(reader, root);
}
```

4. The inner workings of custom `SJReader` and `SJWriter` has changed (generally the access modifiers):
```cs
// For SJReader:
// (the behaviour isn't changed, only the access modifiers)
protected override char At(int i); /* → */ public override char At(int i);
protected override ReadOnlySpan<char> Slice(int start, int length); /* → */ public override ReadOnlySpan<char> Slice(int start, int length);

// For SJWriter:
// Add these two, change accordingly to your data source:
public override bool CanReadData => true;
public override string ReadData() => sb.ToString();

// Clear written data on Reset()
public override void Reset()
{
    base.Reset();
    sb.Clear();
}

// ⚠️ : ToString() should no longer return the data written by the SJWriter, instead it should return state if relevant.
// ❌ : public override string ToString() { return ReadData(); }
```

5. Writing comments with `SJWriter` is easier now, you no longer need to use hacky `SJWriter.WriteLiteralValue`:
```cs
// 1. Enable it
writer.allowComments = true;
// 2. Write it (if you don't enable it before it will throw)
writer.WriteComment("This is a comment!");
writer.WriteCommentLine("This is a comment that starts with //!");

// Note : Writer tracks whether if a comment is written (through WrittenComment). If commented json is undesired you 
// can "false assert" that. **However it won't capture any indirect way of writing comments, so be careful!**
```

6. Unit tests have been changed to support custom `SJReader` and `SJWriter` classes (for extensions in future), with more coverage and cases to test.
```cs
namespace MySJEx.Tests;

// Reader
[TestClass]
public sealed class CustomReaderTests : ReaderUnitTests<SJCustomReader>
{
    public override SJCustomReader CreateFromString(string data) => new(new MemoryStream(Encoding.UTF8.GetBytes(data)), Encoding.UTF8);
    public override SJCustomReader CreateFromStream(Stream data, Encoding? enc) => new(data, enc);
    // Some other bootstrap functions can be changed via overriding

    // Some optional tests are declared "virtual" so that you can apply [TestMethod]
    [TestMethod]
    public override void TestDisposeTwice()
    {
        base.TestDisposeTwice();
    }
}

// Writer
[TestClass]
public sealed class CustomWriterTests : WriterUnitTests<SJCustomWriter>
{
    public override SJCustomWriter CreateWriter() => new();
    // Some other bootstrap functions can be changed via overriding

    // (similar optional tests exist on Writer)
}
```

From 1.x, everything else is more or less the same, except the inner workings of the reader and writer. <br>
**Note:** However in the future releases, implicit behaviour _could be_ removed. It is recommended to use the new way (with `for`) of iterating objects and arrays.

</details>

## TODO
* [ ] Port tests to xunit as mstest seems somewhat specific to visual studio and can't be ran headless trivially (2.1.x) <br>
  This should also improve the tests and place most logic into one manageable place, instead of repeating redundant logic..
* [ ] Improve performance by reducing redundant branching/bounds checking and manage state slightly better.
