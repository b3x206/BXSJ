using System;

namespace BX.SJ
{
    public interface ISJDataWriter : IDisposable
    {
        int Count { get; }
        int Version { get; }

        bool CanRead { get; }

        string Read();
        void Write(char c);
        void Write(ReadOnlySpan<char> s);
    }
}
