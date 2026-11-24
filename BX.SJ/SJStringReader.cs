using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    public sealed class SJStringReader : SJBufferReader<char>
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
        public override ReadOnlySpan<char> Buffer => _Data;
        public bool Empty => string.IsNullOrEmpty(Data);

        public SJStringReader()
        { }
        public SJStringReader(string data)
        {
            _Data = data;
        }

        protected override byte ToByte(char value) => unchecked((byte)Math.Min((char)255U, value));
        public override ReadOnlySpan<byte> SliceU8(int start, int length)
        {
            if (Empty) return ReadOnlySpan<byte>.Empty;
            return Encoding.UTF8.GetBytes(Data.Substring(start, length));
        }
        public override ReadOnlySpan<char> SliceU16(int start, int length)
        {
            if (Empty) return ReadOnlySpan<char>.Empty;
            return Data.AsSpan(start, length);
        }
    }
}

