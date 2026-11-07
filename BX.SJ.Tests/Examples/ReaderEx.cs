#pragma warning disable IDE0005
using System;
using System.Diagnostics;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Demonstrates the non-standard extensions other than the comments and the correct way of reading documents.
    /// </summary>
    sealed class ReaderEx
    {
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
            // # Comments
            // They are demonstrated in ReaderJSC, but here's a primer:
            // * They can be anywhere and can be root values.
            // * Are always captured under SJType.Comment (except for non-entry based object/array reader)
            const string CommentData = @"
// Contains video settings
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
            for (SJReader.Value root = reader.Read(); !reader.S.ended; root = reader.Read())
            {
                if (root.type == SJType.Comment)
                {
                    Console.WriteLine($"Root level comment : {new string(root.Slice())}");
                    continue;
                }

                // We know that the max depth here will be 2, so we can write a special
                // reading "routine", instead of writing something recursive?
                while (reader.IterateObjectEntries(root, out SJReader.Value kv))
                {
                    SJReader.Value key = SJReader.Value.Error();
                    switch (kv.type)
                    {
                        case SJType.Error:
                            throw new Exception($"SJReader error : {reader.Error}");

                        case SJType.Key:
                            key = kv;
                            break;

                        case SJType.Object:
                            {
                                SJReader.Value innerKey = SJReader.Value.Error();
                                while (reader.IterateObjectEntries(kv, out SJReader.Value inner))
                                {
                                    switch (inner.type)
                                    {
                                        case SJType.Key:
                                            innerKey = inner;
                                            break;

                                        case SJType.Error:
                                            throw new Exception($"SJReader error : {reader.Error}");

                                        case SJType.Number:
                                        case SJType.String:
                                        case SJType.Bool:
                                        case SJType.Null:
                                            Console.WriteLine($"Settings <{new string(key.Slice())}/{new string(innerKey.Slice())} : {new string(inner.Slice())}>");
                                            break;
                                    }
                                }
                            }
                            break;
                        case SJType.Array:
                            while (reader.IterateObjectEntries(kv, out SJReader.Value inner))
                            {
                                Console.WriteLine($"Settings <{new string(key.Slice())}/[{reader.S.Peek().valueCount}] : {new string(inner.Slice())}>");
                            }
                            break;

                        case SJType.Number:
                        case SJType.String:
                        case SJType.Bool:
                        case SJType.Null:
                            Console.WriteLine($"Settings <{new string(key.Slice())} : {new string(kv.Slice())}>");
                            break;
                    }
                }
            }

            throw new NotImplementedException("Implement other key types, trailing comma, types");
        }
    }
}

#pragma warning restore IDE0005
