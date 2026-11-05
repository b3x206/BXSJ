using System;
using System.IO;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Encoding related utilities.
    /// </summary>
    public static class SJEncoding
    {
        public const int MaxEncodingPreambleSize = 4;
        public static readonly Encoding DefaultEncoding = Encoding.UTF8;
        public static Encoding DetectEncoding(ReadOnlySpan<byte> firstChunk, out int bomLength) => DetectEncoding(firstChunk, DefaultEncoding, out bomLength);
        public static Encoding DetectEncoding(ReadOnlySpan<byte> firstChunk, Encoding fallbackEncoding, out int bomLength)
        {
            var pa = Encoding.UTF32.Preamble;
            if (firstChunk.StartsWith(pa)) { bomLength = pa.Length; return Encoding.UTF32; }

            pa = Encoding.BigEndianUnicode.Preamble;
            if (firstChunk.StartsWith(pa)) { bomLength = pa.Length; return Encoding.BigEndianUnicode; }

            pa = Encoding.Unicode.Preamble;
            if (firstChunk.StartsWith(pa)) { bomLength = pa.Length; return Encoding.Unicode; }

            // UTF8 bom is still cringe
            pa = Encoding.UTF8.Preamble;
            if (firstChunk.StartsWith(pa)) { bomLength = pa.Length; return Encoding.UTF8; }

            bomLength = 0;
            return fallbackEncoding ?? DefaultEncoding;
        }
        public static Encoding DetectEncoding(Stream stream, out int bomLength) => DetectEncoding(stream, DefaultEncoding, out bomLength);
        public static Encoding DetectEncoding(Stream stream, Encoding fallbackEncoding, out int bomLength)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (!stream.CanSeek)
            {
                // Maybe to not "seek" the stream, we could create a list of preambles and go until finding a fully matching preamble
                // Though this is hard to do and non trivial, alongside with the need to seek back if nothing matches. So this will do.
                throw new ArgumentException("Stream must support seeking", nameof(stream));
            }

            Span<byte> bomBytes = stackalloc byte[MaxEncodingPreambleSize];
            long prevPos = stream.Position;
            stream.Read(bomBytes);

            bomLength = 0;
            try
            {
                return DetectEncoding(bomBytes, Encoding.UTF8, out bomLength);
            }
            finally
            {
                stream.Position = prevPos + bomLength;
            }
        }
    }
}
