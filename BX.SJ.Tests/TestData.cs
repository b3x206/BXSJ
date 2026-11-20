using System.Reflection;

namespace BX.SJ.Tests;

/// <summary>
/// Contains JSON data to test against.
/// </summary>
public static class TestData
{
    // JSON Data
    // - Root Data
    public const string
        // JSON compatible root data
        JsonDataStringContent = "Hello world!",
        JsonDataEmptyObject = @"{}", JsonDataEmptyArray = @"[]", JsonDataString = $@"""{JsonDataStringContent}""",
        JsonDataNumber = @"42.42", JsonDataBoolFalse = @"false", JsonDataBoolTrue = @"true", JsonDataNull = @"null",
        // Special BXSJ data
        BXSJDataType = $@"(HelloWorld)", BXSJDataRootStringType = $@"(""{JsonDataStringContent} () \"""")",
        BXSJDataSingleComment = $@"// {JsonDataStringContent}", BXSJDataNewlineSingleComment = $"// {JsonDataStringContent}\r\n",
        BXSJDataMultiComment = $@"/* {JsonDataStringContent} */",
        // Other
        DataEmpty = "";
    public static IEnumerable<string[]> JsonRootDatas => [
        // Data, ExpectedCapture (if not using the tokenized output that gives BXSJ-adjacent output)
        [JsonDataEmptyObject, ""], [JsonDataEmptyArray, ""],
        [JsonDataString, JsonDataStringContent], [JsonDataNumber, JsonDataNumber],
        [JsonDataBoolFalse, JsonDataBoolFalse], [JsonDataBoolTrue, JsonDataBoolTrue],
        [JsonDataNull, JsonDataBoolFalse]
    ];
    public static IEnumerable<string[]> JsonTruncatedRootDatas() => TestEx.CreateTruncatedStringData(
        JsonRootDatas.Where(v => v.Length > 0 && v[0] != JsonDataNumber),
        argc: 1
    );
    // BXSJ root data assumes all tolerances are allowed.
    public static IEnumerable<string[]> BXSJRootDatas => [
        // Data, ExpectedCapture
        [BXSJDataType, BXSJDataType.Trim('(', ')')], [BXSJDataRootStringType, BXSJDataRootStringType.Trim('(', ')').Trim()[1..^1]],
        [BXSJDataSingleComment, BXSJDataSingleComment.TrimStart('/').Trim()], [BXSJDataNewlineSingleComment, BXSJDataNewlineSingleComment.TrimStart('/').Trim()],
        [BXSJDataMultiComment, BXSJDataMultiComment.Trim('/', '*', ' ')],
    ];
    public static IEnumerable<string[]> BXSJTruncatedRootDatas() => TestEx.CreateTruncatedStringData(
        JsonRootDatas.Where(v => v.Length > 0),
        argc: 1
    );
    public static IEnumerable<string[]> BXSJTypeDatas => [
        // Literals
        ["(Foo)"], ["(Bar)"], ["(Interesting_Type)"], [$"({AZaz09})"], ["(-_.=)"], ["(IsValid-Wow_Yep.ItReallyIs=Valid)"],
        // Strings
        ["(\"Spaces wow \")"], ["(\"String () wow\")"], ["(\"\\\"\\\"\\\" Quotes wow\")"], ["(\")(\\\"\\\"\\\"\")"],
        ["(\"🔥\")"], ["(\" <-- fire respect \")"], ["(\"\t\t\t\")"], ["(\"._-*0>£#$½{[]}\")"],
    ];

    // ---
    // String-only entries
    public const string AsciiUppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string AsciiLowercase = "abcdefghijklmnopqrstuvwxyz";
    public const string AsciiDigits = "0123456789";
    public const string HexDigits = "0123456789ABCDEFabcdef";
    public const string AZaz09 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public const string DataEmojis = "🔴🟡🟢😭";
    // Creates 1MB of UTF-16 string. Quite wholesome.
    public static readonly string DataEmojiSpam = string.Concat(Enumerable.Repeat("🅱️🅱️🅱️🍄‍🍕🍕🍕🍄‍🍄‍🍕🍄‍🍕🍕🍕🍕🍄‍🍕🍄‍", 10240));
    public const string FireSpamKey = "YoThisDataIsFire";
    public static readonly string FireSpamValue = string.Concat(Enumerable.Repeat("🔥", 32768));
    public const string DepthMaxxKey = "depthMaxx";
    public const string ObjectTwisterKey = "objectTwister";
    public const string SciNumsKey = "sciNum";
    public const string JsonDataDiscardKey = "secretInfo";
    // - Escape Data
    public const string EscapeContent = "Quote: \", Backslash: \\, Tab: \t, Newline: \n, Pizza: \uD83C\uDF55, The 🅱 variant: \uD83C\uDD71\uFE0F";
    public const string EscapeInvalidUnescape = "Oh wow, I'm escaping things I shouldn't! : \\a\\b\\c\\d\\e\\f\\g\\h\\i\\j\\k\\l\\m\\n\\o\\p\\q\\r\\s\\t\\u\\v\\w\\x\\y\\z\\A\\B\\C\\D\\E\\F\\G\\H\\I\\J\\K\\L\\M\\N\\O\\P\\Q\\R\\S\\T\\U\\V\\W\\X\\Y\\Z'";
    public const string EscapeInvalidSurrogate = "\uDF55\uD83C\uDD71\uD83C\uFE0F In C++, surrogate escape you!";
    public static readonly string EscapeContentAsciiSequence = string.Join("", Enumerable.Range(0, 255).Select(Convert.ToChar));
    public static readonly string EscapeContentEmojiTest = string.Join('\n', Enumerable.Range(0x2700, 0xBF).Select((v, i) => 
        string.Concat(char.ToString((char)v), ((i & 1) == 0) ? "ABCDEFGHIJKLMNOPQRSTUVWXYZ" : "abcdefghijklmnopqrstuvwxyz"))
    );

    public const string JsonData1 = @"{
        ""num"":42,
        ""num2"":-42,
        ""t"":true,
        ""f"":false,
        ""n"":null,
        """":"""",
        ""foo"":[""bar"",""baz""],
        ""mango"":{""6"":7}
}";
    public const string JsonData2 = @"{
    ""numbers"": [0, -1, 1.23, 1.0e-5, 1000000],
    ""strings"": {
        ""basic"": ""Hello World"",
        ""escaped"": ""Quote: \"", Backslash: \\, Tab: \t, Newline: \n"",
        ""unicode_BMP"": ""Euro: \u20AC"",
        ""emoji_surrogate"": ""Pizza: \uD83C\uDF55"",
        ""emoji_with_variant"": ""The 🅱 variant: \uD83C\uDD71\uFE0F"",
        ""raw_emoji"": ""🍕"",
        ""non_ascii_literal"": ""你好, ¡Hola!, Grüße""
    },
    ""nesting"": [{
        ""depth_1"": [
            { ""depth_2"": ""We're deep now"" }
        ]
    }],
    ""logic"": [true, false, null],
    ""empty"": { ""obj"": {}, ""arr"": [] }
}";
    public static readonly string JsonDataFireSpam = $@"{{
    ""{FireSpamKey}"": ""{FireSpamValue}""
}}";
    public const string JsonDataDiscard = $@"{{
    ""userName"": ""gamer"",
    ""password"": ""password"",
    ""{JsonDataDiscardKey}"": {{
        ""cardNumber"": 1234123412341234,
        ""hondaCivic"": 676,
        ""expiry"": ""6/7"",
        ""valid"": false,
        ""transactions"": [{{ ""date"": 453461254869, ""id"": ""jorjor well"" }}, {{ ""date"": 0, ""id"": null }}]
    }},
    ""stats"": {{
        ""score"": 99999999999,
        ""moneysWasted"": ""9000.24 USD"",
        ""lifetime"": 12000,
        ""skill"": 21498124
    }},
    ""badAtThisGame"": true
}}";

    public const string BxsjCommentData = @"/* Let's start our ""invalid"" JSON journey! */
