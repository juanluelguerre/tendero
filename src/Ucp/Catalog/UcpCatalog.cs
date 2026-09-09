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

/// <summary>
/// A category, which is a PATH and not a node.
///
/// This record read <c>{ id, name }</c> until the schema was run against it, and
/// the schema requires <c>value</c> — *"Category value or path (e.g. 'Apparel &gt;
/// Shirts', '1604')"*. That is a better fit for what Tendero has than the shape
/// that was guessed: the catalogue's branch is `Hogar &gt; Cocina &gt; Menaje de
/// cocina`, and emitting its three steps as three categories would claim three
/// unrelated ones rather than one path.
///
/// <c>taxonomy</c> is <c>merchant</c>, which is one of the specification's
/// well-known values and is the truthful one: this is the shop's own taxonomy,
/// not Google's.
/// </summary>
public sealed record UcpCategory
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("taxonomy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Taxonomy { get; init; }
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

    /// <summary>
    /// Which requested identifier resolved to this variant, and how.
    ///
    /// **Required in a `lookup_catalog` answer and absent everywhere else**,
    /// which is a distinction worth having rather than resenting: a lookup is
    /// the one operation where the caller named things, so it is the one place
    /// a shop owes an answer to "which of my ids is this?". A search or a
    /// product detail has nothing to correlate against.
    ///
    /// Found by validation. The lookup response `$ref`s a `lookup_variant` that
    /// adds `inputs` to the ordinary variant, and nothing in the prose mentions
    /// it.
    /// </summary>
    [JsonPropertyName("inputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UcpInputCorrelation>? Inputs { get; init; }
}

/// <summary>
/// One requested identifier, and how it resolved.
///
/// <c>exact</c> means the caller named this variant — its SKU. <c>featured</c>
/// means the server chose it as representative, which is what happens when a
/// caller names a PRODUCT and gets its variants back. Tendero's lookup takes
/// product codes, so every correlation it emits is <c>featured</c>, and saying
/// that is more useful than claiming a precision the operation does not have.
/// </summary>
public sealed record UcpInputCorrelation
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("match")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Match { get; init; }
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

/// <summary>
/// Pagination in a RESPONSE, which is cursor-shaped and not the
/// <c>limit/offset/total</c> this record carried until the schema was run
/// against it. Only <c>has_next_page</c> is required, and the specification says
/// a cursor MUST be present when it is true.
///
/// Tendero's own search is offset-paged, so the cursor it hands back is the next
/// offset, opaque to the caller by contract even though it is legible. That is
/// the honest translation: the protocol asks for a token to get the next page,
/// and this is one.
/// </summary>
public sealed record UcpPagination
{
    [JsonPropertyName("has_next_page")]
    public required bool HasNextPage { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }

    [JsonPropertyName("total_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalCount { get; init; }
}

/// <summary>
/// Every catalogue answer opens with this, and it carries the version and
/// nothing else.
///
/// It had a <c>request_id</c> and a <c>timestamp</c>, taken from a
/// documentation page's prose. **Neither exists anywhere in the schemas.** They
/// would have been tolerated — the envelope allows additional properties — and
/// that is exactly why they had to go: a document whose whole claim is "this is
/// the protocol" should not carry fields the protocol never defined.
/// </summary>
public sealed record UcpEnvelope
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }
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
