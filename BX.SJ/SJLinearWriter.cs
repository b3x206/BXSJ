using BX.SJ.Collections;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace BX.SJ
{
    /// <summary>
    /// Writes BXSJ to a <see cref="char[]"/> rented from an <see cref="ArrayPool{T}"/>.
    /// </summary>
    public sealed class SJLinearWriter : ISJDataWriter, ISJStateStackable, IDisposable, IEnumerable<char>, IConvertible, IEquatable<string>
    {
        public readonly bool rented;
        private readonly int maxCapacity = -1;
        private char[] data;
        private int current;
        private bool disposed;
        public bool CanReadData => true;
        public int Count => current;
        public int Version { get; private set; } = 0;
        public bool CanRead => true;
        bool ISJStateStackable.HasError => disposed; // Errors immediately throw
        int ISJStateStackable.MaxStateCount
        {
            get => _states.maxSize; set => _states.maxSize = value;
        }

        public const int DefaultCapacity = 4096;

        public static SJLinearWriter Rented(int capacity = DefaultCapacity, int maxCapacity = -1) =>
            new SJLinearWriter(true, capacity, maxCapacity);
        public static SJLinearWriter Allocated(int capacity = DefaultCapacity, int maxCapacity = -1) =>
            new SJLinearWriter(false, capacity, maxCapacity);

        private SJLinearWriter(bool rented, int capacity, int maxCapacity)
        {
            this.rented = rented;
            this.maxCapacity = maxCapacity;
            data = rented ? ArrayPool<char>.Shared.Rent(capacity)! : (new char[capacity]);
        }

        private void EnsureRemainingFor(int size)
        {
            if (size < 0)
            {
                throw new ArgumentException("Cannot ensure negative size");
            }
            if (data == null)
            {
                throw new InvalidOperationException("Cannot resize null buffered writer. This usually means that the writer was disposed.");
            }

            // StringBuilder throws, and the append stuff does not handle errors. You either append or not.
            int targetSize = current + size;
            if (maxCapacity > 0 && targetSize > maxCapacity)
            {
                throw new ArgumentException($"Exceeded maximum capacity '{maxCapacity}' allowed", nameof(size));
            }

            if (targetSize > data.Length)
            {
                // Grow more aggresively
                size = Math.Max(Math.Max(current * 4, current + size), 1);
                if (rented)
                {
                    char[] prevData = data;
                    data = ArrayPool<char>.Shared.Rent(size);
                    try
                    {
                        MemoryExtensions.CopyTo(prevData, (Span<char>)data);
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(prevData, true);
                    }
                }
                else
                {
                    Array.Resize(ref data, size);
                }
            }
        }

        public void Write(char c)
        {
            CheckDisposed();

            EnsureRemainingFor(1);
            data[current++] = c;
        }
        public void Write(ReadOnlySpan<char> s)
        {
            CheckDisposed();

            EnsureRemainingFor(s.Length);

            s.CopyTo(data.AsSpan(current));
            current += s.Length;
        }

        public string ReadWritten()
        {
            CheckDisposed();

            // The "ReadOnlySpan<char>" pointer will work fine as well.
            // All C# strings copy, this includes the Create and this constructor.
            return new string(data.AsSpan(0, current));
        }
        public int ReadWritten(in Span<char> data)
        {
            CheckDisposed();

            int copied = Math.Min(data.Length, current);
            data.Slice(0, copied).TryCopyTo(data);
            return copied;
        }

        private SJValueStack<int> _states = SJValueStack<int>.Create();
        int ISJStateStackable.PushState()
        {
            CheckDisposed();
            _states.PushValue(current);
            return _states.Count - 1;
        }
        int ISJStateStackable.PopState()
        {
            CheckDisposed();
            current = _states.PopValue();
            return _states.Count;
        }

        public void Reset()
        {
            CheckDisposed();

            _states.Clear(false);
            current = 0;

            Version++;
        }

        private void CheckDisposed()
        {
            if (disposed) throw new InvalidOperationException("Attempt to use a disposed linear writer.");
        }
        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                try
                {
                    if (rented && data != null)
                    {
                        if (disposing) ArrayPool<char>.Shared.Return(data, true);
                    }
                }
                finally
                {
                    data = null;
                }

                disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public IEnumerator<char> GetEnumerator()
        {
            for (int i = 0; i < current; i++)
            {
                CheckDisposed();
                yield return data[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        TypeCode IConvertible.GetTypeCode() => TypeCode.Object;
        string IConvertible.ToString(IFormatProvider provider) => ReadWritten();
        object IConvertible.ToType(Type conversionType, IFormatProvider provider)
        {
            CheckDisposed();
            switch (Convert.GetTypeCode(conversionType))
            {
                case TypeCode.String:
                    return ReadWritten();
                case TypeCode.Object:
                    if (conversionType.IsAssignableFrom(typeof(SJLinearWriter))) return this;
                    else goto default;
                default:
                    throw new NotSupportedException($"Cannot convert to type <{conversionType}>");
            }
        }

        public bool Equals(string other, StringComparison comparison)
        {
            CheckDisposed();
            if (other is null) return false;
            return MemoryExtensions.Equals(data.AsSpan(0, current), other, comparison);
        }
        public bool Equals(string other) => Equals(other, StringComparison.Ordinal);

        bool IConvertible.ToBoolean(IFormatProvider provider) =>
            throw new NotSupportedException();
        byte IConvertible.ToByte(IFormatProvider provider) =>
            throw new NotSupportedException();
        char IConvertible.ToChar(IFormatProvider provider) =>
            throw new NotSupportedException();
        DateTime IConvertible.ToDateTime(IFormatProvider provider) =>
            throw new NotSupportedException();
        decimal IConvertible.ToDecimal(IFormatProvider provider) =>
            throw new NotSupportedException();
        double IConvertible.ToDouble(IFormatProvider provider) =>
            throw new NotSupportedException();
        short IConvertible.ToInt16(IFormatProvider provider) =>
            throw new NotSupportedException();
        int IConvertible.ToInt32(IFormatProvider provider) =>
            throw new NotSupportedException();
        long IConvertible.ToInt64(IFormatProvider provider) =>
            throw new NotSupportedException();
        sbyte IConvertible.ToSByte(IFormatProvider provider) =>
            throw new NotSupportedException();
        float IConvertible.ToSingle(IFormatProvider provider) =>
            throw new NotSupportedException();
        ushort IConvertible.ToUInt16(IFormatProvider provider) =>
            throw new NotSupportedException();
        uint IConvertible.ToUInt32(IFormatProvider provider) =>
            throw new NotSupportedException();
        ulong IConvertible.ToUInt64(IFormatProvider provider) =>
            throw new NotSupportedException();
    }
}
