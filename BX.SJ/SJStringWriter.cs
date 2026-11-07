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

        protected override void DoAppend(char c) => sb.Append(c);
        protected override void DoAppend(ReadOnlySpan<char> s) => sb.Append(s);

        public override bool CanReadData => true;
        public override string ReadData() => sb.ToString();

        private int[] _sbStates = new int[DefaultStackSize];

        protected override void ResizeStateStack(int size)
        {
            base.ResizeStateStack(size);
            System.Array.Resize(ref _sbStates, size);
        }
        public override int PushState()
        {
            int baseResult = base.PushState();
            if (baseResult >= 0) _sbStates[baseResult] = sb.Length;
            return baseResult;
        }
        public override int PopState()
        {
            int baseResult = base.PopState();
            if (baseResult >= 0) sb.Length = _sbStates[baseResult];
            return baseResult;
        }

        public override void Reset()
        {
            base.Reset();
            sb.Clear();
        }
    }
}
