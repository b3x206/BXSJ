#nullable disable
#pragma warning disable IDE0005
#pragma warning disable IDE0090
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BX.SJ.Tests.Examples
{
    /// <summary>
    /// Used for the inspector in the <see cref="ReaderEx"/> example.
    /// <br>Prefer to use <see cref="SJTree"/> instead if retaining "metadata" isn't important,
    /// but this is another example that shows how you can save all values.</br>
    /// <br><b>Note :</b> This directly uses the stored <see cref="SJReader.Value"/>,
    /// which is perishable if the data source on the parent <see cref="SJReader"/> changes. Use with caution.</br>
    /// </summary>
    internal static class ReaderInspect
    {
        // A generic reader loop that will write the output into a Dictionary, alongside it's convenience type.
        // If you plan to use this seriously, I recommend doing something like SJTree instead of this.
        // (how ironic, this is probably better than the current BXSave in terms of performance, maybe.)
        public sealed class ValueOrList : IEnumerable<SJReader.Value>
        {
            private SJReader.Value _Value;
            public SJReader.Value Value => _Value;
            private readonly Lazy<List<SJReader.Value>> _Values = new Lazy<List<SJReader.Value>>();
            public SJReader.Value NonMetaValue => this.FirstOrDefault(v => v.type
                switch
            { SJType.Comment => false, SJType.Type => false, _ => true }
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
        public static Dictionary<string, ValueOrList> ReadIntoDictionary(SJReader reader)
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
        public static bool IsChildOfParent(string parent, string path, int maxLevel = 1)
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
    }
}
#pragma warning restore IDE0005
#pragma warning restore IDE0090
#nullable restore
