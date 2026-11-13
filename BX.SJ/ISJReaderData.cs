using System;

namespace BX.SJ
{
    /// <summary>
    /// The generic SJReader interface, used for reading data for any .
    /// </summary>
    /// <remarks>
    /// * It is heavily recommended to use reference types instead of value types.
    /// The shared copy should be one to not copy and box the state.
    /// <br>* Only ASCII characters are tokens on BXSJ format, you can downcast the character 
    /// if for example you are reading from a UTF-16 or UTF-32 source.
    /// For any unicode, BXSJ wraps them around '"' and it's only relevant 
    /// to things like <see cref="SJEscape"/> which work with in memory resources.
    /// Only the "Current" is relevant for getting slices, SJReader will save
    /// the current according to the tokens you provide.</br>
    /// <br>* Slice should convert the string (or read it with a "Decoder" if you want).</br>
    /// <br>* <see cref="Next"/> should behave identically to something like "Stream".
    /// Position will show the next character's index rather than the actual .</br>
    /// </remarks>
    public interface ISJReaderData
    {
        byte CharSize { get; }
        int Position { get; }
        bool Ended { get; }
        byte Next();

        ReadOnlySpan<byte> SliceUTF8(int start, int length);
        ReadOnlySpan<char> SliceUTF16(int start, int length);

        void PushState(int n);
        void PopState(int n);
        void Reset();
    }
}
