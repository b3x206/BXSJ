using System;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Writes JSON to a <see cref="StringBuilder"/>
    /// </summary>
    public sealed class SJStringWriter : SJWriter
    {
        private readonly StringBuilder sb;
        public const int DefaultCapacity = 256;

        public SJStringWriter(StringBuilder sb)
        {
            this.sb = sb ?? throw new ArgumentNullException(nameof(sb));
        }
        public SJStringWriter()
        {
            sb = new StringBuilder(DefaultCapacity);
        }
        public SJStringWriter(int capacity)
        {
            sb = new StringBuilder(capacity);
        }
        public SJStringWriter(int capacity, int maxCapacity)
        {
            sb = new StringBuilder(capacity, maxCapacity);
        }

        public override void Append(char c) => sb.Append(c);
        public override void Append(ReadOnlySpan<char> s) => sb.Append(s);

        public override bool CanReadData => true;
        public override string ReadData() => sb.ToString();

        public override void Reset()
        {
            base.Reset();
            sb.Clear();
        }
    }
}
