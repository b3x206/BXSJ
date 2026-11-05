using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Writes JSON to a <see cref="char[]"/> borrowed from an <see cref="ArrayPool{T}"/>, creating it using <see cref="string.Create"/>
    /// </summary>
    public sealed class SJLinearWriter : SJWriter, IDisposable
    {
        private readonly int maxCapacity = -1;
        private char[] data;
        private int current;
        private bool disposed;

        public const int DefaultCapacity = 4096;

        public SJLinearWriter()
        {
            data = ArrayPool<char>.Shared.Rent(DefaultCapacity)!;
        }
        public SJLinearWriter(int capacity)
        {
            data = ArrayPool<char>.Shared.Rent(capacity)!;
        }
        public SJLinearWriter(int capacity, int maxCapacity)
        {
            data = ArrayPool<char>.Shared.Rent(capacity)!;
            this.maxCapacity = maxCapacity;
        }

        private void EnsureRemainingFor(int size)
        {
            if (size < 0)
            {
                Error = "Cannot ensure negative size";
                ThrowError();
                return;
            }

            if (data == null)
            {
                Error = "Cannot resize null buffered writer";
                ThrowError();
                return;
            }

            // StringBuilder throws, and the append stuff does not handle errors. You either append or not.
            int targetSize = current + size;
            if (maxCapacity > 0 && targetSize > maxCapacity)
            {
                Error = $"Exceeded maximum capacity '{maxCapacity}' allowed";
                ThrowError();
                return;
            }

            if (targetSize > data.Length)
            {
                char[] prevData = data;

                // Grow more aggresively
                size = Math.Max(Math.Max(current * 4, current + size), 1);
                data = ArrayPool<char>.Shared.Rent(size);
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
        }

        public override void Append(char c)
        {
            EnsureRemainingFor(1);
            data[current++] = c;
        }
        public override void Append(ReadOnlySpan<char> s)
        {
            EnsureRemainingFor(s.Length);

            s.CopyTo(data.AsSpan(current));
            current += s.Length;
        }

        private sealed class StringCreateState
        {
            public SJLinearWriter self;
            public int current;

            public StringCreateState() { }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetData(SJLinearWriter self)
            {
                this.self = self ?? throw new ArgumentNullException(nameof(self));
                current = 0;
            }
        }
        public override bool CanReadData => true;
        [ThreadStatic]
        private static StringCreateState sharedCreateState;
        public override string ReadData()
        {
            sharedCreateState ??= new StringCreateState();
            sharedCreateState.SetData(this);

            return string.Create(current, sharedCreateState, (data, state) =>
            {
                for (int i = 0; i < data.Length; i++)
                {
                    // Access the reference through this
                    data[i] = state.self.data[state.current++];
                }
            });
        }

        public override void Reset()
        {
            base.Reset();
            current = 0;
        }

        private void Dispose(bool disposing)
        {
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
