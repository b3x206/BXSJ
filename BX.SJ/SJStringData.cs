using BX.SJ.Collections;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    public sealed class SJStringData : ISJDataReader
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
        public bool Ended => Position >= Data.Length;
        public bool Empty => Data?.Length <= 0; // Giga brain
        public byte Current => Ended || Position <= 0 ? (byte)0 : (byte)Math.Min((char)0xFF, Data[Position - 1]);

        public SJStringData()
        { }
        public SJStringData(string data)
        {
            _Data = data;
        }

        public byte Next()
        {
            if (Ended) return 0;

            char d = Data[Position++];
            return (byte)Math.Min((char)0xFF /* ÿ */, d);
        }
        public int Next(in Span<byte> data)
        {
            int i;
            for (i = 0; !Ended && i < data.Length; i++)
            {
                data[i] = (byte)Math.Min((char)0xFF /* ÿ */, Data[Position++]);
            }
            return i; // 'i' includes the Length
        }
        public int SkipWhiteSpace()
        {
            int i;
            for (i = 0; !Ended && char.IsWhiteSpace(Data[Position]); i++, Position++) ;
            return i;
        }

        public ReadOnlySpan<byte> SliceU8(int start, int length)
        {
            if (Empty) throw new InvalidOperationException("Cannot slice empty data.");
            return Encoding.UTF8.GetBytes(Data.Substring(start, length));
        }
        public ReadOnlySpan<char> SliceU16(int start, int length)
        {
            if (Empty) throw new InvalidOperationException("Cannot slice empty data.");
            return Data.AsSpan(start, length);
        }

        private ValueStack<int> _positions = ValueStack<int>.Create();
        public void PushState()
        {
            _positions.PushValue(Position);
        }
        public void PopState()
        {
            var h = _positions.Pop();
            if (h.IsStub) return;
            Position = h.Value;
        }

        public void Reset()
        {
            Position = 0;
            _positions.Clear(false);
        }
    }
}

