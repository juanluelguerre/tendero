namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Where something is sent, or who is billed.
///
/// It lives in the SharedKernel and not in `Ordering` because three contexts
/// need the same shape and none of them owns it: an order is shipped to one,
/// `Accounts` will keep a customer's book of them (still on the backlog), and
/// a shipping rate is a function of one. Putting it in `Ordering` would make
/// `Accounts` reference `Ordering` to store an address, which is the wrong
/// direction and exactly the crossing ADR 0014 forbids.
///
/// It is a **value**, not an entity: two identical addresses are the same
/// address, and an order freezes the one it was shipped to (ADR 0002) rather
/// than pointing at a row somebody can edit afterwards. Editing your address
/// must never rewrite where last month's parcel went.
///
/// **`CountryCode` is ISO 3166-1 alpha-2 and uppercase**, because it is the key
/// three different things will look up: a shipping rate, a tax rule, and
/// eventually a market. `PostalCode` stays a string — it is alphanumeric in half
/// of Europe, and validating it per country is a table this shop does not need
/// at laboratory scale.
/// </summary>
public sealed record Address(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? Phone)
{
    /// <summary>
    /// Builds one, normalising the country code and refusing what cannot be
    /// posted. It is a factory rather than a constructor validation so the
    /// record still deserialises from jsonb without fighting EF.
    /// </summary>
    public static Address Create(
        string recipientName,
        string line1,
        string? line2,
        string city,
        string? region,
        string postalCode,
        string countryCode,
        string? phone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(line1);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);

        var country = NormaliseCountry(countryCode);

        return new Address(
            recipientName.Trim(),
            line1.Trim(),
            Blank(line2),
            city.Trim(),
            Blank(region),
            postalCode.Trim(),
            country,
            Blank(phone));
    }

    /// <summary>
    /// Two letters, uppercase. Anything else is rejected here rather than
    /// carried: a lowercase "es" and an "ESP" both look harmless and both make
    /// the shipping table miss.
    /// </summary>
    public static string NormaliseCountry(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var trimmed = countryCode.Trim();

        return trimmed.Length == 2 && trimmed.All(char.IsAsciiLetter)
            ? trimmed.ToUpperInvariant()
            : throw new ArgumentException(
                $"'{countryCode}' is not an ISO 3166-1 alpha-2 country code.", nameof(countryCode));
    }

    /// <summary>One line, for a confirmation screen or an email. Ordered the way
    /// a postal address is read, and it skips what is missing rather than
    /// leaving the commas behind.</summary>
    public string SingleLine() => string.Join(", ",
        new[] { Line1, Line2, PostalCode + " " + City, Region, CountryCode }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
