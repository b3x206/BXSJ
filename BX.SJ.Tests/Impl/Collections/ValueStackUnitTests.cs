using BX.SJ.Collections;
using System.Collections;

namespace BX.SJ.Tests.Impl.Collections;

[TestClass]
public sealed class ValueStackUnitTests
{
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void Stack()
    {
        var ints = ValueStack<int>.Create();

        const int Value = 1;
        ints.Push(Value);
        Assert.AreEqual(1, ints.Count);
        ValueStack<int>.Handle handle = ints.Peek();
        Assert.IsTrue(handle.IsValid);
        Assert.IsFalse(handle.IsStub);
        Assert.AreEqual(Value, handle.Value);

        ints.Push();
        Assert.AreEqual(2, ints.Count);
        ValueStack<int>.Handle handle2 = ints.Peek();
        Assert.IsTrue(handle2.IsValid);
        Assert.IsFalse(handle2.IsStub);
        Assert.AreEqual(default, handle2.Value);

        // Order is reversed
        ValueStack<int>.Handle handle3 = ints.Pop();
        Assert.AreEqual(1, ints.Count);
        Assert.IsTrue(handle3.IsValid);
        Assert.IsFalse(handle3.IsStub);
        Assert.AreEqual(default, handle3.Value);

        ValueStack<int>.Handle handle4 = ints.Pop();
        Assert.AreEqual(0, ints.Count);
        Assert.IsTrue(handle4.IsValid);
        Assert.IsFalse(handle4.IsStub);
        Assert.AreEqual(Value, handle4.Value);
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
    public void OutOfBoundsPopPushPeek()
    {
        const int Value = 1;
        var ints = new ValueStack<int>(maxSize: 1);
        Assert.AreEqual(1, ints.Capacity);

        static void TestStubResult(Func<ValueStack<int>.Handle> act)
        {
            var h = act();
            Assert.IsFalse(h.IsValid);
            Assert.IsTrue(h.IsStub);
        }

        ints.Push(Value);
        TestStubResult(() => ints.Push());

        ints.Pop();
        TestStubResult(() => ints.Pop());

        // Peek into empty must return stub
        TestStubResult(() => ints.Peek());

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
    public void PushGrow(int maxSize)
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
                    var h = ints.Push();
                    if (i >= maxSize)
                    {
                        Assert.IsFalse(h.IsValid);
                        Assert.IsTrue(h.IsStub);
                    }
                }
                break;
        }
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DataRow(1)]
    [DataRow(16)]
    public void IEnumerableCorrect(int expectedCount)
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
        ints.Push(clear: false);
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

    // GetHashCode does not need coverage, HashSet<T> has different cases to handle.
}
