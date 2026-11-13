using static BX.SJ.Tests.TestData;

namespace BX.SJ.Tests.Examples;

[TestClass]
public sealed class ExamplesUnitTests
{
    public static IEnumerable<string[]> TestDataProcessors => [
        [JsonData1],
        [JsonData2],
        [JsonData3],
        [JsonDataDiscard],
        [JsonDataRootNull],
        [JsonDataRootNumber],
        [JsonDataRootBoolFalse],
        [JsonDataRootBoolTrue],
        [JsonDataRootString],
        [DataEmpty],
    ];
    public static IEnumerable<string[]> JscTestDataProcessors => [
        [JsonDataComment],
    ];

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestReader()
    {
        Reader.TMain([]);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestReaderJSC()
    {
        ReaderJSC.TMain([]);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestReaderEx()
    {
        ReaderEx.TMain([]);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(TestDataProcessors))]
    public void TestPrinter(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            Printer.TMain([AppDomain.CurrentDomain.FriendlyName]);
        }
        else
        {
            Printer.TMain([AppDomain.CurrentDomain.FriendlyName, data]);
        }
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestWriter()
    {
        Writer.TMain([]);
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    public void TestWriterEx()
    {
        WriterEx.TMain([]);
    }

    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(TestDataProcessors))]
    public void TestTreeFrom(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        var reader = new SJReader(new SJStringData(data)) { ThrowOnError = true };
        var tree = SJTree.FromJSON(reader);

        Console.WriteLine($"Parsed Tree : {tree}");
    }
    [TestMethod]
    [Timeout(TestTimeouts.Short)]
    [DynamicData(nameof(TestDataProcessors))]
    public void TestTreeTo(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        var reader = new SJReader(new SJStringData(data)) { ThrowOnError = true };
        var tree = SJTree.FromJSON(reader);

        Console.WriteLine($"Tree as JSON : {SJTree.ToJSON(tree)}");
    }
}
