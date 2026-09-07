using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Spanish colour labels to English ones, read from the attribute definitions.
///
/// The catalogue stores the **Spanish label** in each product
/// (<c>"color": "azul marino"</c>) while a prompt has to say "navy blue", and the
/// pairing already exists: <c>attributes.sample.json</c> declares every option
/// once, with a label per culture. Hardcoding nine pairs here would be a tenth
/// place to keep in step with the catalogue, and the day somebody adds a colour
/// it would be the one place nobody edits.
/// </summary>
public sealed class ColourLexicon
{
    private const string ColourCode = "COLOR";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _englishBySpanish;

    private ColourLexicon(Dictionary<string, string> englishBySpanish) =>
        _englishBySpanish = englishBySpanish;

    public static ColourLexicon Read(string path)
    {
        var definitions = JsonSerializer.Deserialize<Definition[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"'{path}' is empty or not an attribute definition array.");

        var colour = definitions.FirstOrDefault(definition =>
                string.Equals(definition.Code, ColourCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"'{path}' declares no {ColourCode} attribute.");

        // Ordinal and not culture-aware: the repository builds with
        // InvariantGlobalization, where a culture-sensitive comparison is a
        // promise the runtime does not keep.
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in colour.Options ?? [])
        {
            var spanish = option.Label?.GetValueOrDefault("es");
            var english = option.Label?.GetValueOrDefault("en");

            if (spanish is not null && english is not null)
                pairs[spanish] = english;
        }

        return new ColourLexicon(pairs);
    }

    /// <summary>The English label, or null when the catalogue says a colour this
    /// file has never heard of — which is a seed that drifted, not a prompt to
    /// improvise around.</summary>
    public string? English(string spanishLabel) => _englishBySpanish.GetValueOrDefault(spanishLabel);

    private sealed record Definition(string Code, Option[]? Options);

    private sealed record Option(string Code, Dictionary<string, string>? Label);
}
