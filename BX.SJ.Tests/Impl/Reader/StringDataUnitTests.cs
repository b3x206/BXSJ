using System.Text;

namespace BX.SJ.Tests.Impl;

[TestClass]
public sealed class StringDataUnitTests : ReaderUnitTests<SJStringReader>
{
    public override SJStringReader FromString(string data) => new(data);
    public override SJStringReader FromStream(Stream data, Encoding? enc)
    {
        ArgumentNullException.ThrowIfNull(data);
        // This just reads the text using the STL.
        // > This will make more sense for extensions (through stuff like BXSave)
        // need the encoding as StreamReader does not detect C# unicode and makes mojibake :P
        // Well I suppose they learnt something from IsTextUnicode() and gave up with "BOM"
        // Could write the preamble, but meh.
        using var sr = enc is not null ? new StreamReader(data, enc) : new StreamReader(data);
        return new(sr.ReadToEnd());
    }
}