// Also some more lines.
{
        // I like annotating my JSON, but it gets deleted when it's serialized :(
        /* wen eta fix this? */
        ""Array"": [1,2,3,4,5,6,7,8, /* oops convenient comment */ 9,10,11],
        ""Objects"": /* OH NO */ { ""Yes"": true, ""No"": false, ""Empty??"": null, ""Whatever"": ""/* Not really a comment. */"" } // I like putting comments where inconvenient
} // Our line ends with */";
    // Validations available are anything inside S.
    // Flag syntax is not yet parsed.
    public const string BxsjCommentDataWithValidations = $@"{{
    ""{DepthMaxxKey}"": [[[[[[[/* assert=depth=8 */]]]]]]],
    ""{ObjectTwisterKey}"": {{ ""[0]"": [{{}}, 8, [], {{}}, [], [], {{}}, [], {{ ""[0][8][0]"": [{{}}, [], [], []/* assert=top.count=4 */] }}], ""[1]"": 41 }},
    ""{SciNumsKey}"": [-24, 24, -24.24, 24.24, -1e+206, 1e+206, -1e-206, 1e+206]
}}";

    public static IEnumerable<string[]> JsonDatas => [
        [JsonData1], [JsonData2], [JsonDataFireSpam], [JsonDataDiscard]
    ];
    public static IEnumerable<string[]> BxsjDatas => [
        [BxsjCommentData], [BxsjCommentDataWithValidations]
    ];
    public static IEnumerable<string[]> BxsjValidatedDatas => [
        [BxsjCommentDataWithValidations]
    ];

    // Invalid
    public const string JsonDataInvalid = @"{
        ""num"": not,
        ""foo"": {""bar"", ""baz""],
        ""id]"":,[123]
