namespace BX.SJ
{
    /// <summary>
    /// Contains constants related to SJ.
    /// </summary>
    public static class SJConstants
    {
        public enum Literal
        {
            None,

            False,
            True,

            Null, // Must be the last literal
        }
        public static string ToToken(this Literal v) => v switch
        {
            Literal.False => "false",
            Literal.True => "true",
            Literal.Null => "null",
            _ => ""
        };

        // Setting constants
        public const int MaxDepth = 128;
        public const int MaxInterBufferSize = 512;
        public const int MaxWriterPrefixLength = 32;
        public const string NumberFormat = "G";
    }
}
