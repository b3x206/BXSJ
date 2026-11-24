using System;
using System.Collections.Generic;

namespace BX.SJ
{
    /// <summary>
    /// The generic SJReader interface, used for reading data for any sort of SJReader.
    /// </summary>
    /// <remarks>
    /// * It is heavily recommended to use reference types instead of value types to implement this.
    /// <br>* Only ASCII characters are tokens on BXSJ data definition "language", 
    /// you can downcast the character if reading from a unicode resource.
    /// BXSJ wraps unicode around ASCII delimiters.</br>
    /// </remarks>
    public interface ISJDataReader : IEnumerator<byte>
    {
        int Version { get; }
        int CharSize { get; }

        int Position { get; }
        int Index => Position - 1;

        byte Next();                  // Moves to the next character and returns it. Should return 0 if no next character.
        bool TryNext(out byte v);     // Moves next if possible.
        byte Peek();                  // Peeks the next character. Should return 0 if no next character.
        bool TryPeek(out byte v);     // Peeks and returns whether if it can be moved next.
        int SkipWhiteSpace();         // Skips whitespace, returns move count. Current is the value that is expected.
        bool TrySkipWhiteSpace(out int skipped); // Skips whitespace. If there is continuation of file, should return true
        bool TrySkipWhiteSpace()
        {
            return TrySkipWhiteSpace(out int _);
        }

        bool TryNext(out char v)
        {
            bool r = TryNext(out byte b);
            v = (char)b;
            return r;
        }
        bool TryPeek(out char v)
        {
            bool r = TryPeek(out byte b);
            v = (char)b;
            return r;
        }

        ReadOnlySpan<byte> SliceU8(int start, int length);
        ReadOnlySpan<char> SliceU16(int start, int length);
    }
}
