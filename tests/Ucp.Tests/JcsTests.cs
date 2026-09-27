using System.Text;
using System.Text.Json;
using ElGuerre.Tendero.Ucp.Signing;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// RFC 8785's own vectors: the §3.2.2 example, the §3.2.3 sort, and Appendix B's
/// numbers — IEEE 754 bit patterns and the text ECMAScript writes for each. A
/// canonicaliser that passes its own examples but not these is one that signs
/// bytes nobody else rebuilds.
/// </summary>
public sealed class JcsTests
{
    private static string Canonical(string json) => Encoding.UTF8.GetString(Jcs.Canonicalize(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void The_rfc_example_canonicalises_to_the_rfc_bytes()
    {
        const string input = """
            {
              "numbers": [333333333.33333329, 1E30, 4.50, 2e-3, 0.000000000000000000000000001],
              "string": "\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"\/",
              "literals": [null, true, false]
            }
            """;

        Assert.Equal(
            """{"literals":[null,true,false],"numbers":[333333333.3333333,1e+30,4.5,0.002,1e-27],"string":"€$\u000f\nA'B\"\\\\\"/"}""",
            Canonical(input));
    }

    /// <summary>§3.2.3: by UTF-16 code units — so an emoji's surrogate pair sorts before U+FB33, which sorting by code point would not do.</summary>
    [Fact]
    public void Members_sort_by_utf16_code_units()
    {
        const string input = """
            {
              "\u20ac": "Euro Sign",
              "\r": "Carriage Return",
              "\ufb33": "Hebrew Letter Dalet With Dagesh",
              "1": "One",
              "\ud83d\ude00": "Emoji: Grinning Face",
              "\u0080": "Control",
              "\u00f6": "Latin Small Letter O With Diaeresis"
            }
            """;

        var keys = JsonDocument.Parse(Canonical(input)).RootElement.EnumerateObject().Select(member => member.Name);

        Assert.Equal(["\r", "1", "\u0080", "\u00f6", "\u20ac", "\ud83d\ude00", "\ufb33"], keys);
    }

    [Theory]
    [InlineData("0000000000000000", "0")]
    [InlineData("8000000000000000", "0")]
    [InlineData("0000000000000001", "5e-324")]
    [InlineData("8000000000000001", "-5e-324")]
    [InlineData("7fefffffffffffff", "1.7976931348623157e+308")]
    [InlineData("4340000000000000", "9007199254740992")]
    [InlineData("c340000000000000", "-9007199254740992")]
    [InlineData("4430000000000000", "295147905179352830000")]
    [InlineData("44b52d02c7e14af5", "9.999999999999997e+22")]
    [InlineData("44b52d02c7e14af6", "1e+23")]
    [InlineData("44b52d02c7e14af7", "1.0000000000000001e+23")]
    [InlineData("444b1ae4d6e2ef4e", "999999999999999700000")]
    [InlineData("444b1ae4d6e2ef4f", "999999999999999900000")]
    [InlineData("444b1ae4d6e2ef50", "1e+21")]
    [InlineData("3eb0c6f7a0b5ed8c", "9.999999999999997e-7")]
    [InlineData("3eb0c6f7a0b5ed8d", "0.000001")]
    [InlineData("41b3de4355555553", "333333333.3333332")]
    [InlineData("41b3de4355555554", "333333333.33333325")]
    [InlineData("41b3de4355555555", "333333333.3333333")]
    [InlineData("41b3de4355555556", "333333333.3333334")]
    [InlineData("41b3de4355555557", "333333333.33333343")]
    [InlineData("becbf647612f3696", "-0.0000033333333333333333")]
    [InlineData("43143ff3c1cb0959", "1424953923781206.2")]
    public void Numbers_are_written_the_way_ecmascript_writes_them(string bits, string expected) =>
        Assert.Equal(expected, Jcs.Number(BitConverter.Int64BitsToDouble(Convert.ToInt64(bits, 16))));

    [Fact]
    public void Whitespace_and_member_order_do_not_change_the_bytes()
    {
        Assert.Equal(
            Canonical("""{"b": [1, 2.0, {"y": 1, "x": "é"}], "a": "A"}"""),
            Canonical("""{"a":"A","b":[1,2,{"x":"é","y":1}]}"""));
    }
}
