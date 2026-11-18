# BX.SJ

JSON **compatible** parser (lexer) + formatted writer for C#.

Loosely based on [sj.h](https://github.com/rxi/sj.h).

This branch (`devel`) is where development is done, before getting packed into "two files". Development of the repository is done by using `git worktree` on seperate directories. This branch can have incomplete code pushed into it, so use with caution! Only the `devel` commits that I tag are considered "completed".

## Notes

1. This will likely _never be_ a **"100% correct" parser**, also what it does with the data (outside of the JSON tokens) ultimately depends on what you read from the parsed chunks. <br>
  It also _may fail_ on correct or _may not fail_ on wrong sequences. **Use at your own risk.** <br>
  It has **non standard** extensions to the JSON, so it is happy with reading or writing non-standard data.

2.  "Stable" SJ releases are the ones that are tagged with semver. You will generally see major releases as I generally refactor things and don't backport bugfixes (if there are bugfixes). <br>
_(all tests pass, benchmarks are consistent, slightly worse or slightly improved and it doesn't regress too much)_ <br>

3. It isn't very simple and has incompatible extensions.

4. This branch is currently in development. The readme will elaborate more things in future. This is the minimal readme.

## Examples

### Reader

Use the [`SJStringReader`](./SJ/SJStringReader.cs) class to get started.

```cs
// A basic object deserialization example, non-recursive.
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
            default: throw new Exception($"Invalid Vector3 key : {key}");
        }
    }
    return result;
}

const string data = @"{ ""x"": 42.0, ""y"": -42.0, ""z"": 0.0 }";
var reader = new SJReader(new SJStringReader(data));

for (SJReader.Value root = reader.Read(); !reader.S.ended; root = reader.Read())
{
    // Can conditionally skip the irrelevant values here if it exists.
    Console.WriteLine($"Saved Position : {ReadVector3(reader, root)}");
}
```
---

Unlike the original SJ, there is also a string escaping implementation if desired:

```cs
const string source = "Hello world! Back slash: \\, Quote: \"";
string escape = SJEscape.Escape(source);
string result = SJEscape.Unescape(escape);
Console.WriteLine($"Escaped: <{escape}>, Unescaped: <{result}>");
```

This is used as the default string escape for the Writer.

### Writer

Use the [`SJStringWriter`](SJ/SJStringWriter.cs) class to get started. This allows structured writing with validations.

```cs
using BX.SJ;
using System;
using System.Numerics;
using System.Globalization;

static Vector3 ReadVector3(SJReader reader, SJReader.Value root) { /*...*/ } // Copy the method from above Vector3 reader

// Serialize an arbitrary Vector3
var initialPosition = new Vector3(1f, 2f, 3f);
var position = initialPosition;

var writer = new SJWriter(new SJStringWriter())
{
    // Enable pretty printing
    indentSize = 4,
};
using (writer.Object())
{
    writer.WriteKey("X").Write(position.X);
    writer.WriteKey("Y").Write(position.Y);
    writer.WriteKey("Z").Write(position.Z);
}

// Now deserialize it
position = Vector3.Zero;

string data = writer.Data.ReadWritten();
var reader = new SJReader(new SJStringReader(data));
position = ReadVector3(reader, reader.Read());

// Use ReadData to read the resulting string.
Console.WriteLine($"Saved Position : {data}, Read Position : {position}, Equals : {initialPosition == position}");
```

<!--
### Benchmarks

(This is outdated, it should be done again in a later time with fixes. Or I will remove it)
(It works, thus it is a good code. BXSJ is _very_ singlethreaded and does not read into a temporary buffer / batch process with a defined word size to maybe speed up things. It reads whatever is in memory sequentially. Writer is _faster_ as it's rules are more basic)

* Read = Read a [small file (valid.json)](TestFiles/valid.json) 256 times
* ReadLarge = Read a [large file (5mb.json)](TestFiles/5mb.json) 32 times
* Write = Write few kb of JSON (`{ "random_key": "random_value", ... 512 entries }`) 256 times
* WriteLarge = Write ~500kb JSON (`{ "random_key": "random_value", ... 16384 entries }`) 32 times
* Baseline = Do the same action, just without processing but read/write it.

-->

---

More examples are in the [`./BX.SJ/Examples`](./BX.SJ/Examples) directory. <br>
These files are not included for compiling, you can copy and paste it normally.

You can also use the [unit tests](./BX.SJ.Tests) as examples too, but some of the tests are "convoluted" (even though it should be simple), so it's better to use the examples.

---

This README will be remade after the interface vs inheritance is profiled in the better benchmarks that I will write.

<!--
---

## But why?
* Because it's for older versions of Godot and other C# runtimes without good access to System.Text.Json or other JSON libraries. (Although there is no .NET Framework support. This is because I still sometimes use mono for some reason)
* Newtonsoft and other larger JSON libraries (incl. the System.Text.Json) are not really friendly to game engine runtimes (eg. AOT, Assembly load/unload friendliness, etc.). Source generators solve this but it's generally not a simple "plug and play" solution (meaning I don't need it or it's out of scope for this repository).
* It's meant to be ~~simple and~~ stable ~~but "low level"~~ meaning it can be a pinned dependency. (Large updates will always increment versions and I will try my best to make first releases not need minor updates)

## Changelog
### - 1.0.0
* Initial release as "SJ".
* Somewhat janky unit tests, implicit/automated validation of files that could be partially broken (is too permissive).
* But still functional and can technically parse large valid JSON documents and JSC (Comments were not captured).
* Released with the `SJStringReader` and `SJStringWriter`.

### - 2.0.0
* Rename to "BXSJ"

**▶ SJReader**
* More explicit API (but migrating to explicitness will likely not break older code, except for the new Key type)
* Reader now asserts correctness better
* Stack management is explicit
* Can capture `SJType.Comment` blocks if comments are allowed ~~(opt-in via `SJReader.captureComments = true`)~~ No longer opt-in as of 3.0.*

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

The inner workings have been partially redone..

**Note** : There has been a supposed "3.x" release with, however it should be treated more as "2.x bugfix with breaking changes". You can use that if you would like to rollback to a version that is simpler with some of these changes. It will exist as an intermediate commit in `master` for the time being. It could be turned into a "2.x compatible" system, although with `[ObsoleteAttribute]` that will still point to working things.

### - 3.0.0
**▶ SJReader**
* [X] Move all state to `ref State S`.
  * [X] Stacked reader state is moved to `BX.SJ.SJReader+State+Stacked` and is managed through `BX.SJ.Collections.ValueStack<T>`.
  * [X] `PushState`/`PopState` to save / load opaque `SJWriter` state.
  * [ ] Tests
* [X] Allow `SJType.Key` values outside of `SJType.String`, that aren't recursive. The slices are provided as is, but requires some minor code changes. <br>
  (can use `SJReader.Value.prevType` to check whether if the type was formerly bare [not `String`])
* [X] Allow trailing commas (`{ "a": "b", }`)
* [X] Allow custom key types (non recursive data for now, `{ 0x21498124: "b", }`)
* [X] Allow types on keys or values (`{ (CustomType)0x21498124: (0x1234)"b", }`)
  (Numbers or type names are allowed, type must pass regex similar to `\(\s*([A-Za-z0-9\-_=]*|"(?:[^"]|(?<=\\)")*")\s*\)`..)
  * [ ] Tests
* [X] Removed the non-captured comment reading (to ignore comments, just skip them if `SJReader.Value.type` is `SJType.Comment`, which is added to the code regardless for the potential of a `SJReader` with `allowComments`)
* [X] `SJType.Comment` slices now trim the prefix `//` or `/*`, the postfix `*/` and the surrounding whitespace. To determine the variety of the `SJType.Comment`, use `SJReader.Value.variant`.
* [X] More tolerant with tokens of type `SJType.Number`. <br>
  (octal [`0o`], hexadecimal [`0x`] and binary [`0b`] is now allowed, if prefixed with their respective sequence)
* [X] `PeekEntryCount` to peek the entries within any `SJType.Object` or `SJType.Array`.
* `SJType.Comment` now only includes the content contained within the `SJType.Comment`. This behaviour is not opt-in, .
* [X] `SJTypeVariant` to check the variant of value or comment to guess the value correctly. Currently this is useful with `SJType.Number` and `SJType.Comment`.
  * [ ] Tests
* [ ] **The class is no longer inheritable**, implement ISJReaderData to supply a reader.

**▶ SJWriter**
* [X] Move all state to `ref State S`.
  * [X] Rename `finish` to `ended` for consistency with `SJReader`.
  * [X] Stacked state is moved to `BX.SJ.SJWriter+State+Stacked` and is managed through `BX.SJ.Collections.ValueStack<T>`.
  * [X] `PushState`/`PopState` to save / load opaque `SJWriter` state.
  * [ ] Tests
* [X] Better checks for non-standard JSON outputs (`WrittenCustomKey`, `WrittenCustomValue`, etc.)
* [X] More checks to avoid accidental "unparseable by `SJReader`" output (specifically for number literals).
* [ ] Allow prefixes + automatic prefixing in formatted number writing.
* Allow _custom key types_, _type writing_ and _trailing commas_ (only happens if `WriteComment()'s hasNextValue` is wrongfully set `true`) <br>
  (↑ See `SJReader` changes)
  * [X] Custom key types
  * [X] Trailing commas
  * [ ] Custom types
  * [ ] Tests
* [X] Reduce formatted number writing boilerplate (by resorting to `static` local lambdas).
* [X] Built-in format buffer that moves from `stackalloc -> char[] -> ArrayPool<char>.Shared` (reuse of writer is better)

**▶ New**
* Reached a higher percentage of test coverage (for `bx.sj.dll`)
* Simplified and more focused tests that use minimal data. (This _is_ my first time writing unit tests, so there were _[and still are]_ things to fix)
* Tooling for creating the workspace / worktrees. <br>
  (This could be improved with bare repos + contained folder for SJ project files, but the current state is fine for this project.)
* `Collections.ValueStack<T>` to stack states on reader and writer. (It isn't recommended to use that struct outside of SJ related code due to the odd behaviour it has [because it's a non-ref value type]. For more information on that, check the tests it has.)
* `SJStreamReader` to read from a `System.IO.Stream` with encoding. If the file encoding is unsupported, it can be converted or be just interpreted as ANSI. It has a temporary buffer to convert the slice into a UTF-16 one.
* `SJStreamWriter` to write into a `System.IO.Stream` with encoding.
* `SJLinearWriter` to write into a contiguous region of `char[]` <br>
  (`StringBuilder` uses chunks, `SJStringReader` is almost matching to this although it does not use a typed `System.Array`).
> (These are backports from my other project named `BXSave`, due to the unnecessary testing complexity it brings to that)

<details>

<summary>• Migration from 2.x -> 3.x </summary>

3.x was just going to be a simple addition, but has turned out to be something much bigger, pointing out a fundamental design flaw in it's original design + many premature optimizations without profiling, turning SJ's internals into something complicated. This should be remedied in later 3.x releases.

1. All state has been reworked and moved into `S` on both `SJReader` and `SJWriter`.
```cs
var reader = new SJStringReader("67");
reader.Read();

Console.WriteLine(reader.ended);   // ❌
Console.WriteLine(reader.S.ended); // ✅

var writer = new SJStringWriter();
writer.WriteNumber(1234);

Console.WriteLine(writer.finish);  // ❌
Console.WriteLine(writer.S.ended); // ✅
```

State can be pushed and popped in an opaque manner. The interface extensions / sources for the reader / writer also has corresponding methods on `ISJStackable`.

3. All classes are `sealed` in favor of interfaces.
* This is one of the largest changes.

2. All data is now read / written through the `ISJDataReader` / `ISJDataWriter`. These are new interfaces that only care about the ascii codepoints. If you consider unicode codepoints (within the C# limit) as tokens, prefer using the 2.x version with additional fixes.
* Data sources' are no longer immutable to the `SJReader`'s inheriting type. It can be changed on the fly within the same reader. This is rarely practical outside of reusability, generally a single type is preferred for reasonable approaches (that isn't churning out other libraries and is actually developing an application).
* Both `SJReader` and `SJWriter` are `IDisposable` for their data reader / writer.

4. Seekless behaviour for reading / writing (reading all data of the writer still requires seeking), outside of slicing (as `SJReader.Value` is lazily evaluated. Can keep another buffer to create from, if it isn't desired to load all the file and create a slice) that is.

5. No more UPM Json files and other related engine metadatas. A specific branch will exist for a directly authorable version.

6. Updated MSTest to 4.x, which adds bunch of new things. Specifically,
  * `ExpectedExceptionAttribute` is no more, because I need to have more indirections apparently.

<!-- Filter for powershell script to postprocess README.md : /<!--\s*NO_RELEASE[\s\S]*END_NO_RELEASE\s*-->/gm -->
<!-- NO_RELEASE -->
<!--
7. Tests have been changed a lot.
* The generic type is now the data source, and future releases may add extra data sources.. I will design / plan this a little better so that mixed configurations could be better, but 3.0 release will just leave it similar to this..

8. The `printf`/`WriteLine` driven debugging is because `vsdbg` is hilariously slow. (I guess that's why `raddbg` exists. Too bad C# is _too different_ or I didn't bother checking if a `netcoredbg` backend exists.)
-->
<!-- END_NO_RELEASE -->
<!--
</details>

<details>

<summary>• TODO </summary>

1. [ ] Port tests to xunit as mstest seems somewhat specific to visual studio and can't be ran headless trivially <br>
  This should also improve the tests and place most logic into one manageable place, instead of repeating redundant logic..
2. [~] Use `ISJDataReader`/`ISJDataWriter` to write data (composition) instead of using inheritance.
3. [ ] UTF-8 (byte) reading/writing (Perhaps I could use the 'char' version as a basic UTF-8 version as that only converts the relevant tokens. Though the problem here becomes the slice spans + the endianness of the UTF-16 if I make the internals of BXSJ byte based..)
4. [ ] Improve readability and then improve performance. (Doing the first one will likely improve performance.)

<sup>Should have used the `BinaryWriter` instead...</sup>

</details>
-->
