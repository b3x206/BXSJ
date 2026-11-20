namespace BX.SJ.Tests.Impl;

internal sealed class DummySJWriter : ISJDataWriter
{
    public int Count => default;

    public int Version => default;

    public bool CanRead => default;

    public void Dispose()
    {
    }

    public string ReadWritten()
    {
        return "";
    }

    public int ReadWritten(in Span<char> data)
    {
        return default;
    }

    public void Reset()
    {
    }

    public void Write(char c)
    {
    }

    public void Write(ReadOnlySpan<char> s)
    {
    }
}
