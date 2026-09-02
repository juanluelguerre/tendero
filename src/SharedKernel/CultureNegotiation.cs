namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The ADR 0013 chain in one place: explicit parameter -> Accept-Language ->
/// <c>es</c>.
///
/// It lived copied in <c>/api/catalog/products</c> and <c>/api/search</c>, under
/// a comment that said exactly when that would stop being fine: *"they are two
/// cases, and extracting an abstraction from two cases is guessing. With a third
/// it moves to a shared binder."* Price quoting is the third.
///
/// **It takes the raw header rather than an <c>HttpRequest</c>**, which is what
/// makes it useful twice: MCP has no <c>Accept-Language</c>, and in phase 9 the
/// chain becomes "tool parameter -> session default -> es". A signature tied to
/// ASP.NET would have to be written again there — and this way the shared kernel
/// never learns that HTTP exists.
/// </summary>
public static class CultureNegotiation
{
    /// <summary>The cultures the shop speaks. Two, and the list is closed: a
    /// language with no translations is not a supported culture.</summary>
    public static readonly string[] Supported = ["es", "en"];

    public const string Default = "es";

    /// <summary>
    /// The parameter wins because a URL carrying a language is shareable,
    /// cacheable, and the only route available to an agent, which has no browser
    /// locale. The header decides when there is no parameter, which is what
    /// makes somebody who arrives without asking see their language and not
    /// ours.
    /// </summary>
    public static string Resolve(string? requested, string? acceptLanguage = null)
    {
        if (Supports(requested))
            return Culture.Normalize(requested!);

        return FromAcceptLanguage(acceptLanguage) ?? Default;
    }

    public static bool Supports(string? culture) =>
        culture is not null
        && Supported.Contains(Culture.Normalize(culture), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Accept-Language: en-GB;q=0.9, es;q=0.8</c> -> <c>en</c>. Ordered by
    /// quality, first one the shop speaks wins; with no <c>q</c>, the
    /// specification says it counts as 1.
    /// </summary>
    private static string? FromAcceptLanguage(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? null
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Parse)
                .OrderByDescending(entry => entry.Quality)
                .Select(entry => entry.Culture)
                .FirstOrDefault(Supports);

    private static (string Culture, double Quality) Parse(string entry)
    {
        var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var culture = Culture.Normalize(parts[0]);

        var quality = parts
            .Skip(1)
            .Where(part => part.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            .Select(part => double.TryParse(part[2..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 1d)
            .FirstOrDefault(1d);

        return (culture, quality);
    }
}
