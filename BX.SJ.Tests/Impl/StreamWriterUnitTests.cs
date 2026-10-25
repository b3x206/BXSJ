namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class StreamWriterUnitTests : WriterUnitTests<SJStreamWriter>
{
    public override SJStreamWriter CreateWriter() => new(new MemoryStream());
    public override bool DisposeWriter(SJStreamWriter writer)
    {
        writer?.Dispose();
        return true;
    }

    [TestMethod]
    public override void TestDisposeTwice()
    {
        base.TestDisposeTwice();
    }
}
