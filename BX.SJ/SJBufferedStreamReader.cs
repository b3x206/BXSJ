using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Provides a "reused" region of memory to read string from a stream.
    /// <br>Has a <b>hard size limit</b> (not truly streamed) as SJ's lazy evaluation uses an in memory buffer.</br>
    /// </summary>
    /// <remarks>
    /// <b>Warnings:</b> <br/>
    /// * Lazy evaluation slice <b>can change</b> if another resource was read into it 
    /// and one of the <see cref="SJReader.Value"/> you had was referencing the previous slice,
    /// which actually references the most recent slice because it references the
    /// SJReader.Data object itself, making the range invalid. <br/>
    /// </remarks>
    public sealed class SJBufferedStreamReader : SJBufferReader
    {
        // For my usage of generally small payloads, this is overkill.
        // It only exists to "simplify file reading/writing with a live stream
        // handle that gets commited when done" and allocate less GC.
        public const int DefaultReadingBufferCapacity = 16384; // 16 KB
        public const int DefaultCapacity = 16384;              // 16 KB
        public const int DefaultMaxCapacity = 16777216;        // 16 MB

        public override int CharSize => 2;
        public readonly bool rented;
        private byte[] readBuffer;
        // It would be non-trivial to store the data as other encodings that can dynamically change.
        // It's best to just convert it into UTF-16.
        private char[] data;

        private int dataLength;
        private readonly int maxCapacity = DefaultMaxCapacity;
        public override int Length => dataLength;
        protected override byte this[int position] => unchecked((byte)Math.Max(255U, data[position]));
        protected override bool HasError => disposed;

        public int MaxRegionSize { get; private set; }

        public static SJBufferedStreamReader Rented(int capacity = DefaultCapacity, int maxCapacity = DefaultMaxCapacity, int readBufferCapacity = DefaultReadingBufferCapacity) =>
            new SJBufferedStreamReader(true, capacity, maxCapacity, readBufferCapacity);
        public static SJBufferedStreamReader Allocated(int capacity = DefaultCapacity, int maxCapacity = DefaultMaxCapacity, int readBufferCapacity = DefaultReadingBufferCapacity) =>
            new SJBufferedStreamReader(false, capacity, maxCapacity, readBufferCapacity);
        private SJBufferedStreamReader(bool rented, int capacity, int maxCapacity, int readBufferCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Stream reader capacity must not be negative or zero", nameof(capacity));
            }
            if (maxCapacity <= 0)
            {
                throw new ArgumentException("Max capacity must not be negative or zero", nameof(maxCapacity));
            }
            if (readBufferCapacity <= 0)
            {
                throw new ArgumentException("Stream reading buffer capacity must not be negative or zero", nameof(maxCapacity));
            }

            this.rented = rented;
            this.maxCapacity = maxCapacity;
            if (rented)
            {
                data = ArrayPool<char>.Shared.Rent(capacity)!;
                readBuffer = ArrayPool<byte>.Shared.Rent(readBufferCapacity)!;
            }
            else
            {
                data = new char[capacity];
                readBuffer = new byte[readBufferCapacity];
            }
        }

        protected override void CheckDisposed()
        {
            if (disposed) throw new InvalidOperationException("Attempt to use a disposed SJStreamReader.");
        }
        private bool EnsureRemainingFor(int write, int count)
        {
            CheckDisposed();

            if (count < 0)
            {
                throw new ArgumentException("Cannot ensure negative size", nameof(count));
            }
            if (maxCapacity <= 0)
            {
                throw new InvalidOperationException("Max capacity invalid");
            }

            int targetSize = write + count;
            if (targetSize > maxCapacity)
            {
                throw new ArgumentException($"Exceeded maximum buffer capacity '{maxCapacity}' allowed", nameof(count));
            }

            if (targetSize > data.Length)
            {
                count = Math.Max(Math.Max(write * 2, write + count), 1);
                if (rented)
                {
                    var prevData = data;
                    data = ArrayPool<char>.Shared.Rent(count);
                    if (prevData != null)
                    {
                        try
                        {
                            MemoryExtensions.CopyTo(prevData, (Span<char>)data);
                        }
                        finally
                        {
                            ArrayPool<char>.Shared.Return(prevData, true);
                        }
                    }
                }
                else
                {
                    Array.Resize(ref data, count);
                }
                return true;
            }

            return false;
        }

        // TODO : Implement the ability to store generic data with specified encoding
        //        or no alloc slicing for UTF-8 + UTF-16. Note that this isn't used much
        //        so a "functional" rather than "performant" implementation will do.
        public override ReadOnlySpan<byte> SliceU8(int start, int length)
        {
            if (Length <= 0) return ReadOnlySpan<byte>.Empty;
            var u8 = Encoding.UTF8;
            var slice = data.AsSpan(start, length);
            int resultLength = u8.GetByteCount(slice);
            var result = new byte[resultLength];
            u8.GetBytes(slice, result);
            return result;
        }
        public override ReadOnlySpan<char> Slice(int start, int length)
        {
            if (Length <= 0) return ReadOnlySpan<char>.Empty; 
            return data.AsSpan(start, length);
        }

        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardLoaded"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadFrom(TextReader tr)
        {
            CheckDisposed();

            Span<char> currentDataView = data;
            int count = tr.Read(currentDataView);
            int current = 0;
            while (count > 0)
            {
                current += count;
                if (count >= currentDataView.Length)
                {
                    // Double the capacity and move the "currentDataView"
                    EnsureRemainingFor(current, count);
                    currentDataView = data.AsSpan(current);
                }

                count = tr.Read(currentDataView);
            }

            dataLength = current;
        }
        /// <inheritdoc cref="LoadFrom(Stream, Encoding)"/>
        public void LoadFrom(Stream s)
        {
            LoadFrom(s, SJEncoding.DetectEncoding(s, out _));
        }
        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardLoaded"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadFrom(Stream s, Encoding encoding)
        {
            CheckDisposed();
            encoding ??= SJEncoding.DetectEncoding(s, out _);

            var decoder = encoding.GetDecoder();
            int count = s.Read(readBuffer);
            ReadOnlySpan<byte> streamDataView = readBuffer.AsSpan(0, count);

            int current = 0, decodeCount;
            while (count > 0)
            {
                decodeCount = decoder.GetCharCount(streamDataView, false);
                EnsureRemainingFor(current, decodeCount);
                current += decoder.GetChars(streamDataView, data.AsSpan(current), false);

                count = s.Read(readBuffer);
                streamDataView = readBuffer.AsSpan(0, count);
            }

            // Flush the last remaining bytes, if it's malformed UTF-8 sequence for example
            // Flushing basically resets the state and goes back to default. Should be done in the end.
            var e = ReadOnlySpan<byte>.Empty;
            decodeCount = decoder.GetCharCount(e, false);
            if (decodeCount > 0)
            {
                EnsureRemainingFor(current, decodeCount);
                decoder.GetChars(e, data.AsSpan(current), true);
            }

            dataLength = current;
        }
        /// <summary>
        /// Discard the currently read stream data.
        /// This won't resize the streams if it's excessively sized.
        /// </summary>
        public void DiscardLoaded()
        {
            CheckDisposed();

            dataLength = 0;
        }

        protected override void Dispose(bool disposing)
        {
            // ArrayPool is not unmanaged, but MemoryPool is.
            // No need to handle  the destructor it seems.
            if (!disposed)
            {
                try
                {
                    if (disposing)
                    {
                        if (rented && data != null)
                        {
                            ArrayPool<char>.Shared.Return(data, true);
                        }
                    }
                }
                finally
                {
                    data = null;
                }
                try
                {
                    if (disposing)
                    {
                        if (rented && readBuffer != null)
                        {
                            ArrayPool<byte>.Shared.Return(readBuffer, true);
                        }
                    }
                }
                finally
                {
                    readBuffer = null;
                }

                disposed = true;
            }
        }
    }
}
