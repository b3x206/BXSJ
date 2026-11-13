using System;

namespace BX.SJ
{
    /// <summary>
    /// The generic SJReader interface, used for reading data for any sort of SJReader.
    /// </summary>
    /// <remarks>
    /// * It is heavily recommended to use reference types instead of value types.
    /// The shared copy shouldn't be copied and boxed, as the states for reading 
    /// from a stream or memory source is generally in the heap.
    /// Until very recent C# versions, it wasn't possible to implement any interfaces 
    /// in `ref struct`s and also the fact that SJ data can be moved outside of the stack.
    /// <br>* Only ASCII characters are tokens on BXSJ data definition "language", you can downcast the character 
    /// if for example you are reading from a UTF-16 or UTF-32 source.
    /// For any unicode, BXSJ wraps them around '"' or ASCII delimiters and it's only relevant 
    /// to things like <see cref="SJEscape"/> which work with in memory resources.
    /// Only the "Position" is relevant for getting slices, SJReader will save
    /// the position according to the data you provide.</br>
    /// <br>* <see cref="Next"/> is expected to behave identically like "Stream".
    /// Position will show the next character's index rather than the current index.</br>
    /// <br>* <see cref="SliceU8(int, int)"/> and <see cref="SliceU16(int, int)"/> are lazily evaluated.
    /// Even though calling <see cref="SliceU8(int, int)"/> is rarer, it must be implemented properly.
    /// (Can be encoded to buffer stored in your reader of your liking, or into a temporary buffer, allocating GC.)
    /// </br>
    /// <br>* Any <c>Slice</c> method takes literal length of units, not the amount of runes or codepoints expressed..</br>
    /// <br>* Any <c>Slice</c> method is 0 indexed.</br>
    /// <br>* If seeking is not supported for slicing data, it is better to just load data to the memory 
    /// as the parsed slice can be as big as the file (string literal only file, for example).</br>
    /// <br>* Maximum supported size is <see cref="int.MaxValue"/> - there is no practical application for 2GB+ BXSJ data.</br>
    /// </remarks>
    public interface ISJDataReader
    {
        int Version { get; }
        byte CharSize { get; }
        int Position { get; }
        int Index => Position - 1;
        bool Ended { get; }
        /// <summary>
        /// <see langword="true"/> if there is nothing to get <see cref="Next"/> in the data.
        /// </summary>
        bool Empty { get; }
        /// <summary>
        /// Move to the next character.
        /// </summary>
        byte Next();
        /// <summary>
        /// Move by <paramref name="data"/>'s length while reading all of the next candidate characters as ASCII.
        /// </summary>
        /// <param name="data">Data to read into.</param>
        /// <returns>Length moved by next.</returns>
        int Next(in Span<byte> data);
        /// <summary>
        /// Peek the next character without moving the position.
        /// </summary>
        byte Current { get; }
        /// <summary>
        /// Skips whitespace data or runes.
        /// <br>SJ ignores whitespace for some circumstances between tokens.</br>
        /// <br>Whitespace should be skipped in a way where <see cref="Next"/> returns the non-whitespace character.</br>
        /// </summary>
        int SkipWhiteSpace();

        ReadOnlySpan<byte> SliceU8(int start, int length);
        ReadOnlySpan<char> SliceU16(int start, int length);

        void PushState();
        void PopState();
        void Reset();
    }
}
