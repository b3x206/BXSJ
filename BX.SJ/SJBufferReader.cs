using BX.SJ.Collections;
using System;
using System.Collections;

namespace BX.SJ
{
    /// <summary>
    /// Implements an arbitrary buffer reader with arbitrary struct unit.
    /// <br>The buffer is assumed to be fully in memory. A translation 
    /// (if chunking) can be written, but is non-trivial and may reduce 
    /// performance if the chunks aren't POT.</br>
    /// </summary>
    /// <remarks>
    /// The defined stride size (<see cref="CharSize"/>) does not effect how many units <see cref="Position"/> is moved.
    /// <br>The defined length should be in how many <see cref="Position"/>s exist and is independent of <see cref="CharSize"/></br>
    /// </remarks>
    public abstract class SJBufferReader : ISJDataReader, ISJStateStackable
    {
        public int Version { get; protected set; } = 0;
        public int Position { get; protected set; } = 0;
        public byte Current { get; protected set; }
        object IEnumerator.Current => Current;
        public abstract int Length { get; }
        public abstract int CharSize { get; }
        /// <summary>
        /// Get the ASCII ranged (0-127) character data at <paramref name="position"/>.
        /// <br>In most normal conditions, access to this indexer will be within range.</br>
        /// </summary>
        protected abstract byte this[int position] { get; }
        protected bool disposed;

        private SJValueStack<int> _positions = SJValueStack<int>.CreateWithMaxSize(SJConstants.MaxDepth);
        protected virtual bool HasError => false;
        bool ISJStateStackable.HasError => HasError;
        int ISJStateStackable.MaxStateCount
        {
            get => _positions.Capacity; set => _positions.Count = value;
        }

        // Love being off by one.
        protected virtual void CheckDisposed() { } // Default impl can be disposed and have no problems
        public virtual bool MoveNext()
        {
            CheckDisposed();

            if (Position < Length)
            {
                Current = this[Position];
                Position++;
                return true;
            }
            return MoveNextRare();
        }
        private bool MoveNextRare()
        {
            Position = Length + 1;
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
            CheckDisposed();

            if (Position < Length)
            {
                return this[Position];
            }
            return 0;
        }
        public bool TryPeek(out byte b)
        {
            CheckDisposed();

            if (Position < Length)
            {
                b = this[Position];
                return true;
            }
            b = 0;
            return false;
        }
        public int SkipWhiteSpace()
        {
            CheckDisposed();

            // This method should start the iterator if it wasn't started.
            int i;
            for (i = 0; (Position <= 0 || char.IsWhiteSpace((char)Current)) && MoveNext(); i++) ;
            return i;
        }
        public bool TrySkipWhiteSpace(out int count)
        {
            CheckDisposed();

            bool result = true;
            for (count = 0; (Position <= 0 || char.IsWhiteSpace((char)Current)) && (result = MoveNext()); count++) ;
            return result;
        }

        public abstract ReadOnlySpan<byte> SliceU8(int start, int length);
        public abstract ReadOnlySpan<char> Slice(int start, int length);

        protected virtual int PushState()
        {
            var h = _positions.Push(Position);
            if (h.IsStub) return -1;

            return _positions.Count - 1;
        }
        protected virtual int PopState()
        {
            var h = _positions.Pop();
            if (h.IsStub) return -1;

            Position = h.Value;
            Current = Position >= Length ? (byte)0 : Math.Min(/* ÿ */(byte)0xFF, this[Position]);
            return _positions.Count;
        }
        int ISJStateStackable.PushState() => PushState();
        int ISJStateStackable.PopState() => PopState();

        public virtual void Reset()
        {
            Position = Current = 0;
            _positions.Clear(false);
            Version++;
        }

        protected virtual void Dispose(bool disposing)
        {
            disposed = true;
        }
        // Implement finalizer if needed in the implementing class.
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

