using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Everything structured that is not queried relationally lives in jsonb.
/// A single place holding the serialisation rules keeps each mapping from
/// inventing its own (and the database's JSON from stopping being diffable).
/// </summary>
internal static class Jsonb
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    /// <summary>LocalizedText is stored as the culture -> text dictionary and not
    /// as the object: the shape in Postgres is {"es": "…", "en": "…"} and it reads
    /// at a glance in psql.</summary>
    public static readonly ValueConverter<LocalizedText, string> LocalizedTextConverter = new(
        text => JsonSerializer.Serialize(text.Values, Options),
        json => new LocalizedText(
            JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options)!));

    /// <summary>
    /// The null-tolerant variant. It is needed for the images' alternative text,
    /// which lives INSIDE a JSON column: there EF does not short-circuit the null
    /// before calling the converter the way it does for an ordinary property, and
    /// the non-nullable converter blows up with a NullReferenceException on read.
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
    /// The aggregate's attribute dictionary is OrdinalIgnoreCase; on rehydration
    /// it has to be given that comparer back, or Product.SetAttribute would stop
    /// being case-insensitive after a round trip.
    /// </summary>
    public static readonly ValueConverter<Dictionary<string, string>, string> AttributesConverter = new(
        attributes => JsonSerializer.Serialize(attributes, Options),
        json => ReadAttributes(json));

    /// <summary>
    /// The order of the variant axes. A list and not a set because the order is
    /// the data: it decides whether an order line's label reads
    /// "azul marino · 38" or "38 · azul marino".
    /// </summary>
    public static readonly ValueConverter<List<string>, string> StringListConverter = new(
        values => JsonSerializer.Serialize(values, Options),
        json => ReadStringList(json));

    private static List<string> ReadStringList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();

    /// <summary>
    /// A product's attribute values. They are ALWAYS read with their product and
    /// never queried on their own, so jsonb is the right call (ADR 0008) — what
    /// does get queried are the DEFINITIONS, and those go to a table.
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
    /// Money inside a complex collection: EF (preview 6) cannot bind a nested
    /// complex type to a record constructor parameter, so the amount travels as
    /// the scalar "79.95 EUR" — readable and lossless.
    /// </summary>
    public static readonly ValueConverter<Money, string> MoneyAsTextConverter = new(
        money => FormatMoney(money),
        text => ParseMoney(text));

    /// <summary>
    /// The nullable variant, for an amount that genuinely has not happened yet:
    /// a return carries no refund until one is paid. Null and zero are different
    /// facts, and a converter that flattened them would lose the difference.
    /// </summary>
    public static readonly ValueConverter<Money?, string?> NullableMoneyAsTextConverter = new(
        money => money == null ? null : FormatMoney(money.Value),
        text => text == null ? null : ParseMoney(text));

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
