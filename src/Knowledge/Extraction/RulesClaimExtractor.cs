using System.Globalization;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.Knowledge.Extraction;

/// <summary>
/// The non-AI path (invariant 8): numbers with units, a material dictionary and
/// the word <em>inducción</em>, over the Spanish description. Exact about the
/// little it sees — P10-0 measured precision 1.00 and 9 % of the information
/// no imported attribute carries — and it is what remains when the model is down.
/// </summary>
public sealed partial class RulesClaimExtractor : IClaimExtractor
{
    private static readonly (string Word, string Code)[] Materials =
    [
        ("hierro fundido", "CAST_IRON"), ("acero al carbono", "CARBON_STEEL"), ("acero inoxidable", "STAINLESS_STEEL"),
        ("aluminio", "ALUMINIUM"), ("lino", "LINEN"), ("cerámica", "CERAMIC"), ("bambú", "BAMBOO"),
        ("algodón", "COTTON"), ("merino", "MERINO_WOOL"), ("cristal", "GLASS"),
    ];

    private static readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal)
    {
        ["Dos"] = 2,
        ["Tres"] = 3,
        ["Cuatro"] = 4,
        ["Cinco"] = 5,
        ["Seis"] = 6,
    };

    public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(
        ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var es = text.Spanish;
        var claims = new List<ProposedClaim>();

        void Add(Match match, string? attribute = null, string? value = null) =>
            claims.Add(new ProposedClaim(match.Value, "", "", "", attribute, value, null));

        foreach (Match match in Drop().Matches(es))
            Add(match, "DROP_MM", match.Groups[1].Value);
        foreach (Match match in Litres().Matches(es))
            Add(match, "CAPACITY_L", Number(match.Groups[1].Value));
        foreach (Match match in Millilitres().Matches(es))
            Add(match, "CAPACITY_L", (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 1000m).ToString(CultureInfo.InvariantCulture));
        foreach (Match match in Watts().Matches(es))
            Add(match, "POWER_W", match.Groups[1].Value);
        foreach (Match match in Cups().Matches(es))
            Add(match, "CUPS", match.Groups[1].Value);
        foreach (Match match in Diameters().Matches(es))
            Add(match, "DIAMETERS_CM", match.Groups[1].Value.Replace(" y ", ", ", StringComparison.Ordinal));
        foreach (Match match in Dimensions().Matches(es))
            Add(match);
        foreach (Match match in Weight().Matches(es))
            Add(match);
        foreach (Match match in Induction().Matches(es))
            Add(match, "INDUCTION", "true");
        foreach (var (word, code) in Materials)
            foreach (Match match in Regex.Matches(es, Regex.Escape(word)))
                Add(match, "MATERIAL", code);
        if (Pieces().Match(es) is { Success: true } pieces)
            Add(pieces, "PIECES", Counts[pieces.Groups[1].Value].ToString(CultureInfo.InvariantCulture));

        return Task.FromResult<IReadOnlyList<ProposedClaim>>(claims);
    }

    private static string Number(string spanish) =>
        decimal.Parse(spanish.Replace(',', '.'), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"[Dd]rop de (\d+) mm")]
    private static partial Regex Drop();

    [GeneratedRegex(@"(\d+(?:,\d+)?) litros")]
    private static partial Regex Litres();

    [GeneratedRegex(@"(\d+) ml\b")]
    private static partial Regex Millilitres();

    [GeneratedRegex(@"(\d+) W\b")]
    private static partial Regex Watts();

    [GeneratedRegex(@"(\d+) tazas")]
    private static partial Regex Cups();

    [GeneratedRegex(@"de ((?:\d+, )*\d+ y \d+) cm")]
    private static partial Regex Diameters();

    [GeneratedRegex(@"\d+ x \d+ cm")]
    private static partial Regex Dimensions();

    [GeneratedRegex(@"\d+(?:,\d+)? k?g\b")]
    private static partial Regex Weight();

    [GeneratedRegex(@"[^,.]{0,20}inducción")]
    private static partial Regex Induction();

    [GeneratedRegex(@"^(Dos|Tres|Cuatro|Cinco|Seis) \w+")]
    private static partial Regex Pieces();
}
