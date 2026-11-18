using System;

namespace BX.SJ
{
    public interface ISJDataWriter : IDisposable
    {
        int Count { get; }                   // Count written
        int Version { get; }                 // Version increments every reset or every underlying data resource change

        bool CanRead { get; }                // Whether if the written data can be read
        string ReadWritten();                // Read the written data as string
        int ReadWritten(in Span<char> data); // Read the written data into span

        void Write(char c);                  // Write character
        void Write(ReadOnlySpan<char> s);    // Write string

        void Reset();                        // Reset writer state and maybe current pointer
    }
}
