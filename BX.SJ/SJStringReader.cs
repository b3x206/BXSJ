using BX.SJ.Collections;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    public sealed class SJStringReader : ISJDataReader, ISJStateStackable
    {
        private string _Data;
        public string Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Data;
            set
            {
                if (string.Equals(value, _Data, StringComparison.Ordinal))
                {
                    return;
                }

                _Data = value;
                Version++;
                Reset();
            }
        }
        bool ISJStateStackable.HasError => false;
        public int Version { get; private set; } = 0;
        public byte CharSize => 2;
        public int Position { get; private set; } = 0;
        public bool Ended => Position > Data.Length;
        public bool Empty => string.IsNullOrEmpty(Data);
        public byte Current { get; private set; }
        object IEnumerator.Current => Current;

        public SJStringReader()
        { }
        public SJStringReader(string data)
        {
            _Data = data;
        }

        // Love being off by one.
        public bool MoveNext()
        {
            if (Position < Data?.Length)
            {
                Current = (byte)Math.Min(/* ÿ */(char)0xFF, Data[Position]);
                Position++;
                return true;
            }
            return MoveNextRare();
        }
        private bool MoveNextRare()
        {
            Position = (Data?.Length ?? 0) + 1;
            Current = default;
            return false;
        }
        public byte Next()
        {
            if (MoveNext())
            {
                return Current;
            }

            return default;
        }
        public bool TryNext(out byte b)
        {
            if (MoveNext())
            {
                b = Current;
                return true;
            }
            b = 0;
            return false;
        }
        public byte Peek()
        {
            if (Position < Data?.Length)
            {
                return (byte)Math.Min((char)0xFF, Data[Position]);
            }
            return 0;
        }
        public bool TryPeek(out byte b)
        {
            if (Position < Data?.Length)
            {
                b = (byte)Math.Min((char)0xFF, Data[Position]);
                return true;
            }
            b = 0;
            return false;
        }
        public int SkipWhiteSpace()
        {
            // This method should start the iterator if it wasn't started.
            int i;
            for (i = 0; (Position <= 0 || char.IsWhiteSpace((char)Current)) && MoveNext(); i++) ;
            return i;
        }
        public bool TrySkipWhiteSpace(out int count)
        {
            bool result = true;
            for (count = 0; (Position <= 0 || char.IsWhiteSpace((char)Current)) && (result = MoveNext()); count++) ;
            return result;
        }

        public ReadOnlySpan<byte> SliceU8(int start, int length)
        {
            if (Empty) return ReadOnlySpan<byte>.Empty;
            return Encoding.UTF8.GetBytes(Data.Substring(start, length));
        }
        public ReadOnlySpan<char> SliceU16(int start, int length)
        {
            if (Empty) return ReadOnlySpan<char>.Empty;
            return Data.AsSpan(start, length);
        }

        private SJValueStack<int> _positions = SJValueStack<int>.CreateWithMaxSize(SJReader.DefaultMaxDepth);
        public int MaxStateCount { get => _positions.Capacity; set => _positions.Count = value; }
        public int PushState()
        {
            var h = _positions.Push(Position);
            if (h.IsStub) return -1;

            return _positions.Count - 1;
        }
        public int PopState()
        {
            var h = _positions.Pop();
            if (h.IsStub) return -1;

            Position = h.Value;
            Current = Position >= Data.Length ? (byte)0 : (byte)Math.Min((char)0xFF, Data[Position]);
            return _positions.Count;
        }

        public void Reset()
        {
            Position = Current = 0;
            _positions.Clear(false);
            Version++;
        }

        public void Dispose()
        { }
    }
}