// Are comments valid? No
}";
    public const string JsonDataInvalidStacking = @"{
    ""key"": { ""another key"": [{]}, ]
}";
    public const string JsonDataDiscardInvalid = $@"{{
    ""userName"": ""gamer"",
    ""password"": ""password"",
    ""{JsonDataDiscardKey}"": {{
        ""cardNumber"": 1234123412341234,
        ""hondaCivic"": 676,
        ""transactions"": [1.4, {{ 67984gjkdsngjkfhn ewrg,,jkhgku.jshf.h ""addawdws"" ,,:: // }}]
    }},
    ""stats"": {{
        ""score"": 99999999999,
        ""moneysWasted"": ""9000.24 USD"",
        ""lifetime"": 12000,
        ""skill"": 21498124
    }},
    ""badAtThisGame"": true
}}";

    public const string BxsjCommentDataInvalid = @"/* Let's start our invalid JSON journey! */
// Also some more lines.
{
        // I like annotating my JSON, but it gets deleted when it's serialized :(
        /* wen eta fix this?
} // Our line ends with */";

    public static IEnumerable<string[]> InvalidJsonDatas => [
        [JsonDataInvalid], [JsonDataInvalidStacking], [JsonDataDiscardInvalid]
    ];
    public static IEnumerable<string[]> InvalidBxsjDatas => [
        [BxsjCommentDataInvalid]
    ];

    /// <summary>
    /// Provides basic strings to use with <see cref="DynamicDataAttribute"/>.
    /// <br>Contains <see cref="string.Empty"/>.</br>
    /// </summary>
    public static IEnumerable<object[]> StringDatas => [
        [DataEmojiSpam], [DataEmojis], [FireSpamKey], [JsonData1], [DepthMaxxKey],
        [ObjectTwisterKey], [SciNumsKey], [JsonDataEmptyArray], [JsonDataBoolFalse],
        [JsonDataBoolTrue], [JsonDataNumber], [JsonDataNull], [DataEmpty]
    ];
    public static string? StringDatasDisplayName(MethodInfo _, object[] arg)
    {
        // Emoji spam crashes visual studio
        if (arg.Length <= 0) return "";
        if (arg[0] is string s)
        {
            return s.Length > 256 ? $"{s[..255]}…" : s;
        }
        return string.Join(", ", arg);
    }

    // - File Data
    // * "valid.json" is from some random website.
    // * JSON data sets are from https://microsoftedge.github.io/Demos/json-dummy-data/
    // * These must be loaded from assembly now
    public const string JsonFileValidName = "valid.json",
        JsonFileLargeName = "5mb.json",
        JsonFileLargeMinName = "5mb.min.json",
        JsonFileInvalidUnterminated = "unterminated.json",
        JsonFileInvalidNoColon = "missing_colon.json",
        JsonFileInvalidBinary = "binary.json";
    public static Stream LoadAsmFile(string jsonFile)
    {
        var asm = typeof(TestData).Assembly;
        var stream = asm.GetManifestResourceStream(jsonFile);
        Assert.IsNotNull(stream, $"Failed to load embedded JSON file stream from assembly with name '{jsonFile}'");

        return stream;
    }
}
