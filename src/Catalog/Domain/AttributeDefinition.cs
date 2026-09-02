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
public sealed record AttributeOption(string Code, LocalizedText Label);

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
    private readonly List<AttributeOption> _options = [];
    private readonly List<string> _aliases = [];

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

    public IReadOnlyList<AttributeOption> Options => _options;

    /// <summary>
    /// What each source calls this: <c>genero</c>, <c>gender</c>,
    /// <c>g:gender</c>. Declared rather than guessed from the label, because
    /// guessing fails exactly where it hurts — "genero" without the accent does
    /// not match the label "género", and the failure is silent.
    /// </summary>
    public IReadOnlyList<string> Aliases => _aliases;

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
    public void AddOption(TimeProvider clock, string code, LocalizedText label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (Kind != AttributeKind.Option)
            throw new InvalidOperationException($"'{Code}' is {Kind}, so it has no options.");

        var normalised = Normalise(code);
        _options.RemoveAll(option => option.Code == normalised);
        _options.Add(new AttributeOption(normalised, label));
        Touch(clock);
    }

    public void AddAlias(TimeProvider clock, string alias)
    {
        var normalised = Normalise(alias);
        if (_aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            return;

        _aliases.Add(normalised);
        Touch(clock);
    }

    /// <summary>Whether this definition answers to the key a source sends.</summary>
    public bool AnswersTo(string key)
    {
        var normalised = Normalise(key);
        return string.Equals(Code, normalised, StringComparison.OrdinalIgnoreCase)
            || _aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase);
    }

    public void Relabel(TimeProvider clock, LocalizedText label)
    {
        Label = label;
        Touch(clock);
    }

    /// <summary>Sacarla de Draft: alguien ha mirado sus etiquetas.</summary>
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

        return _options.FirstOrDefault(option =>
                   string.Equals(option.Code, Normalise(trimmed), StringComparison.OrdinalIgnoreCase))
            ?? _options.FirstOrDefault(option =>
                   option.Label.Values.Values.Any(label =>
                       string.Equals(label, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>How this reads in one culture: "azul marino" / "navy blue".</summary>
    public string LabelForOption(string optionCode, string culture) =>
        _options.FirstOrDefault(option =>
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
