using ElGuerre.Tendero.Catalog.Domain;
using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Todo lo estructurado que no se consulta relacionalmente vive en jsonb.
/// Un único sitio con las reglas de serialización evita que cada mapping
/// invente la suya (y que el JSON de la BD deje de ser diffable).
/// </summary>
internal static class Jsonb
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    /// <summary>LocalizedText se guarda como el diccionario cultura -> texto, no
    /// como el objeto: la forma en Postgres es {"es": "...", "en": "..."} y se lee
    /// a simple vista en psql.</summary>
    public static readonly ValueConverter<LocalizedText, string> LocalizedTextConverter = new(
        text => JsonSerializer.Serialize(text.Values, Options),
        json => new LocalizedText(
            JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options)!));

    /// <summary>
    /// Variante que tolera null. Hace falta para el texto alternativo de las
    /// imágenes, que vive DENTRO de una columna JSON: ahí EF no cortocircuita el
    /// null antes de llamar al conversor como sí hace con una propiedad normal,
    /// y el conversor no nulable revienta con NullReferenceException al leer.
    /// </summary>
    public static readonly ValueConverter<LocalizedText?, string?> NullableLocalizedTextConverter = new(
        text => text == null ? null : JsonSerializer.Serialize(text.Values, Options),
        json => json == null ? null : ReadLocalizedText(json));

    public static readonly ValueComparer<LocalizedText?> NullableLocalizedTextComparer = new(
        (left, right) => LocalizedTextEquals(left, right),
        text => text == null ? 0 : JsonSerializer.Serialize(text.Values, Options).GetHashCode(StringComparison.Ordinal),
        text => text == null ? null : new LocalizedText(text.Values));

    private static LocalizedText ReadLocalizedText(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options)!);

    private static bool LocalizedTextEquals(LocalizedText? left, LocalizedText? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return JsonSerializer.Serialize(left.Values, Options)
               == JsonSerializer.Serialize(right.Values, Options);
    }

    public static readonly ValueComparer<LocalizedText> LocalizedTextComparer = new(
        (left, right) => JsonSerializer.Serialize(left!.Values, Options)
                         == JsonSerializer.Serialize(right!.Values, Options),
        text => JsonSerializer.Serialize(text.Values, Options).GetHashCode(StringComparison.Ordinal),
        text => new LocalizedText(text.Values));

    /// <summary>
    /// El diccionario de atributos del agregado es OrdinalIgnoreCase; al
    /// rehidratar hay que devolverle ese comparador o Product.SetAttribute
    /// dejaría de ser insensible a mayúsculas tras un round-trip.
    /// </summary>
    public static readonly ValueConverter<Dictionary<string, string>, string> AttributesConverter = new(
        attributes => JsonSerializer.Serialize(attributes, Options),
        json => ReadAttributes(json));

    /// <summary>
    /// El orden de los ejes de variante. Es una lista y no un conjunto porque el
    /// orden es el dato: decide si la etiqueta de una línea de pedido dice
    /// "azul marino · 38" o "38 · azul marino".
    /// </summary>
    public static readonly ValueConverter<List<string>, string> StringListConverter = new(
        values => JsonSerializer.Serialize(values, Options),
        json => ReadStringList(json));

    private static List<string> ReadStringList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();

    /// <summary>
    /// Los valores de atributo de un producto. Se leen SIEMPRE con su producto y
    /// nunca se consultan sueltos, así que jsonb es lo correcto (ADR 0008) — lo
    /// que sí se consulta son las DEFINICIONES, y esas van a tabla.
    /// </summary>
    public static readonly ValueConverter<List<AttributeValue>, string> AttributeValuesConverter = new(
        values => JsonSerializer.Serialize(values, Options),
        json => ReadAttributeValues(json));

    public static readonly ValueComparer<List<AttributeValue>> AttributeValuesComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
        values => new List<AttributeValue>(values));

    private static List<AttributeValue> ReadAttributeValues(string json) =>
        JsonSerializer.Deserialize<List<AttributeValue>>(json, Options) ?? new List<AttributeValue>();

    public static readonly ValueComparer<List<AttributeOption>> AttributeOptionsComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        options => options.Aggregate(0, (hash, option) => HashCode.Combine(hash, option.Code.GetHashCode(StringComparison.Ordinal))),
        options => new List<AttributeOption>(options));

    public static readonly ValueConverter<List<AttributeOption>, string> AttributeOptionsConverter = new(
        options => JsonSerializer.Serialize(options, Options),
        json => ReadAttributeOptions(json));

    private static List<AttributeOption> ReadAttributeOptions(string json) =>
        JsonSerializer.Deserialize<List<AttributeOption>>(json, Options) ?? new List<AttributeOption>();

    public static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal),
        values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode(StringComparison.Ordinal))),
        values => new List<string>(values));

    public static readonly ValueComparer<Dictionary<string, string>> AttributesComparer = new(
        (left, right) => AttributesEqual(left, right),
        attributes => attributes.Aggregate(
            0,
            (hash, kv) => HashCode.Combine(
                hash,
                kv.Key.GetHashCode(StringComparison.OrdinalIgnoreCase),
                kv.Value.GetHashCode(StringComparison.Ordinal))),
        attributes => new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Money dentro de una colección compleja: EF (preview 6) no sabe enlazar un
    /// tipo complejo anidado a un parámetro del constructor de un record, así que
    /// el importe viaja como escalar "79.95 EUR" — legible y sin pérdida.
    /// </summary>
    public static readonly ValueConverter<Money, string> MoneyAsTextConverter = new(
        money => FormatMoney(money),
        text => ParseMoney(text));

    private static string FormatMoney(Money money) =>
        string.Concat(money.Amount.ToString(CultureInfo.InvariantCulture), " ", money.Currency);

    private static Money ParseMoney(string text)
    {
        var separator = text.LastIndexOf(' ');
        if (separator <= 0)
            throw new FormatException($"'{text}' is not a stored Money value; expected \"<amount> <currency>\".");

        return new Money(
            decimal.Parse(text[..separator], CultureInfo.InvariantCulture),
            text[(separator + 1)..]);
    }

    private static Dictionary<string, string> ReadAttributes(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

    private static bool AttributesEqual(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
                return false;
        }

        return true;
    }
}
