using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tendero.SharedKernel;

namespace Tendero.Persistence;

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
