#nullable disable
#pragma warning disable IDE0005
#pragma warning disable IDE0090
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Demonstrates the non-standard extensions other than the comments and the correct way of reading documents.
    /// <br>This is a more involved demo as it crams a <see cref="SJTree"/>-like structure to a 
    /// <see cref="Dictionary{TKey, TValue}"/> to inspect the objects a little easier.</br>
    /// </summary>
    sealed class ReaderEx
    {
        // A generic reader loop that will write the output into a Dictionary, alongside it's convenience type.
        // If you plan to use this seriously, I recommend doing something like SJTree instead of this.
        // (how ironic, this is probably better than the current BXSave in terms of performance, maybe.)
        private sealed class ValueOrList : IEnumerable<SJReader.Value>
        {
            private SJReader.Value _Value;
            public SJReader.Value Value => _Value;
            private readonly Lazy<List<SJReader.Value>> _Values = new Lazy<List<SJReader.Value>>();
            public SJReader.Value NonMetaValue => this.FirstOrDefault(v => v.type
                switch { SJType.Comment => false, SJType.Type => false, _ => true }
            );

            public int Count { get; private set; }

            public void Add(SJReader.Value v)
            {
                if (++Count > 1)
                {
                    _Values.Value.Add(v);
                }
                else
                {
                    _Value = v;
                }
            }
            public void Add(ValueOrList vs)
            {
                if (vs is null) return;
                foreach (var v in vs) Add(v);
            }

            public IEnumerator<SJReader.Value> GetEnumerator()
            {
                if (Count > 0)
                {
                    yield return _Value;
                    if (_Values.IsValueCreated)
                    {
                        for (int i = 0; i < _Values.Value.Count; i++) yield return _Values.Value[i];
                    }
                }
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>
        /// Reads the contents loaded on <paramref name="reader"/> into resulting dictionary.
        /// <br>The stored format is like this:</br>
        /// <c><br>/</br></c> is always the root
        /// <c><br>/path/to/value</br></c> for keyed values
        /// <c><br>/arrays/use/sequential/numbers/0/2</br></c>
        /// </summary>
        private static Dictionary<string, ValueOrList> ReadIntoDictionary(SJReader reader)
        {
            const string Root = "/";
            var dict = new Dictionary<string, ValueOrList>()
            {
                { Root, new ValueOrList() }
            };
            for (SJReader.Value root = reader.Read(); !reader.S.ended; root = reader.Read())
            {
                if (root.type == SJType.Comment)
                {
                    dict[Root].Add(root);
                    continue;
                }

                // Root seperator is appended when going towards children.
                ReadIntoDictionary(reader, root, Root, dict);
            }

            return dict;
        }
        /// <inheritdoc cref="ReadIntoDictionary(SJReader)"/>
        private static void ReadIntoDictionary(SJReader reader, SJReader.Value value, in string path, in Dictionary<string, ValueOrList> dict)
        {
            if (!dict.TryGetValue(path, out var l))
            {
                l = new ValueOrList();
                dict.Add(path, l);
            }

            switch (value.type)
            {
                case SJType.Error:
                    Debug.Fail($"SJ reader error occured : {reader.Error}");
                    return;

                case SJType.End:    // Can be safely ignored
                    break;

                case SJType.Key:    // Implicitly appended to "path", so is not executed
                case SJType.Number:
                case SJType.String:
                case SJType.Bool:
                case SJType.Null:
                case SJType.Comment:
                case SJType.Type:
                    // Add the related or the value entry.
                    l.Add(value);
                    break;

                case SJType.Object:
                    {
                        string keyStr = null;
                        var vol = new ValueOrList(); // For the first entries until a key is found.
                        while (reader.IterateObjectEntries(value, out var kvo))
                        {
                            switch (kvo.type)
                            {
                                case SJType.Key:
                                    // Key type is irrelevant for this instance as it's just used for indexing.
                                    keyStr = path == "/" ? $"{path}{new string(kvo.Slice())}" : $"{path}/{new string(kvo.Slice())}";
                                    if (vol != null)
                                    {
                                        dict.Add(keyStr, vol);
                                        vol = null;
                                    }
                                    break;

                                default:
                                    if (keyStr == null)
                                    {
                                        vol.Add(kvo);
                                    }
                                    else
                                    {
                                        ReadIntoDictionary(reader, kvo, keyStr, dict);
                                    }
                                    break;
                            }
                        }
                        break;
                    }
                case SJType.Array:
                    {
                        int index = 0;
                        while (reader.IterateArrayEntries(value, out var vo))
                        {
                            string keyStr = path == "/" ? $"{path}{index}" : $"{path}/{index}";
                            ReadIntoDictionary(reader, vo, keyStr, dict);
                            // Non values shouldn't increment index and should be related to the upcoming value.
                            // If nothing exists within the array, the inner metadata should be contained in a dummy index (0)
                            switch (vo.type)
                            {
                                case SJType.Comment:
                                case SJType.Type:
                                    break;
                                default:
                                    index++;
                                    break;
                            }
                        }
                        break;
                    }
            }
        }
        /// <summary>
        /// Checks whether if key <paramref name="path"/> is relative to <paramref name="parent"/> within <paramref name="maxLevel"/> depths.
        /// <br>/foo, /a/b/c =&gt; returns <see langword="false"/></br>
        /// <br>/a/b, /a/b/c =&gt; returns <see langword="true"/> if <paramref name="maxLevel"/> &gt;= 1</br>
        /// <br>/a/b, /a/b/c/d =&gt; returns <see langword="true"/> if <paramref name="maxLevel"/> &gt;= 2</br>
        /// <br>Setting <paramref name="maxLevel"/> to 0 or lower will return <see langword="true"/> within any levels of depth.</br>
        /// </summary>
        private static bool IsChildOfParent(string parent, string path, int maxLevel = 1)
        {
            if (string.IsNullOrEmpty(parent)) return false;
            if (string.IsNullOrEmpty(path)) return false;

            // Psuedo '/' in the parent for the case if it does not end with '/' without allocating a new string.
            int psuedoParentLength = parent.Length + (parent.EndsWith('/') ? 1 : 0);
            bool startsWith = path.Length >= psuedoParentLength;
            for (int i = 0; startsWith && i < psuedoParentLength; i++)
            {
                char c = i >= parent.Length ? '/' : parent[i];
                if (path[i] != c)
                {
                    startsWith = false;
                }
            }

            if (!startsWith) return false;
            else if (maxLevel <= 0) return true;

            for (int i = path.Length - 1, levels = 0; i >= parent.Length; i--)
            {
                if (path[i] == '/' && ++levels > maxLevel)
                {
                    return false;
                }
            }
            return true;
        }

        public static void TMain(string[] args)
        {
            var reader = new SJStringReader
            {
                // These extra options allow reading invalid json, but valid bxsj. All of these will be demoed in order.
                allowComments = true,
                allowOtherKeyTypes = true,
                allowTrailingComma = true,
                allowTypes = true
            };

            // ---
            // # Comments
            // They are demonstrated in ReaderJSC, but here's a primer:
            // * They can be anywhere and can be root values.
            // * Are always captured under SJType.Comment (except for non-entry based object/array reader)
            const string CommentData = @"
// Begin video settings
{
    /* Sets the resolution for the settings */
    ""resolution"": { ""width"": 2560, ""height"": 1440, ""vSync"": 1 },
    /* Texture quality. 0 = lowest, 5 = highest, value changes maximum resolution used and priorities of some textures. */
    ""textureQuality"": 5,
    /* Texture quality, is bit flag. 1 enables Bloom, 2 enables DoF, 4 enables ssao */
    ""postProcessQuality"": 5, // 0b101
    /* Anti aliasing quality. Assume it's MSAA bla bla I love larping */
    ""antiAliasing"": 4,
}
// End of video settings";
            reader.Data = CommentData;
            {
                // "values" will become stale as soon as reader.Data changes due to how ValueOrList is implemented.
                // This is why it is recommended to treat SJReader.Value as perishable rather than to store it forever. It is better to copy it.
                // (when rust port so that it's a compiler error :P)
                // ReadIntoDictionary uses paths like "/path/to/object", root "/" contains root level comments as well.
                // Check "ReaderEx
                Dictionary<string, ValueOrList> values = ReadIntoDictionary(reader);
                // We can now inspect the values, as long as the reader.Data stays the same or we supply CommentData.
                // SJReader.Value's are considered somewhat perishable ranges, but can be used with caution or be converted to strings.
                foreach (var relatedEntries in values["/resolution"])
                {
                    switch (relatedEntries.type)
                    {
                        case SJType.Comment:
                            Console.WriteLine($"Comment (of resolution) says: <{new string(relatedEntries.Slice())}>");
                            break;
                        case SJType.Object:
                            // We know what the children for this is.
                            // Note that the entries do not contain info about the children, only itself.
                            // Since we added the children with "/", we can iterate those as well:
                            foreach (var child in
                                /* Used to get the first layer of children, as it could contain deeper objects. → */
                                values.Where(kv => IsChildOfParent("/resolution", kv.Key)
                            ))
                            {
                                // Keys are appended only to the dictionary, these will provide the value:
                                var childValue = child.Value.NonMetaValue;
                                Console.WriteLine($"Value at path <{child.Key}> = <{new string(childValue.Slice())}>");
                            }
                            break;
                        default:
                            Assert.Fail($"Unexpected entry value on comment demo : '{relatedEntries}'");
                            return;
                    }
                }
                // Or we can directly access a relevant child value as :
                Console.WriteLine(values["/textureQuality"].NonMetaValue);
            }

            // ---
            // # Other key types
            const string OtherKeyTypesData = @"{
    ""otherKeyTypes"": [
        ""numbers"", ""booleans"", ""and null."",
        ""yeah, somewhat disappointing, but whatever recursing key would somewhat turn this into a new gate to hell"",
        ""non-standard varieties of those values are supported (ex: 0x1234, 0b111010101, 0o727, etc.)""
    ],
    true: ""true"",
    false: ""false"",
    1234: ""yes"",
    6.7e+28: ""very large"",
    0xF00F: [0xF0, 0x0F],
    0b1001: false
}";
            reader.Data = OtherKeyTypesData;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Object) continue;
                    while (reader.IterateObject(value, out var k, out var v))
                    {
                        if (v.Length < 0) continue;
                        Console.WriteLine($"Key <{new string(k.Slice())}>, Current type <{k.type}>, Prev type <{k.prevType}>");
                        Console.WriteLine($"Value <{new string(v.Slice())}>");
                    }
                }
            }

            // ---
            // # Trailing comma
            const string TrailingCommaData = @"{
    ""trailingCommas"": [""are"", ""very"", ""fun"",],
    ""makesItFeel"": { ""like"": ""a proper"", ""what"": ""language!"",},
    ""ifIGoInsane"": ""i may add jq inside this"",
    ""butUnfortunately"": ""i'm not that smart :("",
    ""alsoWorksOn"": [
        ""your .NET standard 2.1's and maybe mono. but the testing suite doesn't so eh :shrug:"",
        { ""&"": ""nested objects"", ""wow"": [true, false, 4, { ""maybe"": [0, 1, 0, 0,], },], },
    ],
}";
            reader.Data = TrailingCommaData;
            {
                // We know that the SJReader will validate everything within the value,
                // even if we don't iterate the object. This must fail if it didn't allow trailing comma.
                try
                {
                    reader.allowTrailingComma = false;
                    reader.ThrowOnError = true;

                    for (var value = reader.Read(); !reader.S.ended; value = reader.Read()) ;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Caught trailing comma related error: {e}");
                }
                finally
                {
                    // Must not fail now
                    reader.Reset();
                    reader.allowTrailingComma = true;
                    reader.ThrowOnError = false;

                    for (var value = reader.Read(); !reader.S.ended; value = reader.Read()) ;
                }
            }

            // ---
            // # Types
            // * Types are prefix to any non-metadata entry (including the types themselves).
            // * There can be only one type per value or key.
            // * They are the "really non-standard metadata extension" BXSJ has as of 3.0, making it distinct.
            // * Literals have to match SJReader.IsTypeLiteralContinuation (or any partial regex). Strings are, well, strings.
            // * Captures contain the type data rather than it with it's tokens.
            const string TypeData1 = @"(MyObject){
    ""literal"": (Literal)""yes"",
    ""nonLiteral"": (""Non literal that can contain (\"")"")""ok"",
    ""typeCode"": (0x1234)""Considered a literal"",
    (WithKeyType)""k"": (AndValueType)null
}",
            TypeData2 = @"(""System.Array, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral"")[
    (Number)1, (Number)2, (AreWeSure)3, (Number)""don't mind me"", (Number)4, (NotNumber)5
]";
            reader.Data = TypeData1;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Object) continue;
                    SJReader.Value lastType = SJReader.Value.Error(),
                        lastKey = SJReader.Value.Error();
                    while (reader.IterateObjectEntries(value, out var kvt))
                    {
                        if (kvt.Length < 0) continue;

                        switch (kvt.type)
                        {
                            case SJType.Comment:
                                break;

                            case SJType.Type:
                                lastType = kvt;
                                break;
                            case SJType.Key:
                                Console.WriteLine($"Key ({(lastType.Length > 0 ? new string(lastType.Slice()) : "")})<{new string(kvt.Slice())}>");
                                lastType = default;
                                break;
                            default:
                                Console.WriteLine($"Value ({(lastType.Length > 0 ? new string(lastType.Slice()) : "")})<{new string(kvt.Slice())}>");
                                lastType = default;
                                break;
                        }
                    }
                }
            }
            reader.Data = TypeData2;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Array) continue;
                    SJReader.Value lastType = SJReader.Value.Error();
                    while (reader.IterateArrayEntries(value, out var vt))
                    {
                        if (vt.Length < 0) continue;

                        switch (vt.type)
                        {
                            case SJType.Comment:
                                break;

                            case SJType.Type:
                                lastType = vt;
                                break;
                            default:
                                Console.WriteLine($"Value ({(lastType.Length > 0 ? new string(lastType.Slice()) : "")})<{new string(vt.Slice())}>");
                                lastType = default;
                                break;
                        }
                    }
                }
            }
            throw new Exception("Type does not work.");
        }
    }
}

#pragma warning restore IDE0005
#pragma warning restore IDE0090
#nullable restore
