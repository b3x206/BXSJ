namespace BX.SJ.Tests;

public static class AssertEx
{
    public static void IsNullOrEmpty(this Assert _, string? data) => IsNullOrEmpty(_, data, null);
    public static void IsNullOrEmpty(this Assert _, string? data, string? message)
    {
        Assert.IsTrue(string.IsNullOrEmpty(data), message ?? $"Expected string data to be null or empty, got '{data}'");
    }
    public static void IsNotNullOrEmpty(this Assert _, string? data) => IsNotNullOrEmpty(_, data, null);
    public static void IsNotNullOrEmpty(this Assert _, string? data, string? message)
    {
        Assert.IsFalse(string.IsNullOrEmpty(data), message ?? $"Expected string data to be not null or empty, got {(data is null ? "null data" : "empty data")} instead");
    }

    public static void AreEqual(this Assert _, ReadOnlySpan<char> expected, ReadOnlySpan<char> actual)
    {
        Assert.IsTrue(MemoryExtensions.SequenceEqual(expected, actual), $"Expected string data to equal '{expected}', got '{actual}'");
    }
}
