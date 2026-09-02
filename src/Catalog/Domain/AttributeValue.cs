using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// Lo que un producto concreto dice de un atributo.
///
/// Sustituye a <c>Dictionary&lt;string,string&gt;</c>, que no distinguía un
/// número de un texto ni sabía que "azul marino" tiene traducción. Lleva el
/// <see cref="Kind"/> consigo para que el índice pueda renderizarlo sin volver a
/// preguntar por la definición campo a campo.
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
    /// El valor tal y como se lee en una cultura. Una opción necesita su
    /// definición para traducirse; el resto se basta.
    /// </summary>
    /// <summary>
    /// Si aporta algo al texto buscable. Un booleano en false no: indexar
    /// "inducción" en una sartén que NO es de inducción es peor que no
    /// indexarla, porque la hace aparecer justo en la búsqueda equivocada.
    /// </summary>
    public bool IsWorthIndexing => Kind != AttributeKind.Boolean || Flag == true;

    public string RenderIn(string culture, AttributeDefinition? definition) => Kind switch
    {
        AttributeKind.Option => definition?.LabelForOption(OptionCode!, culture) ?? OptionCode!,
        AttributeKind.LocalizedText => Text?.In(culture) ?? string.Empty,
        AttributeKind.Number => definition?.Unit is { Length: > 0 } unit
            ? $"{Number} {unit}"
            : Number?.ToString() ?? string.Empty,
        // Un booleano no aporta valor buscable: lo que alguien teclea es el
        // NOMBRE del atributo ("inducción"), no la palabra "sí". Que se emita o
        // no lo decide IsWorthIndexing, no esto.
        AttributeKind.Boolean => string.Empty,
        _ => RawText ?? string.Empty
    };
}
