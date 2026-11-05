namespace BX.SJ.Tests;

/// <summary>
/// Contains JSON data to test against.
/// </summary>
public static class TestData
{
    // JSON Data
    // - Root Data
    public const string JsonDataEmptyObject = @"{}", JsonDataEmptyArray = @"[]", JsonDataRootString = @"""Hello world!""",
        JsonDataRootNumber = @"42.42", JsonDataRootBoolFalse = @"false", JsonDataRootBoolTrue = @"true", JsonDataRootNull = @"null", DataEmpty = "";

    public const string JsonData1 = @"{
        ""foo"": [""bar"", ""baz""],
        ""idk"": 42,
        ""mango"": { ""6"": 7 }
}";
    public const string JsonDataInvalid = @"{
        ""foo"": ""bar"", ""baz""],
        ""id],123
// Are comments valid? No
";
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
    // Fire data
    // Creates 1MB of UTF-8 string. Quite wholesome.
    public static readonly string DataEmojiSpam = string.Concat(Enumerable.Repeat("🅱️🅱️🅱️🍄‍🍕🍕🍕🍄‍🍄‍🍕🍄‍🍕🍕🍕🍕🍄‍🍕🍄‍", 10240));
    public const string JsonDataSpamKey = "YoThisDataIsFire";
    public static readonly string JsonDataSpamValue = string.Concat(Enumerable.Repeat("🔥", 32768));
    public static readonly string JsonData3 = $@"{{
    ""{JsonDataSpamKey}"": ""{JsonDataSpamValue}""
}}";
    public const string JsonDataDiscardKey = "secretInfo";
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
    // Discarded values are validated and parsed actually, they are just "skipped until the next adjacent value"
    public const string JsonDataDiscardInvalid = $@"{{
    ""userName"": ""gamer"",
    ""password"": ""password"",
    ""{JsonDataDiscardKey}"": {{
        ""cardNumber"": 1234123412341234,
        ""hondaCivic"": 676,
        ""transactions"": [{{ 67984gjkdsngjkfhn ewrg,,jkhgku.jshf.h ""addawdws"" ,,:: // }}]
    }},
    ""stats"": {{
        ""score"": 99999999999,
        ""moneysWasted"": ""9000.24 USD"",
        ""lifetime"": 12000,
        ""skill"": 21498124
    }},
    ""badAtThisGame"": true
}}";
    // ↓ the sj.h does not complain what was there before, "if it starts/ends a recursive object it was valid". this one will complain though.
    public const string JsonDataStackingInvalid = @"{
    ""key"": { ""another key"": [{]}, ]
}";
    // This comment thing got out of hand because I wanted to read it's data somehow :(
    public const string JsonDataComment = @"/* Let's start our ""invalid"" JSON journey! */
// Also some more lines.
{
        // I like annotating my JSON, but it gets deleted when it's serialized :(
        /* wen eta fix this? */
        ""Array"": [1,2,3,4,5,6,7,8, /* oops convenient comment */ 9,10,11],
        ""Objects"": /* OH NO */ { ""Yes"": true, ""No"": false, ""Empty??"": null, ""Whatever"": ""/* Not really a comment. */"" } // I like putting comments where inconvenient
} // Our line ends with */";
    // TODO : This one tests everything that could be tricky, but is valid
    // This will need it's own basic payload testing, but for now it will be just tested through the usual codepath.
    public const string DepthMaxxKey = "depthMaxx";
    public const string ObjectTwisterKey = "objectTwister";
    public const string SciNumsKey = "sciNum";
    public const string ValidatedJscData = $@"{{
    ""{DepthMaxxKey}"": [[[[[[[/* assert=depth=8 */]]]]]]],
    ""{ObjectTwisterKey}"": {{ ""[0]"": [{{}}, 8, [], {{}}, [], [], {{}}, [], {{ ""[0][8][0]"": [{{}}, [], [], []/* assert=count=4 */] }}], ""[1]"": 41 }},
    ""{SciNumsKey}"": [-24, 24, -24.24, 24.24, -1e+206, 1e+206, -1e-206, 1e+206]
}}";
    public const string JsonDataCommentInvalid = @"/* Let's start our invalid JSON journey! */
// Also some more lines.
{
        // I like annotating my JSON, but it gets deleted when it's serialized :(
        /* wen eta fix this?
} // Our line ends with */";

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

    // - Escape Data
    public const string EscapeContent = "Quote: \", Backslash: \\, Tab: \t, Newline: \n, Pizza: \uD83C\uDF55, The 🅱 variant: \uD83C\uDD71\uFE0F";
    public const string EscapeInvalidUnescape = "Oh wow, I'm escaping things I shouldn't! : \\a\\b\\c\\d \\e\\f\\g\\h\\i\\j\\k \\l\\m\\n\\o\\p\\q\\r\\s\\ t\\u\\v\\w\\x\\y\\z\\A\\B\\C \\D\\E\\F\\G\\H\\I\\J\\K\\L\\M\\N\\O\\P\\Q\\ R\\S\\T\\U\\V \\W\\X \\Y\\Z'";
    public const string EscapeInvalidSurrogate = "\uDF55\uD83C\uDD71\uD83C\uFE0F In C++, surrogate escape you!";
    public static readonly string EscapeContentAsciiSequence = string.Join("", Enumerable.Range(0, 255).Select(Convert.ToChar));
    public static readonly string EscapeContentEmojiTest = string.Join('\n', Enumerable.Range(0x2700, 0xBF).Select((v, i) => string.Concat(char.ToString((char)v), ((i & 1) == 0) ? "ABCDEFGHIJKLMNOPQRSTUVWXYZ" : "abcdefghijklmnopqrstuvwxyz")));
}
