namespace BX.SJ.Tests.Impl;
    
[TestClass]
public sealed class StringWriterUnitTests : WriterUnitTests<SJStringWriter>
{
    public override SJStringWriter CreateWriter() => new();
}
