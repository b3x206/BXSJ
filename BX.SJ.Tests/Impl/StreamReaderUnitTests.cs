using System.Text;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class StreamReaderUnitTests : ReaderUnitTests<SJStreamReader>
{
    public override SJStreamReader CreateFromStream(Stream data, Encoding? enc)
    {
        var reader = new SJStreamReader();
        reader.LoadDataFrom(data, enc);
        return reader;
    }
    public override SJStreamReader CreateFromString(string data)
    {
        var reader = new SJStreamReader();
        using var sr = new StringReader(data);
        reader.LoadDataFrom(sr);
        return reader;
    }

    [TestMethod]
    public override void TestDisposeTwice()
    {
        base.TestDisposeTwice();
    }
}
