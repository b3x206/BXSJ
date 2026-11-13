#nullable disable
#pragma warning disable IDE0005
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using static BX.SJ.Tests.Examples.ReaderInspect;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Demonstrates the non-standard extensions other than the comments and the correct way of reading documents.
    /// <br>This is a more involved demo as it crams a <see cref="SJTree"/>-like structure to a 
    /// <see cref="Dictionary{TKey, TValue}"/> to inspect the objects a little easier.</br>
    /// </summary>
    sealed class ReaderEx
    {
        public static void TMain(string[] args)
        {
            var data = new SJStringData();
            var reader = new SJReader(data)
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
            data.Data = CommentData;
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
            data.Data = OtherKeyTypesData;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Object) continue;
                    while (reader.IterateObject(value, out var k, out var v))
                    {
                        if (v.Length < 0) continue;
                        Console.WriteLine($"Key <{new string(k.Slice())}>, Current type <{k.type}>, Prev type <{k.prevType}> | Value <{new string(v.Slice())}>");
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
            data.Data = TrailingCommaData;
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
            reader.ThrowOnError = true;
            data.Data = TypeData1;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Object) continue;
                    SJReader.Value lastType = default;
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
            data.Data = TypeData2;
            {
                for (var value = reader.Read(); !reader.S.ended; value = reader.Read())
                {
                    if (value.type != SJType.Array) continue;
                    SJReader.Value lastType = default;
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
        }
    }
}

#pragma warning restore IDE0005
#nullable restore
