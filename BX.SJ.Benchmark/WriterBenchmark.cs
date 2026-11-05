using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace BX.SJ.Benchmark;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public abstract class WriterBenchmark<TWriter> where TWriter : SJWriter
{
    public const int NWriteIters = 256;
    public const int NWriteLargeIters = 32;
    public const int NWriteElem = 512;
    public const int NWriteLargeElem = 16384;
    public const int WriteIntoArrayEveryN = 16; // 16 KV, 1 K 16 Array entries

    public const int RandomKeySize = 8, RandomValueSize = 16;
    public const int RandomKeyStringsCount = 512, RandomValueStringsCount = 512;
    public static readonly string randomizedKeyStrings = string.Create(RandomKeySize * RandomKeyStringsCount, 0, (c, _) => RandomString(ref c)),
        randomizedValueStrings = string.Create(RandomValueSize * RandomKeyStringsCount, 0, (c, _) => RandomString(ref c));

    public const string SmallCategoryName = "write ~16KB";
    public const string LargeCategoryName = "write ~1MB";

    [ThreadStatic]
    private static Random? rand;
    public static void RandomString(ref Span<char> result)
    {
        rand ??= new();
        const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz012345678901";
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Chars[rand.Next(Chars.Length)];
        }
    }
    public static ReadOnlySpan<char> GetRandomKey(int n)
    {
        return randomizedKeyStrings.AsSpan((RandomKeySize * n) % randomizedKeyStrings.Length, RandomKeySize);
    }
    public static ReadOnlySpan<char> GetRandomValue(int n)
    {
        return randomizedValueStrings.AsSpan((RandomValueSize * n) % randomizedValueStrings.Length, RandomValueSize);
    }

    // Write
    public abstract TWriter CreateWriter();
    public abstract bool DisposeWriter(TWriter writer);
    public virtual int WriteDataOnly(TWriter writer, int iters, int elemCount)
    {
        writer.Reset();

        // Baseline should also do the benchmark chunking, but it won't do the "JSON details"
        for (int i = 0; i < iters / 2; i++)
        {
            // Append is exposed, so I will just do this.
            // I exposed the Reader internals because of this as well.
            ReadOnlySpan<char> k = GetRandomKey(i), v = GetRandomValue(i);
            // Generally, these values are copied char by char to escape them.
            // This is more of a test for SJEscape's speed.. Meh, let the larp predictor do it's work.
            writer.Append(k);
            writer.S.count += k.Length;
            writer.Append(v);
            writer.S.count += v.Length;
        }
        for (int i = 0; i < iters / 2; i++)
        {
            ReadOnlySpan<char> v = GetRandomValue(i);
            writer.Append(v);
            writer.S.count += v.Length;
        }

        return writer.S.count;
    }

    public int BenchWrite(SJWriter writer, int iters, int elemCount)
    {
        writer.Reset();

        for (int i = 0; i < iters / 2; i++)
        {
            writer.Reset();

            using (writer.Object())
            {
                for (int j = 0; j < elemCount; j++) writer.WriteKV(GetRandomKey(i), GetRandomValue(i));
            }
        }
        for (int i = 0; i < iters / 2; i++)
        {
            writer.Reset();

            using (writer.Array())
            {
                for (int j = 0; j < elemCount; j++) writer.WriteString(GetRandomValue(i));
            }
        }

        return writer.S.count;
    }

    // Length (generated with default settings) = 15857 for normal, 507559 for large
    [BenchmarkCategory(SmallCategoryName), Benchmark(Baseline = true)]
    public int WriteBaseline()
    {
        var writer = CreateWriter();
        try
        {
            return WriteDataOnly(writer, NWriteIters, NWriteElem);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [BenchmarkCategory(SmallCategoryName), Benchmark]
    public int Write()
    {
        var writer = CreateWriter();
        try
        {
            return BenchWrite(writer, NWriteIters, NWriteElem);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [BenchmarkCategory(LargeCategoryName), Benchmark(Baseline = true)]
    public int WriteLargeBaseline()
    {
        var writer = CreateWriter();
        try
        {
            return WriteDataOnly(writer, NWriteLargeIters, NWriteLargeElem);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
    [BenchmarkCategory(LargeCategoryName), Benchmark]
    public int WriteLarge()
    {
        var writer = CreateWriter();
        try
        {
            return BenchWrite(writer, NWriteLargeIters, NWriteLargeElem);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }
}
