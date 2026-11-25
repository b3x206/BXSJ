using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    public sealed class SJStringReader : SJBufferReader
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
                Reset();
            }
        }
        public override int CharSize => 2;
        public override int Length => _Data?.Length ?? 0;
        protected override byte this[int pos] => unchecked((byte)Math.Min((char)255U, Data[Position]));
        public bool Empty => string.IsNullOrEmpty(Data);

        public SJStringReader()
        { }
        public SJStringReader(string data)
        {
            _Data = data;
        }

        public override ReadOnlySpan<byte> SliceU8(int start, int length)
        {
            if (Empty) return ReadOnlySpan<byte>.Empty;
            return Encoding.UTF8.GetBytes(Data.Substring(start, length));
        }
        public override ReadOnlySpan<char> Slice(int start, int length)
        {
            if (Empty) return ReadOnlySpan<char>.Empty;
            return Data.AsSpan(start, length);
        }
    }
}
