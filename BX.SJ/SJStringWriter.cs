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

        bool ISJStateStackable.HasError => false; // No error cases could occur for this
        public int Count => sb.Length;
        public int Version { get; private set; } = 0;
        public bool CanRead => true; // Is in memory
        private SJValueStack<int> _states = SJValueStack<int>.CreateWithMaxSize(SJWriter.DefaultMaxDepth);
        int ISJStateStackable.MaxStateCount
        {
            get => _states.maxSize; set => _states.maxSize = value;
        }

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
            Version++;  // May desync data from the Writer
        }

        public void Write(char c) => sb.Append(c);
        public void Write(ReadOnlySpan<char> s) => sb.Append(s);
        public void Dispose()
        { }

        int ISJStateStackable.PushState()
        {
            var handle = _states.Push(sb.Length);
            if (handle.IsStub) return -1;
            return _states.Count - 1;
        }
        int ISJStateStackable.PopState()
        {
            if (_states.Count <= 0) return -1;
            sb.Length = _states.PopValue();
            return _states.Count;
        }
    }
}
