using System.Text;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class BufferedStreamReaderUnitTests : ReaderUnitTests<SJBufferedStreamReader>
{
    public override SJBufferedStreamReader FromString(string data)
    {
        using var sr = new StringReader(data);
        var reader = SJBufferedStreamReader.Rented();
        reader.LoadFrom(sr);
        return reader;
    }
    public override SJBufferedStreamReader FromStream(Stream data, Encoding? enc)
    {
        var reader = SJBufferedStreamReader.Rented();
        reader.LoadFrom(data, enc);
        return reader;
    }
}
