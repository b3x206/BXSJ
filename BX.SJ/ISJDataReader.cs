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
        byte CharSize { get; }

        int Position { get; }
        int Index => Position - 1;

        bool Ended { get; }
        // This is here, because we could technically MoveNext
        // once and check Ended and then Reset, but it is easier and less dirty.
        // Also, for the case of most readers, Reset may reset
        // other states (such as stacked ones).
        bool Empty { get; }

        byte Next();
        int Next(in Span<byte> data);
        int SkipWhiteSpace();

        ReadOnlySpan<byte> SliceU8(int start, int length);
        ReadOnlySpan<char> SliceU16(int start, int length);
    }
}
