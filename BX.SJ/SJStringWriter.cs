using BX.SJ.Collections;
using System;
using System.Text;

namespace BX.SJ
{
    /// <summary>
    /// Writes <see cref="SJWriter"/> data to a <see cref="StringBuilder"/>
    /// </summary>
    public sealed class SJStringWriter : ISJDataWriter, ISJStateStackable
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

        protected override void DoAppend(char c) => sb.Append(c);
        protected override void DoAppend(ReadOnlySpan<char> s) => sb.Append(s);

        public override bool CanReadData => true;

        public int Count => sb.Length;
        public int Version => 0; // Writing source not mutable
        public bool CanRead => true;

        string ISJStateStackable.Error => null;

        public int MaxStateCount { get => _states.maxSize; set => _states.maxSize = value; }

        public string ReadWritten() => sb.ToString();
        public int ReadWritten(in Span<char> data)
        {
            int c = Math.Min(data.Length, sb.Length);
            sb.CopyTo(0, data, c);
            return c;
        }

        public void Reset()
        {
            sb.Clear();
        }

        public void Write(char c)
        {
            throw new NotImplementedException();
        }

        public void Write(ReadOnlySpan<char> s)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        private SJValueStack<int> _states = SJValueStack<int>.CreateWithMaxSize();
        public int PushState()
        {
            int baseResult = base.PushState();
            if (baseResult >= 0) _states.PushValue(sb.Length);
            return baseResult;
        }
        public int PopState()
        {
            int baseResult = base.PopState();
            if (baseResult >= 0) sb.Length = _states.PopValue();
            return baseResult;
        }
    }
}
