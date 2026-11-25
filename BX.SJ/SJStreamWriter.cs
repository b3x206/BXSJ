using BX.SJ.Collections;
using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Writes BXSJ to a <see cref="Stream"/> of any kind.
    /// </summary>
    public sealed class SJStreamWriter : ISJDataWriter, ISJStateStackable, IDisposable
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
        private int _Version = 0;
        int ISJDataWriter.Version => _Version;
        bool ISJStateStackable.HasError => disposed;
        public int Count { get; private set; }

        private SJValueStack<long> _positions = SJValueStack<long>.Create();
        public int MaxStateCount
        {
            get => _positions.maxSize;
            set => _positions.maxSize = value;
        }

        private bool disposed;
        private void CheckDisposed()
        {
            if (disposed) throw new InvalidOperationException("Attempt to use a disposed SJStreamWriter");
        }

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
            CheckDisposed();

            if (flushBuffer)
            {
                // [!] Does not count towards written count
                if (!wrotePreamble)
                {
                    // UTF8 BOM is cringe. It's UTF8 or not here
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
                    byte[] rentedDataBuffer = null;
                    Span<byte> dataBuffer = length <= 128 ? stackalloc byte[length] : Span<byte>.Empty;
                    try
                    {
                        if (dataBuffer.Length != length)
                        {
                            dataBuffer = rentedDataBuffer = ArrayPool<byte>.Shared.Rent(length);
                        }

                        encoder.GetBytes(buffer, dataBuffer, flushEncoder);
                        stream.Write(dataBuffer.Slice(0, length));
                        streamCurrent = 0;
                    }
                    finally
                    {
                        if (rentedDataBuffer != null) ArrayPool<byte>.Shared.Return(rentedDataBuffer, true);
                    }
                }
            }
            if (flushStream)
            {
                stream.Flush();
            }
        }
        public void Write(char c)
        {
            CheckDisposed();

            if ((streamCurrent + 1) > buffer.Length)
            {
                Flush(false, true, false);
            }

            Count++;
            buffer[streamCurrent++] = c;
        }
        public void Write(ReadOnlySpan<char> s)
        {
            CheckDisposed();

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

                Count += length;
            }

            // Last write that is buffered in this writer (and is not flushed).
            if (remaining.Length > 0)
            {
                remaining.CopyTo(buffer.AsSpan(streamCurrent));
                streamCurrent += remaining.Length;
                Count += remaining.Length;
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
            CheckDisposed();

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
            unchecked { _Version++; }
        }
        public void Reset() => Reset(true);

        public bool CanRead => stream.CanSeek;
        /// <summary>
        /// Read data from the stream writer.
        /// </summary>
        /// <remarks>
        /// This calls <see cref="Flush"/> (to not truncate the existing data) to read 
        /// the data and creates a <see cref="StreamReader"/>, use it sparingly.
        /// </remarks>
        public string ReadWritten()
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
                using var sr = new StreamReader(
                    stream, encoding, detectEncodingFromByteOrderMarks: true, BufferSize, leaveOpen: true
                );
                return sr.ReadToEnd();
            }
            finally
            {
                stream.Position = sp;
            }
        }
        public int ReadWritten(in Span<char> chars)
        {
            long sp = stream.Position;
            try
            {
                Flush(true, true, false);
                sp = stream.Position;

                stream.Position = 0;
                using var sr = new StreamReader(
                    stream, encoding, detectEncodingFromByteOrderMarks: true, BufferSize, leaveOpen: true
                );
                return sr.Read(chars);
            }
            finally
            {
                stream.Position = sp;
            }
        }

        int ISJStateStackable.PushState()
        {
            // We must flush buffer before saving state, this is to get the position accurately
            // I forgot that there's an encoding. Don't be silly like me and set the UTF-16 position.
            int r = _positions.Count + 1;
            if (_positions.maxSize <= 0 || r <= _positions.maxSize)
            {
                // Technically, flushing the encoder is not needed as most rule-compliant
                // strings are padded with ASCII characters which should prevent U+FEFF.
                Flush(false, true, true);
                _positions.PushValue(stream.Position);
            }
            return r;
        }
        int ISJStateStackable.PopState()
        {
            int r = _positions.Count - 1;
            if (r >= 0)
            {
                encoder.Reset();
                streamCurrent = 0; // Discard the stream writing buffer

                // This will flush or discard the stream buffer anyways
                var pos = _positions.PopValue();
                stream.Position = pos;
                stream.SetLength(pos);
            }
            return r;
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
