using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Writes JSON to a <see cref="Stream"/> of any kind.
    /// </summary>
    public sealed class SJStreamWriter : SJWriter, IDisposable
    {
        // Stream has it's own buffers or something
        /// <summary>
        /// Stream supplied to this writer.
        /// </summary>
        public Stream BaseStream => stream;
        private Stream stream;
        private readonly bool closeStream = true;
        private readonly bool isUTF8 = true;

        // These also has it's own "encoding" buffer
        private readonly Encoding encoding;
        private readonly Encoder encoder;
        private long startPosition;
        private bool wrotePreamble = false;

        // Buffer sizing is "constant" throughout the writer, it can't be changed after creation
        // (though with reflection anything is possible!!1!, because I need to "set this null" because it's from ArrayPool)
        // If capacity isn't enough, just flush existing to stream and encoding and reset the buffer.
        // Yo I heard you liked buffers so I put buffers inside your buffer.
        private char[] buffer;
        private int streamCurrent;

        private bool disposed;

        public const int BufferSize = 1024, MinBufferSize = 1;

        public SJStreamWriter(Stream stream, bool closeStream = true)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable", nameof(stream));
            this.stream = stream; startPosition = stream.Position;
            this.closeStream = closeStream;

            encoding = Encoding.UTF8;
            encoder = encoding.GetEncoder()!;
            buffer = ArrayPool<char>.Shared.Rent(BufferSize)!;
        }
        public SJStreamWriter(Stream stream, Encoding encoding, bool closeStream = true)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable", nameof(stream));
            this.stream = stream;
            startPosition = stream.Position;
            this.closeStream = closeStream;

            isUTF8 = encoding == Encoding.UTF8;
            this.encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            encoder = encoding.GetEncoder()!;
            buffer = ArrayPool<char>.Shared.Rent(BufferSize)!;
        }
        public SJStreamWriter(Stream stream, int bufferSize, bool closeStream = true)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable", nameof(stream));
            if (bufferSize < MinBufferSize) throw new ArgumentException($"Stream reader buffer size must be larger than {MinBufferSize}", nameof(bufferSize));
            this.stream = stream; startPosition = stream.Position;
            this.closeStream = closeStream;

            encoding = Encoding.UTF8;
            encoder = encoding.GetEncoder()!;
            buffer = ArrayPool<char>.Shared.Rent(bufferSize)!;
        }
        public SJStreamWriter(Stream stream, Encoding encoding, int bufferSize, bool closeStream = true)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable", nameof(stream));
            if (bufferSize < MinBufferSize) throw new ArgumentException($"Stream reader buffer size must be larger than {MinBufferSize}", nameof(bufferSize));
            this.stream = stream;
            this.closeStream = closeStream;

            isUTF8 = encoding == Encoding.UTF8;
            this.encoding = encoding;
            encoder = encoding.GetEncoder()!;
            buffer = ArrayPool<char>.Shared.Rent(bufferSize)!;
        }

        public void Flush() => Flush(true, true, false); // Doesn't close the stream, so encoder will "stay"
        private void Flush(bool flushStream, bool flushBuffer, bool flushEncoder)
        {
            if (disposed || stream == null)
            {
                Error = "Cannot flush after getting disposed";
                ThrowError();
                return;
            }

            if (flushBuffer)
            {
                if (!wrotePreamble)
                {
                    // UTF8 BOM is actually cringe. It's UTF8 or not here
                    if (!isUTF8)
                    {
                        var preamble = encoding.Preamble;
                        if (preamble.Length > 0)
                        {
                            stream.Write(preamble);
                            startPosition = stream.Position;
                        }
                    }

                    wrotePreamble = true;
                }

                ReadOnlySpan<char> buffer = this.buffer.AsSpan(0, streamCurrent);
                int length = encoder.GetByteCount(buffer, false);
                if (length > 0)
                {
                    byte[] rentedTempBuffer = null;
                    Span<byte> tempBuffer = length <= 128 ? stackalloc byte[length] : Span<byte>.Empty;
                    try
                    {
                        if (tempBuffer.Length != length)
                        {
                            tempBuffer = rentedTempBuffer = ArrayPool<byte>.Shared.Rent(length);
                        }

                        encoder.GetBytes(buffer, tempBuffer, flushEncoder);
                        stream.Write(tempBuffer.Slice(0, length));
                        streamCurrent = 0;
                    }
                    finally
                    {
                        if (rentedTempBuffer != null) ArrayPool<byte>.Shared.Return(rentedTempBuffer, true);
                    }
                }
            }
            if (flushStream)
            {
                stream.Flush();
            }
        }
        protected override void DoAppend(char c)
        {
            if (disposed || stream == null)
            {
                Error = "Cannot append after getting disposed";
                ThrowError();
                return;
            }

            if ((streamCurrent + 1) > buffer.Length)
            {
                Flush(false, true, false);
            }

            buffer[streamCurrent++] = c;
        }
        protected override void DoAppend(ReadOnlySpan<char> s)
        {
            if (disposed || stream == null)
            {
                Error = "Cannot append after getting disposed";
                ThrowError();
                return;
            }

            // Writes that are written to the base stream (which has it's own buffer)
            // It's a buffer matroshka
            ReadOnlySpan<char> remaining = s;
            while ((streamCurrent + remaining.Length) > buffer.Length)
            {
                int length = buffer.Length - streamCurrent;
                remaining.Slice(0, length).CopyTo(buffer.AsSpan(streamCurrent));
                remaining = remaining.Slice(length);
                streamCurrent = buffer.Length;
                Flush(false, true, false);
            }

            // Last write that is buffered in this writer (and is not flushed).
            if (remaining.Length > 0)
            {
                remaining.CopyTo(buffer.AsSpan(streamCurrent));
                streamCurrent += remaining.Length;
            }
        }
        /// <param name="resetStream">
        /// Whether to reset the stream to it's initial state.
        /// <br>If this is false, the contents are committed.
        /// This could be useful if the write is successful,
        /// but the stream position has changed to inject bxsj somewhere else.</br>
        /// </param>
        public void Reset(bool resetStream)
        {
            if (disposed || stream == null)
            {
                Error = "Cannot reset after getting disposed";
                ThrowError();
                return;
            }
            base.Reset();

            if (resetStream)
            {
                stream.Position = startPosition;
                stream.SetLength(startPosition); // Truncate the stream on Reset
            }
            else
            {
                Flush(true, true, true);
            }
            streamCurrent = 0;
            encoder.Reset();
        }
        public override void Reset() => Reset(true);

        public override bool CanReadData => stream.CanSeek;
        /// <summary>
        /// Read data from the stream writer.
        /// </summary>
        /// <remarks>
        /// This calls <see cref="Flush"/> (to not truncate the existing data) to read 
        /// the data and creates a <see cref="StreamReader"/>, use it sparingly.
        /// </remarks>
        public override string ReadData()
        {
            long sp = stream.Position;
            try
            {
                // Generally, SJStreamWriter could receive "untruncated" strings surrounded alongside
                // ascii/latin-1 range chars, so it's okay to not flush the encoder here while in the process of writing.
                // (but comment lines complicate things.. anyways let the user blow up if they write
                //  mojibake as the flushing current state should complete the char)
                Flush(true, true, false);
                sp = stream.Position;     // Save flushed position (Stream position is generally modified)

                stream.Position = 0;
                using var sr = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, BufferSize, leaveOpen: true);
                return sr.ReadToEnd();
            }
            finally
            {
                stream.Position = sp;
            }
        }
        private int[] _streamPositions = new int[DefaultStackSize];
        protected override void ResizeStateStack(int size)
        {
            base.ResizeStateStack(size);
            System.Array.Resize(ref _streamPositions, size);
        }
        public override int PushState()
        {
            int baseResult = base.PushState();
            // State.count is more accurate as a position of writing than the stream pos,
            // which does not update unless all buffers are flushed.
            // TODO : Should test what happens when 'S.count' is overflowed within SJWriter.
            //        I added "checked" as a safety measure, so that it throws if it overflows.
            //        I don't think anyone will want to write 8PB * sizeof(char) of BXSJ :P
            if (baseResult >= 0) _streamPositions[baseResult] = S.count;
            return baseResult;
        }
        public override int PopState()
        {
            int baseResult = base.PopState();
            if (baseResult >= 0)
            {
                // TODO : Fix, truncates invalidly and creates mojibake
                // Flush(true, false, false);
                encoder.Reset();
                streamCurrent = 0; // Discard the stream writing buffer

                // This will flush or discard the stream buffer anyways
                int pos = _streamPositions[baseResult];
                stream.Position = pos;
                stream.SetLength(pos);
            }
            return baseResult;
        }

        private void Dispose(bool disposing = true)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Can flush everything
                    Flush(true, true, true);
                }

                if (stream != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            stream.Dispose();
                        }
                    }
                    finally
                    {
                        stream = null;
                    }
                }

                if (buffer != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            ArrayPool<char>.Shared.Return(buffer, true);
                        }
                    }
                    finally
                    {
                        buffer = null;
                    }
                }

                disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
