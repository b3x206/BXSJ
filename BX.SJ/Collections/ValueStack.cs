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
    /// vs.Pop(); // Does not clear.
    ///           // The "otherInt" handle cannot be reused, it will return a stub and silently ignore.
    ///           // You can retain a reference to the value address by doing `ref otherInt.Value`.
    ///           // It is faster but only safe if you know the value won't be popped.
    ///           // > C# probably keeps the array a bit longer if refs to members exist, but don't trust assumptions.
    /// vs.Push(clear: false); // Retains 512, even though it should be 0.
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
            private readonly int index, version;
            /// <summary>
            /// Returns <see langword="true"/> if the handle to the stack is valid.
            /// </summary>
            public readonly bool IsValid => index >= 0 && index < _ownerStack?.Length && _ownerStack[index].version == version;
            /// <summary>
            /// Returns <see langword="true"/> for strictly stub values, not for invalid values.
            /// </summary>
            public readonly bool IsStub => index < 0 || _ownerStack == null;

            // A roslyn analyzer emitting a warning if the result of this is stored would be great here.
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
                var array = v._stack;
                if (index < 0 || index >= array?.Length) return Stub();
                return new Handle(array, index, array[index].version);
            }
            public static Handle At((T value, int version)[] array, int index)
            {
                if (index < 0 || index >= array?.Length) return Stub();
                return new Handle(array, index, array[index].version);
            }
            internal static Handle AtUnchecked((T, int)[] array, int index, int version)
            {
                return new Handle(array, index, version);
            }
        }

        private (T value, int version)[] _stack;
        private int _count;
        public int maxSize;
        public readonly int Capacity => _stack.Length;
        public readonly int Count => _count;

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

        public static ValueStack<T> Create()
        {
            return new ValueStack<T>(DefaultStackSize, DefaultMaxSize);
        }
        public ValueStack(int capacity = DefaultStackSize, int maxSize = DefaultMaxSize)
        {
            _stack = new (T, int)[maxSize > 0 && maxSize < capacity ? maxSize : capacity];
            _count = 0;
            this.maxSize = maxSize;
        }

        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and pre assigns the reference to <paramref name="value"/>.
        /// </summary>
        public Handle Push(T value)
        {
            if (maxSize > 0 && _count >= maxSize)
            {
                return Handle.Stub();
            }
            if (_count >= (_stack?.Length ?? 0))
            {
                int size = Math.Max((_stack?.Length ?? 0) * 2, DefaultStackSize);
                if (maxSize > 0) size = Math.Min(maxSize, size);
                Array.Resize(ref _stack, size);
            }

            int pos = _count++;
            _stack[pos].value = value;
            return Handle.At(this, pos);
        }
        /// <summary>
        /// Pushes a value of type <typeparamref name="T"/> and returns the resulting auto defaulted value.
        /// </summary>
        /// <param name="clear">Clears the previously existing value and sets it <see langword="default"/>.</param>
        public Handle Push(bool clear = true)
        {
            if (maxSize > 0 && _count >= maxSize)
            {
                _stub = default;
                return Handle.Stub();
            }
            if (_count >= (_stack?.Length ?? 0))
            {
                int size = Math.Max((_stack?.Length ?? 0) * 2, DefaultStackSize);
                if (maxSize > 0) size = Math.Min(maxSize, size);
                Array.Resize(ref _stack, size);
            }

            int pos = _count++;
            ref (T val, int ver) v = ref _stack[pos];
            if (clear) v.val = default;
            unchecked { v.ver++; }
            return Handle.AtUnchecked(_stack, pos, v.ver);
        }
        /// <summary>
        /// Pops a value of type <typeparamref name="T"/>.
        /// Returned handle is "out of bounds" and can be overwritten - treat it as perishable.
        /// </summary>
        public Handle Pop()
        {
            if (_count <= 0) return Handle.Stub();
            int pos = --_count;
            ref (T val, int ver) v = ref _stack[pos];
            unchecked { v.ver++; }
            return Handle.AtUnchecked(_stack, pos, v.ver);
        }
        /// <summary>
        /// Returns a handle to the last pushed value.
        /// </summary>
        public readonly Handle Peek()
        {
            if (_count <= 0) return Handle.Stub();
            return Handle.At(this, _count - 1);
        }

        public readonly IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return _stack[i].value;
        }
        readonly IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public readonly void CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range");
            if (index + Count > array.Length) throw new ArgumentException("Array is too small to copy to index", nameof(array));
            for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
        }
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(_stack, _count);
        }
    }
}
