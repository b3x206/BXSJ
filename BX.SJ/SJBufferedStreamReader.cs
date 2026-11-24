using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace BX.SJ
{
    public enum SJBufferStorageMode
    {
        // Refer to
        // * https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-encoding-introduction#endianness
        // * https://learn.microsoft.com/en-us/dotnet/api/system.text.utf32encoding
        // I think UTF-32 also generally has endianness, but whatever.

        /// <summary>
        /// Store / treat buffer as &lt;current endianness&gt; UTF-16.
        /// <br>If SJ is running on a big-endian system, </br>
        /// </summary>
        Utf16,
        /// <summary>
        /// Store / treat buffer as byte sequenced UTF-8.
        /// </summary>
        Utf8,
        /// <summary>
        /// Store / treat buffer as byte sequenced UTF-32.
        /// </summary>
        Utf32,
    }

    /// <summary>
    /// Provides a "reused" region of memory to read string from a stream.
    /// <br>Has a <b>hard size limit</b> (not truly streamed) as SJ's lazy evaluation requires an in memory buffer.</br>
    /// </summary>
    /// <remarks>
    /// <b>Warnings:</b> <br/>
    /// * Lazy evaluation slice <b>can change</b> if another resource was read into it 
    /// and one of the <see cref="SJReader.Value"/> you had was referencing the previous slice,
    /// which actually references the most recent slice because it references the SJReader object itself, making the range invalid. <br/>
    /// * Freed ArrayPool buffers maybe could be "referenced", but that is much less likely as slices are only ref struct and 
    /// freed pool elements are always "nulled" (either dispose or resize operations change the ArrayPool reference,
    /// but the reader reference within the <see cref="SJReader.Value"/> does not, making this scenario much less likely)
    /// </remarks>
    public sealed class SJBufferedStreamReader : SJBufferReader<char>
    {
        // For my usage of generally small payloads, this is overkill.
        // It only exists to "simplify file reading/writing with a live stream
        // handle that gets commited when done" and allocate less GC.
        public const int DefaultReadingBufferCapacity = 16384; // 16 KB
        public const int DefaultCapacity = 16384;             // 16 KB
        public const int DefaultMaxCapacity = 16777216;       // 16 MB

        public readonly bool rented;
        private byte[] readBuffer;
        // dataU8 can be reinterpret_cast<u16*>(), although that Span view
        // must be done with caution and must truncate the leading byte
        private byte[] dataU8;

        private int dataLength;
        private readonly int maxCapacity = DefaultMaxCapacity;

        public int Length => dataLength;

        public SJBufferedStreamReader()
        {
            dataU8 = ArrayPool<char>.Shared.Rent(DefaultCapacity)!;
            // This one can't be resized.
            // If processing in 16 KB chunks is slow, I may add resizing. (technically, custom size can be defined)
            readBuffer = ArrayPool<byte>.Shared.Rent(DefaultReadingBufferCapacity)!;
        }
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
                dataU8 = ArrayPool<char>.Shared.Rent(capacity)!;
                readBuffer = ArrayPool<byte>.Shared.Rent(readBufferCapacity)!;
            }
            else
            {
                dataU8 = new char[capacity];
                readBuffer = new byte[readBufferCapacity];
            }
        }

        protected override void CheckDisposed()
        {
            if (disposed) throw new InvalidOperationException("Attempt to use a disposed SJStreamReader.");
        }
        private static bool EnsureRemainingFor<T>(int current, int count, int maxCapacity, ref T[] buf)
        {
            if (buf == null)
            {
                throw new ArgumentNullException(nameof(buf), "Cannot resize null buffered stream reader");
            }
            if (count < 0)
            {
                throw new ArgumentException("Cannot ensure negative size", nameof(count));
            }
            if (maxCapacity <= 0)
            {
                throw new ArgumentException("Max capacity invalid", nameof(maxCapacity));
            }

            int targetSize = current + count;
            if (targetSize > maxCapacity)
            {
                throw new ArgumentException($"Exceeded maximum buffer capacity '{maxCapacity}' allowed");
            }

            if (targetSize > buf.Length)
            {
                T[] prevData = buf;

                count = Math.Max(Math.Max(current * 2, current + count), 1);
                buf = ArrayPool<T>.Shared.Rent(count);
                if (prevData != null)
                {
                    try
                    {
                        MemoryExtensions.CopyTo(prevData, (Span<T>)buf);
                    }
                    finally
                    {
                        ArrayPool<T>.Shared.Return(prevData, true);
                    }
                }
                return true;
            }

            return false;
        }

        public override ReadOnlySpan<char> SliceU8(int start, int length) => dataU8.AsSpan(start, length);
        public override ReadOnlySpan<char> SliceU16(int start, int length) => dataU8.AsSpan(start, length);
        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardData"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadDataFrom(TextReader tr)
        {
            CheckDisposed();

            Span<char> currentDataView = dataU8;
            int count = tr.Read(currentDataView);
            int current = 0;
            while (count > 0)
            {
                current += count;
                if (count >= currentDataView.Length)
                {
                    // Double the capacity and move the "currentDataView"
                    EnsureRemainingFor(current, count, maxCapacity, ref dataU8);
                    currentDataView = dataU8.AsSpan(current);
                }

                count = tr.Read(currentDataView);
            }

            dataLength = current;
        }
        public void LoadDataFrom(Stream s)
        {
            CheckDisposed();

            LoadDataFrom(s, SJEncoding.DetectEncoding(s, out _));
        }
        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardData"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadDataFrom(Stream s, Encoding encoding)
        {
            CheckDisposed();

            var decoder = encoding.GetDecoder();
            int count = s.Read(readBuffer);
            ReadOnlySpan<byte> streamDataView = readBuffer.AsSpan(0, count);

            int current = 0, decodeCount;
            while (count > 0)
            {
                decodeCount = decoder.GetCharCount(streamDataView, false);
                EnsureRemainingFor(current, decodeCount, maxCapacity, ref dataU8);
                current += decoder.GetChars(streamDataView, dataU8.AsSpan(current), false);

                count = s.Read(readBuffer);
                streamDataView = readBuffer.AsSpan(0, count);
            }

            // Flush the last remaining bytes, if it's malformed UTF-8 sequence for example
            // Flushing basically resets the state and goes back to default. Should be done in the end.
            var e = ReadOnlySpan<byte>.Empty;
            decodeCount = decoder.GetCharCount(e, false);
            if (decodeCount > 0)
            {
                EnsureRemainingFor(current, decodeCount, maxCapacity, ref dataU8);
                decoder.GetChars(e, dataU8.AsSpan(current), true);
            }

            dataLength = current;
        }
        /// <summary>
        /// Discard the currently read stream data.
        /// This won't resize the streams if excessively sized.
        /// </summary>
        public void DiscardData()
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
                if (dataU8 != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            ArrayPool<char>.Shared.Return(dataU8, true);
                        }
                    }
                    finally
                    {
                        dataU8 = null;
                    }
                }
                if (readBuffer != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            ArrayPool<byte>.Shared.Return(readBuffer, true);
                        }
                    }
                    finally
                    {
                        readBuffer = null;
                    }
                }

                disposed = true;
            }
        }
    }
}
