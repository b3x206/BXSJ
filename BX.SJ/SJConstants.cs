using System;

namespace BX.SJ
{
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
        public const int MaxWriterPrefixLength = 32;
        public const string NumberFormat = "G";

        // UTF-8 constants (because no "string"u8. I could increase the <LangVersion>, but I'd rather stay safe)
        private static readonly byte[] sjType_Error = new byte[] { 69, 114, 114, 111, 114 };
        private static readonly byte[] sjType_Number = new byte[] { 78, 117, 109, 98, 101, 114 };
        private static readonly byte[] sjType_Key = new byte[] { 75, 101, 121 };
        private static readonly byte[] sjType_String = new byte[] { 83, 116, 114, 105, 110, 103 };
        private static readonly byte[] sjType_Bool = new byte[] { 66, 111, 111, 108 };
        private static readonly byte[] sjType_Null = new byte[] { 78, 117, 108, 108 };
        private static readonly byte[] sjType_Object = new byte[] { 79, 98, 106, 101, 99, 116 };
        private static readonly byte[] sjType_Array = new byte[] { 65, 114, 114, 97, 121 };
        private static readonly byte[] sjType_End = new byte[] { 69, 110, 100 };
        private static readonly byte[] sjType_Comment = new byte[] { 67, 111, 109, 109, 101, 110, 116 };
        private static readonly byte[] sjType_Type = new byte[] { 84, 121, 112, 101 };
        private static readonly byte[] sjType_Unknown = new byte[] { 85, 110, 107, 110, 111, 119, 110 };

        public static ReadOnlySpan<byte> ToStringU8(this SJType type)
        {
            return type switch
            {
                SJType.Error => sjType_Error,
                SJType.Number => sjType_Number,
                SJType.Key => sjType_Key,
                SJType.String => sjType_String,
                SJType.Bool => sjType_Bool,
                SJType.Null => sjType_Null,
                SJType.Object => sjType_Object,
                SJType.Array => sjType_Array,
                SJType.End => sjType_End,
                SJType.Comment => sjType_Comment,
                SJType.Type => sjType_Type,
                _ => sjType_Unknown
            };
        }
    }
}
