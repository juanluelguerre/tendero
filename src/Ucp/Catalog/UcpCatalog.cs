using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The catalogue as the Universal Commerce Protocol defines it, not as Tendero
/// happens to model it.
///
/// **These records are a translation, and that is the point.** The shop's own
/// answers are already good — `SearchHit` and `ProductDetail` — and they are not
/// this. UCP says <c>price_range</c> where we say `PriceFrom`/`PriceTo`,
/// <c>title</c> where we say `Name`, and it puts money in **minor units** where
/// we carry a decimal. A binding that returned our shapes under UCP's tool names
/// would be the worst of both: a manifest claiming a protocol, and a payload
/// only this shop understands.
///
/// Every member names itself, and here the naming is load-bearing rather than
/// incidental: <c>price_range</c>, <c>list_price</c>, <c>request_id</c> are
/// snake_case, and this API answers in camelCase.
/// </summary>
public sealed record UcpPrice
{
    /// <summary>
    /// ISO 4217 **minor units** — 39,00 € is 3900, not 39. The specification is
    /// explicit and it is the single easiest thing to get wrong in this whole
    /// binding, because a wrong answer looks like a plausible price.
    /// </summary>
    [JsonPropertyName("amount")]
    public required long Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }
}

public sealed record UcpPriceRange
{
    [JsonPropertyName("min")]
    public required UcpPrice Min { get; init; }

    [JsonPropertyName("max")]
    public required UcpPrice Max { get; init; }
}

/// <summary>Content in one or more formats; plain text is the one Tendero has.</summary>
public sealed record UcpDescription
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed record UcpCategory
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed record UcpMedia
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("alt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alt { get; init; }
}

public sealed record UcpOptionValue
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

public sealed record UcpProductOption
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("values")]
    public required IReadOnlyList<UcpOptionValue> Values { get; init; }
}

public sealed record UcpSelectedOption
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

/// <summary>
/// Whether this variant can be bought. Tendero knows this per variant because
/// the variant is the indexed unit (ADR 0015), which is exactly what a
/// product-level answer could not have told an agent.
/// </summary>
public sealed record UcpAvailability
{
    [JsonPropertyName("in_stock")]
    public required bool InStock { get; init; }
}

public sealed record UcpVariant
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("sku")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sku { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public required UcpDescription Description { get; init; }

    [JsonPropertyName("price")]
    public required UcpPrice Price { get; init; }

    [JsonPropertyName("availability")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpAvailability? Availability { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpSelectedOption>? Options { get; init; }

    [JsonPropertyName("media")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpMedia>? Media { get; init; }
}

public sealed record UcpProduct
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The URL-safe slug. Decoration in this shop, and the specification
    /// agrees it is optional — the identifier is <see cref="Id"/> (ADR 0026).</summary>
    [JsonPropertyName("handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Handle { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public required UcpDescription Description { get; init; }

    [JsonPropertyName("categories")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpCategory>? Categories { get; init; }

    [JsonPropertyName("price_range")]
    public required UcpPriceRange PriceRange { get; init; }

    [JsonPropertyName("media")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpMedia>? Media { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpProductOption>? Options { get; init; }

    [JsonPropertyName("variants")]
    public required IReadOnlyList<UcpVariant> Variants { get; init; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// A business outcome, as opposed to a transport failure.
///
/// The specification draws that line and it is a good one: JSON-RPC errors are
/// for "I could not process this", and <c>messages</c> is for "I processed it
/// and here is something you should know". A shop that returned a JSON-RPC error
/// because a search found nothing would be telling an agent its request was
/// malformed.
/// </summary>
public sealed record UcpMessage
{
    /// <summary><c>error</c>, <c>warning</c> or <c>info</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>A JSONPath (RFC 9535) into the request this is about.</summary>
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}

public sealed record UcpPagination
{
    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonPropertyName("offset")]
    public required int Offset { get; init; }

    [JsonPropertyName("total")]
    public required long Total { get; init; }
}

/// <summary>Every catalogue answer opens with this.</summary>
public sealed record UcpEnvelope
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record UcpCatalogPage
{
    [JsonPropertyName("ucp")]
    public required UcpEnvelope Ucp { get; init; }

    [JsonPropertyName("products")]
    public required IReadOnlyList<UcpProduct> Products { get; init; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpMessage>? Messages { get; init; }

    [JsonPropertyName("pagination")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpPagination? Pagination { get; init; }
}

public sealed record UcpProductAnswer
{
    [JsonPropertyName("ucp")]
    public required UcpEnvelope Ucp { get; init; }

    [JsonPropertyName("product")]
    public required UcpProduct Product { get; init; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpMessage>? Messages { get; init; }
}
