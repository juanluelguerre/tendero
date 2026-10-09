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

    private readonly Dictionary<string, string> englishBySpanish;

    /// <summary>Pairs of English labels the catalogue says may be taken for each other, each written both ways.</summary>
    private readonly HashSet<(string, string)> mistaken;

    private readonly Dictionary<string, string> swatchBySpanish;

    private ColourLexicon(Dictionary<string, string> englishBySpanish, HashSet<(string, string)> mistaken, Dictionary<string, string> swatchBySpanish) =>
        (this.englishBySpanish, this.mistaken, this.swatchBySpanish) = (englishBySpanish, mistaken, swatchBySpanish);

    public static ColourLexicon Read(string path)
    {
        var definitions = JsonSerializer.Deserialize<Definition[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"'{path}' is empty or not an attribute definition array.");

        var colour = definitions.FirstOrDefault(definition =>
                         String.Equals(definition.Code, ColourCode, StringComparison.OrdinalIgnoreCase))
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

        // What may be taken for what is declared on one option and true of both
        // (`mistakenFor`, by code): stainless steel names grey, and grey is then
        // stainless steel's too. The shop reads the same field — one declaration.
        var englishByCode = (colour.Options ?? [])
            .Where(option => option.Label?.ContainsKey("en") == true)
            .ToDictionary(option => option.Code, option => option.Label!["en"], StringComparer.OrdinalIgnoreCase);
        var mistaken = new HashSet<(string, string)>();
        foreach (var option in colour.Options ?? [])
        {
            foreach (var other in option.MistakenFor ?? [])
            {
                if (englishByCode.TryGetValue(option.Code, out var one) && englishByCode.TryGetValue(other, out var another))
                {
                    mistaken.Add((one.ToLowerInvariant(), another.ToLowerInvariant()));
                    mistaken.Add((another.ToLowerInvariant(), one.ToLowerInvariant()));
                }
            }
        }

        var swatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in colour.Options ?? [])
        {
            if (option.Label?.GetValueOrDefault("es") is { } spanish && option.Swatch is { } swatch)
                swatches[spanish] = swatch;
        }

        return new ColourLexicon(pairs, mistaken, swatches);
    }

    /// <summary>Whether the catalogue says these two colours, by their English labels, are one to the eye.</summary>
    public bool MayBeTakenFor(string english, string another) =>
        this.mistaken.Contains((english.ToLowerInvariant(), another.ToLowerInvariant()));

    /// <summary>The English label, or null when the catalogue says a colour this
    /// file has never heard of — which is a seed that drifted, not a prompt to
    /// improvise around.</summary>
    public string? English(string spanishLabel) => this.englishBySpanish.GetValueOrDefault(spanishLabel);

    /// <summary>
    /// Every English colour the catalogue knows, which is the closed set a vision
    /// model is asked to choose from. Generated from the definitions rather than
    /// listed, so the question cannot drift from the catalogue it is checking.
    /// </summary>
    public IReadOnlyList<string> EnglishLabels => [.. this.englishBySpanish.Values.Distinct().Order()];

    /// <summary>
    /// Every colour as the catalogue paints it (<c>#C0643F</c>), in the order it
    /// declares them: what the defects board draws a product in when it draws
    /// it in the wrong one.
    /// </summary>
    public IReadOnlyList<string> Swatches => [.. this.swatchBySpanish.Values];

    /// <summary>The swatch of the colour a product declares, or null for one the catalogue does not paint.</summary>
    public string? Swatch(string spanishLabel) => this.swatchBySpanish.GetValueOrDefault(spanishLabel);

    private sealed record Definition(string Code, Option[]? Options);

    private sealed record Option(string Code, Dictionary<string, string>? Label, string[]? MistakenFor = null, string? Swatch = null);
}
