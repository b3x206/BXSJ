namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class LinearWriterUnitTests : WriterUnitTests<SJLinearWriter>
{
    public override SJLinearWriter CreateWriter() => new();

    [TestMethod]
    public override void TestDisposeTwice()
    {
        base.TestDisposeTwice();
    }
}
