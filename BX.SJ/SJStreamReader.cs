using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace BX.SJ
{
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
    public sealed class SJStreamReader : SJReader, IDisposable
    {
        // Also the conversion overhead to char..
        // Future SJ update should add ReadOnlySpan or some other arbitrary means of reading from an in-disk resource rather than this
        // But for my usage of small payloads, this is overkill. It only exists to "simplify file management" and allocate less GC.
        private byte[] streamData;
        private char[] data;
        private int dataLength;
        private readonly int maxCapacity = DefaultMaxCapacity;

        private bool disposed;

        public const int DefaultStreamBufferCapacity = 16384; // 16 KB
        public const int DefaultCapacity = 16384;             // 16 KB
        public const int DefaultMaxCapacity = 16777216;       // 16 MB

        public override int Length => dataLength;

        public SJStreamReader()
        {
            data = ArrayPool<char>.Shared.Rent(DefaultCapacity)!;
            // This one can't be resized.
            // If processing in 16 KB chunks is slow, I may add resizing. (technically, custom size can be defined)
            streamData = ArrayPool<byte>.Shared.Rent(DefaultStreamBufferCapacity)!;
        }
        public SJStreamReader(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Stream reader capacity must not be negative or zero", nameof(capacity));
            }

            data = ArrayPool<char>.Shared.Rent(capacity)!;
            streamData = ArrayPool<byte>.Shared.Rent(DefaultStreamBufferCapacity)!;
        }
        public SJStreamReader(int capacity, int maxCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Stream reader capacity must not be negative or zero", nameof(capacity));
            }
            if (maxCapacity <= 0)
            {
                throw new ArgumentException("Max capacity must not be negative or zero", nameof(maxCapacity));
            }

            data = ArrayPool<char>.Shared.Rent(capacity)!;
            this.maxCapacity = maxCapacity;
            streamData = ArrayPool<byte>.Shared.Rent(DefaultStreamBufferCapacity)!;
        }
        public SJStreamReader(int capacity, int maxCapacity, int streamDataCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Stream reader capacity must not be negative or zero", nameof(capacity));
            }
            if (maxCapacity <= 0)
            {
                throw new ArgumentException("Max capacity must not be negative or zero", nameof(maxCapacity));
            }
            if (streamDataCapacity <= 0)
            {
                throw new ArgumentException("Stream reading buffer capacity must not be negative or zero", nameof(maxCapacity));
            }

            data = ArrayPool<char>.Shared.Rent(capacity)!;
            this.maxCapacity = maxCapacity;
            streamData = ArrayPool<byte>.Shared.Rent(streamDataCapacity)!;
        }

        private static bool EnsureRemainingFor<T>(int current, int count, int maxCapacity, ref T[] buf)
        {
            if (buf == null)
            {
                throw new ArgumentNullException(nameof(buf), "Cannot resize null buffered stream reader");
            }
            if (count < 0)
            {
                throw new ArgumentException("Cannot ensure negative size");
            }
            if (maxCapacity <= 0)
            {
                throw new ArgumentException("Max capacity invalid");
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

        public override char At(int i) => i >= 0 && i < dataLength ? data[i] : '\0';
        public override ReadOnlySpan<char> Slice(int start, int length) => data.AsSpan(start, length);
        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardData"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadDataFrom(TextReader tr)
        {
            if (disposed || data == null || streamData == null)
            {
                Error = "Attempt to read a stream into disposed StreamReader";
                ThrowError();
                return;
            }

            Span<char> currentDataView = data;
            int count = tr.Read(currentDataView);
            int current = 0;
            while (count > 0)
            {
                current += count;
                if (count >= currentDataView.Length)
                {
                    // Double the capacity and move the "currentDataView"
                    EnsureRemainingFor(current, count, maxCapacity, ref data);
                    currentDataView = data.AsSpan(current);
                }

                count = tr.Read(currentDataView);
            }

            dataLength = current;
        }
        public void LoadDataFrom(Stream s)
        {
            LoadDataFrom(s, SJEncoding.DetectEncoding(s, out _));
        }
        /// <summary>
        /// Reads and stores all text from given stream <paramref name="s"/>.
        /// <br>Data can be discarded with <see cref="DiscardData"/> or <see cref="Dispose"/></br>
        /// </summary>
        public void LoadDataFrom(Stream s, Encoding encoding)
        {
            if (disposed || data == null || streamData == null)
            {
                Error = "Attempt to read a stream into disposed StreamReader";
                ThrowError();
                return;
            }

            var decoder = encoding.GetDecoder();
            int count = s.Read(streamData);
            ReadOnlySpan<byte> streamDataView = streamData.AsSpan(0, count);

            int current = 0, decodeCount;
            while (count > 0)
            {
                decodeCount = decoder.GetCharCount(streamDataView, false);
                EnsureRemainingFor(current, decodeCount, maxCapacity, ref data);
                current += decoder.GetChars(streamDataView, data.AsSpan(current), false);

                count = s.Read(streamData);
                streamDataView = streamData.AsSpan(0, count);
            }

            // Flush the last remaining bytes, if it's malformed UTF-8 sequence for example
            // Flushing basically resets the state and goes back to default. Should be done in the end.
            var e = ReadOnlySpan<byte>.Empty;
            decodeCount = decoder.GetCharCount(e, false);
            if (decodeCount > 0)
            {
                EnsureRemainingFor(current, decodeCount, maxCapacity, ref data);
                decoder.GetChars(e, data.AsSpan(current), true);
            }

            dataLength = current;
        }
        /// <summary>
        /// Discard the currently read stream data.
        /// This won't resize the streams if excessively sized.
        /// </summary>
        public void DiscardData()
        {
            if (disposed)
            {
                Error = "Attempt to discard a disposed stream reader";
                ThrowError();
                return;
            }

            dataLength = 0;
        }

        private void Dispose(bool disposing)
        {
            // ArrayPool is not unmanaged, but MemoryPool is.
            // No need to handle override the destructor it seems.
            if (!disposed)
            {
                if (data != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            ArrayPool<char>.Shared.Return(data, true);
                        }
                    }
                    finally
                    {
                        data = null;
                    }
                }
                if (streamData != null)
                {
                    try
                    {
                        if (disposing)
                        {
                            ArrayPool<byte>.Shared.Return(streamData, true);
                        }
                    }
                    finally
                    {
                        streamData = null;
                    }
                }

                disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
