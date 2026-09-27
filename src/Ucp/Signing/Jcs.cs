using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ElGuerre.Tendero.Ucp.Signing;

/// <summary>
/// The JSON Canonicalization Scheme, RFC 8785: the one byte sequence a JSON value
/// has, whoever serialised it.
///
/// **Why a mandate needs it and a request does not.** UCP signs requests over
/// their raw bytes, verified at once on the same connection. A mandate is kept as
/// evidence and crosses systems that re-serialise JSON — platform, shop, payment
/// provider — so every party has to rebuild the signed bytes from the logical
/// value. JCS is how: members sorted by their UTF-16 code units, no whitespace,
/// strings escaped minimally, numbers written the way ECMAScript writes them.
///
/// Written here rather than taken from a package because it is sixty lines, the
/// RFC's own vectors test it, and the dependency budget is deliberately small.
/// </summary>
public static class Jcs
{
    public static byte[] Canonicalize(JsonElement value)
    {
        var builder = new StringBuilder();
        Write(value, builder);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void Write(JsonElement value, StringBuilder builder)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                // Ordinal comparison of .NET strings IS comparison by UTF-16 code
                // units, which is what the RFC asks for (§3.2.3) — not by code
                // point, and not by culture.
                foreach (var member in value.EnumerateObject().OrderBy(member => member.Name, StringComparer.Ordinal))
                {
                    if (!first)
                        builder.Append(',');
                    first = false;
                    WriteString(member.Name, builder);
                    builder.Append(':');
                    Write(member.Value, builder);
                }

                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (index++ > 0)
                        builder.Append(',');
                    Write(item, builder);
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                WriteString(value.GetString()!, builder);
                break;

            case JsonValueKind.Number:
                builder.Append(Number(value.TryGetDouble(out var number) && double.IsFinite(number)
                    ? number
                    : throw new FormatException($"{value.GetRawText()} is not an IEEE 754 double, and JCS has no way to write it.")));
                break;

            case JsonValueKind.True:
                builder.Append("true");
                break;

            case JsonValueKind.False:
                builder.Append("false");
                break;

            case JsonValueKind.Null:
                builder.Append("null");
                break;

            default:
                throw new FormatException($"A {value.ValueKind} is not a JSON value.");
        }
    }

    /// <summary>§3.2.2.2: the six short escapes, <c>\u00xx</c> in lower case for the other controls, everything else as it is.</summary>
    private static void WriteString(string text, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case < ' ': builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: builder.Append(character); break;
            }
        }

        builder.Append('"');
    }

    /// <summary>
    /// ECMAScript's Number::toString (ECMA-262 §6.1.6.1.20), which §3.2.2.3 adopts:
    /// the shortest digits that round-trip — what .NET's default formatting already
    /// finds — laid out by ECMAScript's rules for where the point and the exponent go.
    /// </summary>
    internal static string Number(double value)
    {
        if (value == 0)
            return "0"; // -0 too.

        var sign = value < 0 ? "-" : "";
        var shortest = Math.Abs(value).ToString(CultureInfo.InvariantCulture); // "123.45", "1E+21", "5E-324"
        var (mantissa, exponent) = shortest.IndexOf('E') is var e and >= 0
            ? (shortest[..e], int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture))
            : (shortest, 0);

        // As ECMAScript states it: value = 0.digits × 10^n, digits with no zero at either end.
        var point = mantissa.IndexOf('.') is var p and >= 0 ? p : mantissa.Length;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits.Trim('0');
        var k = digits.Length;
        var n = point - leading + exponent;

        if (k <= n && n <= 21)
            return sign + digits + new string('0', n - k);
        if (0 < n && n <= 21)
            return sign + digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0)
            return sign + "0." + new string('0', -n) + digits;

        var power = n - 1;
        return sign + digits[..1] + (k > 1 ? "." + digits[1..] : "") + "e" + (power > 0 ? "+" : "-") + Math.Abs(power).ToString(CultureInfo.InvariantCulture);
    }
}
