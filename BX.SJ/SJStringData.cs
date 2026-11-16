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
    public sealed class SJStringData : ISJDataReader, ISJStateStackable
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
        public int Version { get; private set; } = 0;
        public byte CharSize => 2;
        public int Position { get; private set; } = 0;
        public bool Ended => Position > Data.Length;
        public bool Empty => string.IsNullOrEmpty(Data);
        public byte Current { get; private set; }
        object IEnumerator.Current => Current;

        public SJStringData()
        { }
        public SJStringData(string data)
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
        public byte Peek()
        {
            if (Position < Data?.Length)
            {
                return (byte)Math.Min((char)0xFF, Data[Position]);
            }
            return 0;
        }
        public int Next(in Span<byte> data)
        {
            if (data == null || data.Length <= 0) return 0;
            int i;
            // MoveNext first, this is to skip the last character as it will be left as the "Current"
            for (i = 0; MoveNext() && i < data.Length; i++)
            {
                data[i] = Current;
            }

            // 'i' includes the Length
            return i;
        }
        public int SkipWhiteSpace()
        {
            int i;
            for (i = 0; (Position <= 0 || char.IsWhiteSpace((char)Current)) && MoveNext(); i++) ;
            return i;
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

        private ValueStack<int> _positions = ValueStack<int>.CreateWithMaxSize(SJReader.DefaultMaxDepth);
        public int MaxStateCount { get => _positions.Capacity; set => _positions.Count = value; }
        public int PushState()
        {
            _positions.PushValue(Position);
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
        }

        public void Dispose()
        { }
    }
}

