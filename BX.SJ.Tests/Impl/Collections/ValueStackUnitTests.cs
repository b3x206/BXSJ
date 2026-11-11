using BX.SJ.Collections;
using System.Collections;

namespace BX.SJ.Tests.Impl.Collections;

[TestClass]
public sealed class ValueStackUnitTests
{
    static void HandleValueEquals(ValueStack<int>.Handle handle, int expected)
    {
        Assert.IsTrue(handle.IsValid);
        Assert.IsFalse(handle.IsStub);
        Assert.AreEqual(expected, handle.Value);
    }
    static void StubResultAfter(Func<ValueStack<int>.Handle> act)
    {
        var h = act();
        Assert.IsFalse(h.IsValid);
        Assert.IsTrue(h.IsStub);
        Assert.AreEqual(default, h.Value);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void Stack()
    {
        const int Value = 1;

        var ints = ValueStack<int>.Create();

        ints.Push(Value);
        Assert.AreEqual(1, ints.Count);
        HandleValueEquals(ints.Peek(), Value);

        ints.Push();
        Assert.AreEqual(2, ints.Count);
        HandleValueEquals(ints.Peek(), default);

        // Order is reversed
        HandleValueEquals(ints.Pop(), default);
        Assert.AreEqual(1, ints.Count);

        HandleValueEquals(ints.Pop(), Value);
        Assert.AreEqual(0, ints.Count);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void HandleInvalidation()
    {
        const int Value = 1;
        var ints = ValueStack<int>.Create();

        var handle = ints.Push(Value);
        var poppedHandle = ints.Pop();

        Assert.IsTrue(poppedHandle.IsValid);
        Assert.IsFalse(poppedHandle.IsStub);
        Assert.AreEqual(Value, poppedHandle.Value);

        Assert.IsFalse(handle.IsValid);
        Assert.IsFalse(handle.IsStub);
        Assert.AreEqual(default, handle.Value);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void HandleDefaultConfig()
    {
        ValueStack<int> ints = default;

        HandleValueEquals(ints.Push(0), 0);
        HandleValueEquals(ints.Push(1), 1);
        HandleValueEquals(ints.Push(2), 2);
        HandleValueEquals(ints.Push(3), 3);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    public void StackClear(int count)
    {
        static void LastValueEquals(ValueStack<int> vs, int count, int equals)
        {
            for (int i = 0; i < count; i++)
            {
                vs.Push(reset: false);
            }
            Assert.AreEqual(equals, vs.Peek().Value);
        }

        var vs = ValueStack<int>.Create();
        for (int i = 0; i < count; i++) vs.Push(i);
        vs.Clear(resetValues: true);
        LastValueEquals(vs, count, default);

        // This could assume that Clear works properly, so recreate the vs instead of calling Clear.
        vs = ValueStack<int>.Create();
        for (int i = 0; i < count; i++) vs.Push(i);
        vs.Clear(resetValues: false);
        LastValueEquals(vs, count, count - 1);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void OutOfBoundsPopPushPeek()
    {
        const int Value = 1;
        var ints = new ValueStack<int>(maxSize: 1);
        Assert.AreEqual(1, ints.Capacity);

        ints.Push(Value);
        StubResultAfter(() => ints.Push());

        ints.Pop();
        StubResultAfter(() => ints.Pop());

        // Peek into empty must return stub
        StubResultAfter(() => ints.Peek());

        // OOB reads must fail
        ints.Push(Value);
        Assert.AreEqual(default, ints[int.MinValue]);
        Assert.AreEqual(default, ints[-1]);
        Assert.AreEqual(default, ints[int.MaxValue]);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(0)]
    [DataRow(ValueStack<int>.DefaultStackSize * 2)]
    public void PushGrows(int maxSize)
    {
        var ints = new ValueStack<int>(capacity: ValueStack<int>.DefaultStackSize, maxSize: maxSize);
        
        switch (maxSize)
        {
            case <= 0:
                int startingCapacity = ints.Capacity;
                for (int i = 0; ints.Capacity == startingCapacity; i++)
                {
                    ints.Push();
                    Assert.IsLessThanOrEqualTo(
                        startingCapacity, i, "Exceeded expected size, Capacity reflects that the stack didn't resize"
                    );
                }
                break;

            default:
                for (int i = 0; i <= maxSize; i++)
                {
                    if (i >= maxSize)
                    {
                        StubResultAfter(() => ints.Push());
                        StubResultAfter(() => ints.Push(1));
                    }
                    else
                    {
                        ints.Push();
                    }
                }
                break;
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(16)]
    public void StackIEnumerable(int expectedCount)
    {
        var ints = ValueStack<int>.Create();
        for (int i = 0; i < expectedCount; i++) ints.Push();

        int c = 0;
        foreach (int v in ints)
        {
            Assert.AreEqual(default, v);
            c++;
        }
        Assert.AreEqual(expectedCount, c);

        c = 0;
        foreach (object v in (IEnumerable)ints)
        {
            // default is boxed, so cast v to int
            Assert.AreEqual(default, (int)v);
            c++;
        }
        Assert.AreEqual(expectedCount, c);

        c = 0;
        for (int i = 0; i < ints.Count; i++, c++)
        {
            Assert.AreEqual(default, ints[i]);
        }
        Assert.AreEqual(expectedCount, c);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(16)]
    public void ValueKeptOnNonClearPush(int count)
    {
        if (count <= 0) throw new ArgumentException("Count must be larger than 0", nameof(count));
        const int Value = 1;
        var ints = ValueStack<int>.Create();
        for (int i = 0; i < count; i++)
        {
            ints.Push(Value);
        }

        ints.Pop();
        ints.Push(reset: false);
        Assert.AreEqual(Value, ints.Peek().Value);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    public void StackCopyTo(int count)
    {
        if (count <= 0) throw new ArgumentException("Count must be larger than 0", nameof(count));

        const int Value = 3;
        int[] a = new int[count];
        var s = ValueStack<int>.Create();
        for (int i = 0; i < count; i++) s.Push(Value);

        s.CopyTo(a, 0);
        Assert.ContainsAll(Enumerable.Repeat(Value, count), a);
        Array.Fill(a, default);

        Assert.Throws<ArgumentNullException>(() => s.CopyTo(null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.CopyTo(a, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.CopyTo(a, a.Length + 1));
        Assert.Throws<ArgumentException>(() => s.CopyTo(a, 1));

        a = new int[a.Length + 1];
        s.CopyTo(a, 1);
        Assert.ContainsAll(Enumerable.Repeat(Value, count), a.AsSpan(1).ToArray());
    }

    [TestMethod]
    public void StackHashCode()
    {
        var s1 = new ValueStack<int>();
        s1.Push();

        var s2 = new ValueStack<int>();
        s2.Push(1);

        var s3 = ValueStack<int>.Create();
        
        ValueStack<int> s4 = default;

        Assert.AreAllDistinct([s1.GetHashCode(), s2.GetHashCode(), s3.GetHashCode(), s4.GetHashCode()]);
    }
}
