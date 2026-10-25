using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;
using System.Text;

namespace BX.SJ.Benchmark
{
    [MemoryDiagnoser]
    public class SJStringReaderBenchmark : ReaderBenchmark<SJStringReader>
    {
        public string smallText = string.Empty;
        public string largeText = string.Empty;

        public Dictionary<string, SJStringReader> readerDict = [];
        public SJStringReaderBenchmark() : base()
        {
            readerDict[JsonFileValidName] = CreateFromFile(JsonFileValidName);
            readerDict[JsonFileLargeName] = CreateFromFile(JsonFileLargeName);
        }

        public override SJStringReader CreateFromStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var sr = new StreamReader(stream);
            var reader = new SJStringReader(sr.ReadToEnd());
            return reader;
        }
        public override SJStringReader CreateFromFile(string fileName)
        {
            if (readerDict.TryGetValue(fileName, out var v))
            {
                v.Reset();
                return v;
            }

            return base.CreateFromFile(fileName);
        }
        public override bool DisposeReader(SJStringReader reader) => false;
    }
    [MemoryDiagnoser]
    public class SJStringWriterBenchmark : WriterBenchmark<SJStringWriter>
    {
        public static readonly StringBuilder sharedBuilder = new(524288);

        public override SJStringWriter CreateWriter()
        {
            sharedBuilder!.Clear();
            return new(sharedBuilder);
        }
        public override bool DisposeWriter(SJStringWriter writer) => false;
    }

    [MemoryDiagnoser]
    public class SJStreamReaderBenchmark : ReaderBenchmark<SJStreamReader>, IDisposable
    {
        public Dictionary<string, SJStreamReader>? readerDict = [];
        private bool disposedValue;

        public SJStreamReaderBenchmark() : base()
        {
            readerDict[JsonFileValidName] = CreateFromStream(File.Open(JsonFileValidName, FileMode.Open));
            readerDict[JsonFileLargeName] = CreateFromStream(File.Open(JsonFileLargeName, FileMode.Open));
        }

        public override SJStreamReader CreateFromStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            var sr = new SJStreamReader();
            sr.LoadDataFrom(stream);
            return sr;
        }
        public override SJStreamReader CreateFromFile(string fileName)
        {
            if (readerDict!.TryGetValue(fileName, out var v))
            {
                v.Reset();
                return v;
            }

            return base.CreateFromFile(fileName);
        }
        // As much as I'd like the DisposeReader to work, you aren't supposed to
        // "dispose" the stream reader/writer until you are done with it...
        // SJ isn't designed for "stream" reading, so the stream readers are fake
        public override bool DisposeReader(SJStreamReader reader) => false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    foreach (var v in readerDict!) v.Value.Dispose();
                }

                readerDict = null;
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    [MemoryDiagnoser]
    public class SJStreamWriterBenchmark : WriterBenchmark<SJStreamWriter>, IDisposable
    {
        private bool disposedValue = false;
        public readonly MemoryStream sharedMemoryStream = new(capacity: 524288);

        public override SJStreamWriter CreateWriter() => new(sharedMemoryStream);
        public override bool DisposeWriter(SJStreamWriter writer) => false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    sharedMemoryStream.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    sealed class BenchProgram
    {
        static void Main()
        {
            var config = DefaultConfig.Instance.WithSummaryStyle(SummaryStyle.Default.WithTimeUnit(TimeUnit.Millisecond));
            BenchmarkRunner.Run([
                typeof(SJStringReaderBenchmark), typeof(SJStringWriterBenchmark),
                typeof(SJStreamReaderBenchmark), typeof(SJStreamWriterBenchmark)
            ], config);
        }
    }
}
