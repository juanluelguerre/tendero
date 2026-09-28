using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// What kind of data an attribute is. It decides how the value is stored, how it
/// is validated and how it is rendered into the index.
/// </summary>
public enum AttributeKind
{
    /// <summary>Free text in a single language (a manufacturer code).</summary>
    Text,

    /// <summary>User-facing free text, and therefore translatable.</summary>
    LocalizedText,

    Number,
    Boolean,

    /// <summary>One of a closed set of options, each labelled per culture.</summary>
    Option
}

/// <summary>
/// One possible option. The CODE is stable and the label is user-facing text,
/// hence <see cref="LocalizedText"/> (invariant 6).
///
/// This separation is what fixes the English queries scoring 0.000: today the
/// catalogue stores "azul marino" and nothing knows that in English that is
/// called "navy blue".
/// </summary>
/// <param name="Swatch">What the option looks like, for a filter drawn as swatches:
/// a colour as `#RRGGBB`. Data about the product, not a style — the storefront
/// paints it as the option's own colour, beside its name (R-7.3). Null for an
/// option with no colour to show.</param>
public sealed record AttributeOption(string Code, LocalizedText Label, string? Swatch = null);

public sealed record AttributeDefinitionChanged(string Code, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// What an attribute means, once and for the whole catalogue.
///
/// Attributes used to be a <c>Dictionary&lt;string,string&gt;</c>: no type, no
/// unit, no label and no translation. <c>SetAttribute("colour", "banana")</c>
/// was accepted, and "azul marino" was literally the data — so the English index
/// contained Spanish and no English query could match it.
///
/// An aggregate of its own rather than a lookup table inside Product, because it
/// has its own lifecycle: the shopkeeper defines "COLOR" once and a thousand
/// products use it.
/// </summary>
public sealed class AttributeDefinition : AggregateRoot
{
    private readonly List<AttributeOption> options = [];
    private readonly List<string> aliases = [];

    /// <summary>Stable and uppercase: it is the key a product refers to this by,
    /// and changing it would rewrite the whole catalogue.</summary>
    public string Code { get; private set; } = default!;

    public LocalizedText Label { get; private set; } = default!;
    public AttributeKind Kind { get; private set; }

    /// <summary>"mm", "W", "L". Kept apart from the value so that the number
    /// stays a number.</summary>
    public string? Unit { get; private set; }

    /// <summary>Whether it can tell variants apart (COLOR yes, WATTAGE no).</summary>
    public bool IsVariantAxis { get; private set; }

    /// <summary>Whether it shows up as a search facet.</summary>
    public bool IsFacet { get; private set; }

    /// <summary>Whether its text enters the index. A manufacturer code should
    /// not: it adds noise and nobody types it.</summary>
    public bool IsSearchable { get; private set; }

    /// <summary>
    /// Proposed by the importer and waiting for a human to give it labels. Same
    /// idea as Draft on a product: importing does not publish, and here it does
    /// not define either.
    /// </summary>
    public bool IsDraft { get; private set; }

    public IReadOnlyList<AttributeOption> Options => this.options;

    /// <summary>
    /// What each source calls this: <c>genero</c>, <c>gender</c>,
    /// <c>g:gender</c>. Declared rather than guessed from the label, because
    /// guessing fails exactly where it hurts — "genero" without the accent does
    /// not match the label "género", and the failure is silent.
    /// </summary>
    public IReadOnlyList<string> Aliases => this.aliases;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AttributeDefinition() { } // EF Core

    public static AttributeDefinition Define(
        TimeProvider clock,
        string code,
        LocalizedText label,
        AttributeKind kind,
        string? unit = null,
        bool isVariantAxis = false,
        bool isFacet = false,
        bool isSearchable = true,
        bool isDraft = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var now = clock.GetUtcNow();
        var definition = new AttributeDefinition
        {
            Code = Normalise(code),
            Label = label,
            Kind = kind,
            Unit = unit,
            IsVariantAxis = isVariantAxis,
            IsFacet = isFacet,
            IsSearchable = isSearchable,
            IsDraft = isDraft,
            CreatedAt = now,
            UpdatedAt = now
        };

        definition.Raise(new AttributeDefinitionChanged(definition.Code, now));
        return definition;
    }

    /// <summary>Adds or replaces an option. Idempotent by code.</summary>
    public void AddOption(TimeProvider clock, string code, LocalizedText label, string? swatch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (swatch is not null && !IsSwatch(swatch))
            throw new ArgumentException($"A swatch is a colour as #RRGGBB, not '{swatch}'.", nameof(swatch));

        if (Kind != AttributeKind.Option)
            throw new InvalidOperationException($"'{Code}' is {Kind}, so it has no options.");

        var normalised = Normalise(code);
        this.options.RemoveAll(option => option.Code == normalised);
        this.options.Add(new AttributeOption(normalised, label, swatch?.ToUpperInvariant()));
        Touch(clock);
    }

    private static bool IsSwatch(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    public void AddAlias(TimeProvider clock, string alias)
    {
        var normalised = Normalise(alias);
        if (this.aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            return;

        this.aliases.Add(normalised);
        Touch(clock);
    }

    /// <summary>Whether this definition answers to the key a source sends.</summary>
    public bool AnswersTo(string key)
    {
        var normalised = Normalise(key);
        return string.Equals(Code, normalised, StringComparison.OrdinalIgnoreCase)
               || this.aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase);
    }

    public void Relabel(TimeProvider clock, LocalizedText label)
    {
        Label = label;
        Touch(clock);
    }

    /// <summary>Out of Draft: somebody has looked at its labels.</summary>
    public void Approve(TimeProvider clock)
    {
        IsDraft = false;
        Touch(clock);
    }

    /// <summary>
    /// Finds the option whose text matches what a source sends, IN ANY culture.
    /// A Spanish connector sends "azul marino" and a Google feed sends "navy
    /// blue"; both point at <c>NAVY_BLUE</c>, and the one that has to know that
    /// is the catalogue, not each connector.
    /// </summary>
    public AttributeOption? ResolveOption(string incoming)
    {
        var trimmed = incoming.Trim();

        return this.options.FirstOrDefault(option =>
                   string.Equals(option.Code, Normalise(trimmed), StringComparison.OrdinalIgnoreCase))
               ?? this.options.FirstOrDefault(option =>
                   option.Label.Values.Values.Any(label =>
                       string.Equals(label, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The value this definition accepts for a raw string, or null when it does
    /// not accept it: an option it does not have, a number that is not one, a
    /// yes/no that is neither. One rule for every writer — the import mapper
    /// falls back to plain text on a null, an approved claim (P10-6) is refused.
    ///
    /// A number may carry this definition's own unit ("20 L"), because that is how
    /// a description says it; any other trailing word makes it not a number. A
    /// localized text is never accepted from one raw string: a single string has
    /// one language, and the attribute promises two (invariant 6).
    /// </summary>
    public AttributeValue? Accept(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var trimmed = raw.Trim();

        return Kind switch
        {
            AttributeKind.Option => ResolveOption(trimmed) is { } option ? AttributeValue.Option(Code, option.Code) : null,
            AttributeKind.Number => ParseNumber(trimmed) is { } number ? AttributeValue.Numeric(Code, number) : null,
            AttributeKind.Boolean => trimmed.ToLowerInvariant() switch
            {
                "si" or "sí" or "yes" or "true" or "1" => AttributeValue.Boolean(Code, true),
                "no" or "false" or "0" => AttributeValue.Boolean(Code, false),
                _ => null,
            },
            AttributeKind.Text => trimmed.Length == 0 ? null : AttributeValue.Plain(Code, trimmed),
            _ => null,
        };
    }

    private decimal? ParseNumber(string text)
    {
        var number = text.TrimEnd('"').Trim();
        if (Unit is { Length: > 0 } unit && number.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            number = number[..^unit.Length].Trim();

        return decimal.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>How this reads in one culture: "azul marino" / "navy blue".</summary>
    public string LabelForOption(string optionCode, string culture) =>
        this.options.FirstOrDefault(option =>
                string.Equals(option.Code, optionCode, StringComparison.OrdinalIgnoreCase))
            ?.Label.In(culture) ?? optionCode;

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();
        Raise(new AttributeDefinitionChanged(Code, UpdatedAt));
    }

    /// <summary>
    /// Uppercase and underscores. A code with spaces or accents ends up escaped
    /// in a URL or in an index field name.
    /// </summary>
    public static string Normalise(string code) =>
        code.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant();
}
