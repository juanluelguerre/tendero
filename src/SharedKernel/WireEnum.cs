namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A closed set read from a query string (ADR 0043) — the other half of
/// <see cref="WireJson.Enums"/>, which reads the same sets from a body.
///
/// The framework binds an enum parameter by itself, and three of the things it
/// does were the wrong answer, found by a test before anybody relied on them:
/// it is case-sensitive (<c>?sort=priceasc</c> fails), it takes a number
/// (<c>?sort=1</c> is the second member, so the member's POSITION becomes the
/// contract), and it takes a number the enum does not name. This reads the
/// member's name, in any case, and nothing else.
///
/// An endpoint takes <c>WireEnum&lt;ProductSort&gt;?</c>; the document declares
/// it as the enum's names (<c>ClosedSetsInTheQueryTransformer</c> in the API),
/// so a client generates the union from the contract and never learns the set
/// from a 400.
/// </summary>
public readonly record struct WireEnum<TEnum>(TEnum Value)
    where TEnum : struct, Enum
{
    public static bool TryParse(string? text, out WireEnum<TEnum> result)
    {
        result = default;

        // Enum.TryParse is more generous than a contract: it reads "1" as a
        // member by position, trims "Week ", and ORs "Day, Week" into Week
        // (0 | 1). Asking whether the value is defined lets the last two
        // through, because the answer is a real member. What is asked instead
        // is whether the text IS that member's name, which refuses a number, a
        // padded name, a combination and an undefined value in one comparison.
        if (string.IsNullOrEmpty(text)
            || !Enum.TryParse<TEnum>(text, ignoreCase: true, out var value)
            || !string.Equals(Enum.GetName(value), text, StringComparison.OrdinalIgnoreCase))
            return false;

        result = new WireEnum<TEnum>(value);
        return true;
    }

    public static implicit operator TEnum(WireEnum<TEnum> wire) => wire.Value;
}
