using System;
using System.Runtime.CompilerServices;

namespace BX.SJ
{
    /// <summary>
    /// Uses <see cref="string"/> as JSON read resource.
    /// </summary>
    public sealed class SJStringReader : SJReader
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

        public SJStringReader()
        { }
        public SJStringReader(string data)
        {
            _Data = data;
        }

        // This is used as a limiter.
        public override int Length => _Data?.Length ?? 0;
        // Because I ported the pointer stuff as-is, some parts of the parser may read off by one.
        // It is recommended to do an bound check and return EOF of your choice
        public override char At(int i) => i >= 0 && i < Length ? _Data[i] : '\0'; // TODO : Debug the out of bounds reads (we have a lot of tests, so make the hard coded bound checks irrelevant)
        // Because each value slice is evaluated lazily, the data ranges must persist and should be representable easily as a range (making arbitrary Streams much harder)
        // Note that there isn't much of a reason to do this, if you read the data as soon as it's received from the SJReader.
        public override ReadOnlySpan<char> Slice(int start, int length) =>
            string.IsNullOrEmpty(_Data) ? ReadOnlySpan<char>.Empty : _Data.AsSpan(start, length);
    }
}

