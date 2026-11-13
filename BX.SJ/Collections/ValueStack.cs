using System;
using System.Collections;
using System.Collections.Generic;

namespace BX.SJ.Collections
{
    /// <summary>
    /// Heap allocated value based stack that returns local refs from it's array.
    /// <br>* Is not necessarily `ref struct` based.</br>
    /// <br>* Used with SJ specific data to reuse heap resources that may potentially exist on <typeparamref name="T"/>.
    /// Rely on the BXUtility version instead.</br>
    /// <br>* This collection only grows until a given set limit <see cref="maxSize"/>.
    /// If the size limit does not exist, it grows infinitely.</br>
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// using System;
    /// using BX.SJ.Collections;
    /// 
    /// // `default`/`new ValueStack<int>()` init will also work, but lazy inits the array.
    /// var vs = ValueStack<int>.Create();
    /// // Value pushed is default(T), if it wasn't previously modified and not cleared.
    /// // Default Push clears it.
    /// var myInt = vs.Push();
    /// myInt.Value = 6;
    /// vs.Push(42);
    /// vs.Push(7);
    /// var otherInt = vs.Push(8);
    /// otherInt.Value = 512;
    /// vs.Pop(); // Does not clear and returns 512.
    ///           // The "otherInt" handle cannot be reused, it will return a stub and silently ignore.
    ///           // You can retain a reference to the value address by doing `ref otherInt.Value`.
    ///           // It is faster but only safe if you know the value won't be popped.
    ///           // > C# probably keeps the array a bit longer if refs to members exist, but don't trust assumptions.
    /// // Retains 512, even though it should be 0. This is useful if you want to initialize the value with existing heap resources.
    /// vs.Push(clear: false);
    /// 
    /// Console.WriteLine(string.Join(vs, ", ")); // 6, 42, 7, 42, 512
    /// ]]>
    /// </example>
    /// <typeparam name="T">Struct type to store.</typeparam>
    public struct ValueStack<T> : IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, ICollection
        where T : struct
    {
        [ThreadStatic]
        private static T _stub;
        public const int DefaultStackSize = 8;
        public const int DefaultMaxSize = 0;

        public readonly ref struct Handle
        {
            private readonly (T value, int version)[] _ownerStack;
            public readonly int index;
            private readonly int version;
            /// <summary>
            /// Returns <see langword="true"/> if the handle to the stack is valid.
            /// <br>Returns <see langword="false"/> for <see cref="IsStub"/>, but non-valid and non-stub values do exist as well.</br>
            /// </summary>
            public readonly bool IsValid => !IsStub && index < _ownerStack.Length && _ownerStack[index].version == version;
            /// <summary>
            /// Returns <see langword="true"/> for strictly stub values, not for invalid values.
            /// </summary>
            public readonly bool IsStub => index < 0 || _ownerStack == null;

            // A roslyn analyzer emitting a warning if the result of this is stored would be great here.
            // Too bad it's out of scope for this project regardless.
            public ref T Value
            {
                get
                {
                    if (index < 0 || index >= _ownerStack?.Length)
                    {
                        goto _Fail;
                    }

                    ref (T value, int version) t = ref _ownerStack[index];
                    if (version != t.version)
                    {
                        goto _Fail;
                    }

                    // ref-ref is supported on tuples, so I assume there's some compiler magic here.
                    return ref t.value;
                _Fail:
                    _stub = default;
                    return ref _stub;
                }
            }

            public Handle((T, int)[] ownerStack, int index, int version)
            {
                _ownerStack = ownerStack;
                this.index = index;
                this.version = version;
            }
            public static Handle Stub() => new Handle(null, -1, -1);
            public static Handle At(ValueStack<T> v, int index)
            {
                if (index < 0 || index >= v.Count) return Stub();
                var array = v._stack;
                return new Handle(array, index, array[index].version);
            }
            internal static Handle AtUnchecked((T, int)[] array, int index, int version)
            {
                return new Handle(array, index, version);
            }

            /// <summary>
            /// Renews handle if the previously pointed stack is known to be valid, but has wrong heap reference.
            /// <br>This can happen if the stack resizes.</br>
            /// </summary>
            public readonly Handle Renew(ValueStack<T> v, bool withCurrentVersion = true)
            {
                // All ts because ref fields are not supported.
                // I assume that a ref field likely pins the struct and the whole heap region that contains this struct,
                // or adds to a tracker that allows moving when GC..
                return withCurrentVersion ? AtUnchecked(v._stack, index, version) : At(v, index);
            }
        }

        private (T value, int version)[] _stack;
        private int _count, _maxReachedCount;
        public int maxSize;
        /// <summary>
        /// Capacity of the stack. Can be shrunk only when not <see cref="HasAny"/> and can be grown.
        /// </summary>
        public int Capacity
        {
            readonly get => _stack?.Length ?? 0;
            set
            {
                if (Capacity > value && HasAny)
                {
                    throw new InvalidOperationException("Cannot shrink stack that likely has borrowed values. Clear the list first and ensure no code uses a `ref` field to the stack.");
                }

                ResizeInternal(value);
            }
        }
        /// <summary>
        /// Count pushed into the stack. Clamped between <see cref="Capacity"/>, to resize change capacity (stack can only grow).
        /// <br>Can be directly set, however <b>does not release any existing <see cref="Handle"/>s</b> and keeps them valid.</br>
        /// <br>Use <see cref="SetCount"/> with <c>invalidate</c> as <see langword="true"/> to invalidate existing handles. (iterates the array in the direction)</br>
        /// </summary>
        public int Count
        {
            readonly get => _count;
            set => _count = Math.Clamp(value, 0, Capacity);
        }
        public readonly bool HasAny => Count > 0;
        /// <summary>
        /// Sets count pushed into the stack.
        /// </summary>
        /// <param name="count">Count value to set. Clamped between 0 and <see cref="Capacity"/>.</param>
        /// <param name="invalidate">Invalidate handles existing outside (if growing) or inside (if shrinking).</param>
        /// <param name="reset">Clear the value contained outside (if growing) or inside (if shrinking) as <see langword="default"/>.</param>
        public void SetCount(int count, bool invalidate, bool reset)
        {
            count = Math.Clamp(count, 0, Capacity);

            if (_count != count)
            {
                if (invalidate || reset)
                {
                    while (_count < count)
                    {
                        if (invalidate) { unchecked { _stack[_count].version++; } }
                        if (reset) { _stack[_count].value = default; }
                        _count++;
                    }
                    while (_count > count)
                    {
                        _count--;
                        if (invalidate) { unchecked { _stack[_count].version++; } }
                        if (reset) { _stack[_count].value = default; }
                    }
                }
                else
                {
                    _count = count;
                }
            }
        }

        readonly bool ICollection.IsSynchronized => _stack.IsSynchronized;
        readonly object ICollection.SyncRoot => _stack.SyncRoot;

        public readonly ref T this[int index]
        {
            get
            {
                if (_stack == null || index < 0 || _count < index)
                {
                    _stub = default;
                    return ref _stub;
                }
                return ref _stack[index].value;
            }
        }

        public static ValueStack<T> Create() => new ValueStack<T>(DefaultStackSize, DefaultMaxSize);
        public static ValueStack<T> CreateWithCapacity(int capacity) => new ValueStack<T>(capacity, DefaultMaxSize);
        public static ValueStack<T> CreateWithMaxSize(int maxSize) => new ValueStack<T>(DefaultStackSize, maxSize);
        public static ValueStack<T> Create(int capacity, int maxSize) => new ValueStack<T>(capacity, maxSize);
        public ValueStack(int capacity = DefaultStackSize, int maxSize = DefaultMaxSize)
        {
            _stack = new (T, int)[maxSize > 0 && maxSize < capacity ? maxSize : capacity];
            _count = _maxReachedCount = 0;
            this.maxSize = maxSize;
        }

        private int ResizeInternal(int size)
        {
            size = Math.Max(0, size);
            if (maxSize > 0) size = Math.Min(size, maxSize);

            (T value, int version)[] prev = _stack;
            Array.Resize(ref _stack, size);

            // Invalidate previous stack instance after resizing, because the one in the handle isn't a reference to this struct.
            for (int i = 0; i < Math.Min(prev?.Length ?? 0, _maxReachedCount); i++) prev[i].version++;
            return size;
        }
        private (int index, int version) PushInternal(T? value)
        {
            if (maxSize > 0 && _count >= maxSize)
            {
                return (-1, -1);
            }
            if (_count >= Capacity)
            {
                ResizeInternal(Math.Max(Capacity * 2, DefaultStackSize));
            }
            int pos = _count++;

            ref var vv = ref _stack[pos];
            if (value.HasValue) vv.value = value.Value;
            unchecked { vv.version++; }

            _maxReachedCount = Math.Max(_maxReachedCount, _count);
            return (pos, vv.version);
        }
        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and pre assigns the reference to <paramref name="value"/>.
        /// </summary>
        public ref T PushValue(T value)
        {
            (int i, int _) = PushInternal(value);

            if (i < 0)
            {
                _stub = default;
                return ref _stub;
            }
            return ref _stack[i].value;
        }
        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and returns the resulting auto defaulted value.
        /// </summary>
        /// <param name="reset">Clears the previously existing value and sets it <see langword="default"/>.</param>
        public ref T PushValue(bool reset = true)
        {
            T? pushValue = null;
            if (reset) pushValue = default(T);
            (int i, int v) = PushInternal(pushValue);

            if (i < 0)
            {
                _stub = default;
                return ref _stub;
            }
            return ref _stack[i].value;
        }
        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and pre assigns the reference to <paramref name="value"/>.
        /// </summary>
        public Handle Push(T value)
        {
            (int i, int v) = PushInternal(value);

            if (i < 0) return Handle.Stub();
            return Handle.AtUnchecked(_stack, i, v);
        }
        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and returns the resulting auto defaulted value.
        /// </summary>
        /// <param name="reset">Clears the previously existing value and sets it <see langword="default"/>.</param>
        public Handle Push(bool reset = true)
        {
            T? pushValue = null;
            if (reset) pushValue = default(T);
            (int i, int v) = PushInternal(pushValue);

            if (i < 0) return Handle.Stub();
            return Handle.AtUnchecked(_stack, i, v);
        }
        private (int index, int version) PopInternal()
        {
            if (!HasAny) return (-1, -1);

            int pos = --_count;

            ref (T val, int ver) v = ref _stack[pos];
            unchecked { v.ver++; }
            return (pos, v.ver);
        }
        /// <summary>
        /// Returns the last popped value or stub, depending on having any value.
        /// </summary>
        public ref T PopValue()
        {
            (int i, int _) = PopInternal();
            if (i < 0)
            {
                _stub = default;
                return ref _stub;
            }
            return ref _stack[i].value;
        }
        /// <summary>
        /// Pops a value of type <typeparamref name="T"/>.
        /// Returned handle is "out of bounds" and can be overwritten - treat it as perishable.
        /// </summary>
        public Handle Pop()
        {
            (int i, int v) = PopInternal();
            if (i < 0) return Handle.Stub();
            return Handle.AtUnchecked(_stack, i, v);
        }
        private readonly int PeekInternal()
        {
            if (!HasAny) return -1;
            return _count - 1;
        }
        /// <summary>
        /// Returns the last pushed value.
        /// </summary>
        public readonly ref T PeekValue()
        {
            int p = PeekInternal();
            if (p < 0)
            {
                _stub = default;
                return ref _stub;
            }
            return ref _stack[p].value;
        }
        /// <summary>
        /// Returns a handle to the last pushed value.
        /// </summary>
        public readonly Handle Peek()
        {
            int p = PeekInternal();
            if (p < 0) return Handle.Stub();
            return Handle.AtUnchecked(_stack, p, _stack[p].version);
        }
        /// <summary>
        /// Clears the contents.
        /// </summary>
        /// <param name="resetValues">Whether to reset the values to <see langword="default"/> of <typeparamref name="T"/>.</param>
        public void Clear(bool resetValues = true)
        {
            if (resetValues)
            {
                int count = Math.Min(_stack.Length, _maxReachedCount);
                for (int i = 0; i < count; i++)
                {
                    ref var vv = ref _stack[i];
                    vv.value = default;
                    unchecked { vv.version++; }
                }
            }

            _maxReachedCount = _count = 0;
        }

        public readonly IEnumerator<T> GetEnumerator()
        {
            // Treat GetEnumerator as a snapshot (no version, count is constant, only _stack_ has bound checks.)
            // Captured locals aren't by-ref to this as the enumerator is essentially a 
            // As an extra measure, could just use the indexer with a reference to the struct from a heap resource..
            for (int i = 0; i < _count; i++)
            {
                yield return _stack[i].value;
            }
        }
        readonly IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public readonly void CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of array range");
            if ((index + Count) > array.Length) throw new ArgumentException("Array is too small to copy to index", nameof(array));
            for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
        }
        public readonly void CopyTo(T[] array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of array range");
            if ((index + Count) > array.Length) throw new ArgumentException("Array is too small to copy to index", nameof(array));
            for (int i = 0; i < Count; i++) array[index + i] = this[i];
        }
        public delegate void CopyAction(T self, ref T to);
        public readonly void CopyTo(ref ValueStack<T> vs, CopyAction action = null)
        {
            for (int i = 0; i < _count; i++)
            {
                if (action == null) { vs.PushInternal(this[i]); }
                else { action(this[i], ref vs[vs.PushInternal(null).index]); }
            }
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(_stack, _count, maxSize);
        }
    }
}
