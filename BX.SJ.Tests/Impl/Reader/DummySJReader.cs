using System.Collections;

namespace BX.SJ.Tests.Impl;

/// <summary>
/// Used to execute erroreneous codepaths caused by a bogus reader.
/// </summary>
internal sealed class DummySJReader : ISJDataReader
{
    public int Version => default;
    public int CharSize => default;
    public int Position => default;
    public byte Current => default;
    object IEnumerator.Current => Current;

    public void Dispose()
    {
    }

    public bool MoveNext()
    {
        return default;
    }

    public byte Next()
    {
        return default;
    }

    public byte Peek()
    {
        return default;
    }

    public void Reset()
    {
    }

    public int SkipWhiteSpace()
    {
        return default;
    }

    public ReadOnlySpan<char> Slice(int start, int length)
    {
        return default;
    }

    public ReadOnlySpan<byte> SliceU8(int start, int length)
    {
        return default;
    }

    public bool TryNext(out byte v)
    {
        v = default;
        return default;
    }

    public bool TryPeek(out byte v)
    {
        v = default;
        return default;
    }

    public bool TrySkipWhiteSpace(out int skipped)
    {
        skipped = default;
        return default;
    }
}
