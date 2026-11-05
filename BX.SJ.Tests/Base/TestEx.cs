namespace BX.SJ.Tests;

/// <summary>
/// Assorted extensions to testing.
/// </summary>
public static class TestEx
{
    /// <summary>
    /// Create a list of truncated data processors, going from the max length within the collection of collection's max size to zero.
    /// </summary>
    /// <param name="strings">List of string arguments.</param>
    /// <returns>Truncated list of strings iterable to cause new and fun errors!</returns>
    public static IEnumerable<string[]> CreateTruncatedDataProcessors(IEnumerable<string[]> strings, bool startTruncated = true)
    {
        foreach (var args in strings)
        {
            if (args.Length <= 0) continue;

            string[] truncArgs = new string[args.Length];
            int maxLength = Math.Max(args.Max(v => v.Length) - 1, 0);
            for (int i = startTruncated ? 1 : 0; i <= maxLength; i++)
            {
                bool allEmpty = true;
                for (int j = 0; j < args.Length; j++)
                {
                    string arg = args[j];
                    // this method makes some args empty, but that's ok.
                    truncArgs[j] = arg[..Math.Max(arg.Length - i, 0)]; // tung tung args
                    allEmpty = allEmpty && string.IsNullOrEmpty(truncArgs[j]);
                }

                // Skip this if it's empty, so that we don't do the empty string 129450812945 times. (that test should be explicit anyway)
                if (!allEmpty)
                {
                    yield return truncArgs;
                }
            }
        }
    }
}
