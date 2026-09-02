using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// What one particular product says about an attribute.
///
/// It replaces <c>Dictionary&lt;string,string&gt;</c>, which told a number from
/// a text apart in no way at all and did not know that "azul marino" has a
/// translation. It carries its <see cref="Kind"/> along so the index can render
/// it without asking the definition field by field.
/// </summary>
public sealed record AttributeValue(
    string Code,
    AttributeKind Kind,
    string? OptionCode = null,
    LocalizedText? Text = null,
    string? RawText = null,
    decimal? Number = null,
    bool? Flag = null)
{
    public static AttributeValue Option(string code, string optionCode) =>
        new(AttributeDefinition.Normalise(code), AttributeKind.Option,
            OptionCode: AttributeDefinition.Normalise(optionCode));

    public static AttributeValue Localized(string code, LocalizedText text) =>
        new(AttributeDefinition.Normalise(code), AttributeKind.LocalizedText, Text: text);

    public static AttributeValue Plain(string code, string text) =>
        new(AttributeDefinition.Normalise(code), AttributeKind.Text, RawText: text);

    public static AttributeValue Numeric(string code, decimal number) =>
        new(AttributeDefinition.Normalise(code), AttributeKind.Number, Number: number);

    public static AttributeValue Boolean(string code, bool flag) =>
        new(AttributeDefinition.Normalise(code), AttributeKind.Boolean, Flag: flag);

    /// <summary>
    /// Whether it contributes anything to the searchable text. A boolean that is
    /// false does not: indexing "induction" on a pan that is NOT induction is
    /// worse than not indexing it, because it makes it turn up in exactly the
    /// wrong search.
    /// </summary>
    public bool IsWorthIndexing => Kind != AttributeKind.Boolean || Flag == true;

    /// <summary>
    /// The value as it reads in one culture. An option needs its definition to
    /// be translated; everything else is self-sufficient.
    /// </summary>
    public string RenderIn(string culture, AttributeDefinition? definition) => Kind switch
    {
        AttributeKind.Option => definition?.LabelForOption(OptionCode!, culture) ?? OptionCode!,
        AttributeKind.LocalizedText => Text?.In(culture) ?? string.Empty,
        AttributeKind.Number => definition?.Unit is { Length: > 0 } unit
            ? $"{Number} {unit}"
            : Number?.ToString() ?? string.Empty,
        // A boolean contributes no searchable value: what somebody types is the
        // attribute's NAME ("induction"), not the word "yes". Whether it is
        // emitted at all is IsWorthIndexing's call, not this one.
        AttributeKind.Boolean => string.Empty,
        _ => RawText ?? string.Empty
    };
}
