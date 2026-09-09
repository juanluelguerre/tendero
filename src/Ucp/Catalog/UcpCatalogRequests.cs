using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// What a caller sends. These exist because the REST binding puts them in a
/// request BODY with a schema of its own — <c>catalog_search.json#/$defs/search_request</c>
/// and its two siblings — which makes REST the one binding where both
/// directions can be validated. Over MCP the same fields arrive as tool
/// arguments and only the answer has a schema.
///
/// Nothing here is required by the specification except <c>ids</c> and
/// <c>id</c>: a search with no arguments at all is a browse.
/// </summary>
public sealed record UcpSearchRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("filters")]
    public IReadOnlyDictionary<string, string>? Filters { get; init; }

    [JsonPropertyName("pagination")]
    public UcpPageRequest? Pagination { get; init; }

    /// <summary>
    /// Buyer signals, platform environment and referral context. Accepted and
    /// **ignored**, which is said out loud rather than left for somebody to
    /// discover: a shop that silently drops what it was told is worse than one
    /// that says it does not use it yet. Personalisation is a later phase.
    /// </summary>
    [JsonPropertyName("context")]
    public JsonElement? Context { get; init; }

    [JsonPropertyName("signals")]
    public JsonElement? Signals { get; init; }

    [JsonPropertyName("attribution")]
    public JsonElement? Attribution { get; init; }

    [JsonPropertyName("meta")]
    public Dictionary<string, JsonElement>? Meta { get; init; }
}

public sealed record UcpLookupRequest
{
    [JsonPropertyName("ids")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    [JsonPropertyName("context")]
    public JsonElement? Context { get; init; }

    [JsonPropertyName("signals")]
    public JsonElement? Signals { get; init; }

    [JsonPropertyName("attribution")]
    public JsonElement? Attribution { get; init; }

    [JsonPropertyName("meta")]
    public Dictionary<string, JsonElement>? Meta { get; init; }
}

public sealed record UcpGetProductRequest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Partial option selections, for narrowing to a variant. Accepted and not
    /// yet acted on: this shop returns every variant with its own availability,
    /// which is more than a narrowing would give and is what the variant being
    /// the indexed unit buys (ADR 0015).
    /// </summary>
    [JsonPropertyName("selected")]
    public JsonElement? Selected { get; init; }

    [JsonPropertyName("preferences")]
    public JsonElement? Preferences { get; init; }

    [JsonPropertyName("context")]
    public JsonElement? Context { get; init; }

    [JsonPropertyName("signals")]
    public JsonElement? Signals { get; init; }

    [JsonPropertyName("attribution")]
    public JsonElement? Attribution { get; init; }

    [JsonPropertyName("meta")]
    public Dictionary<string, JsonElement>? Meta { get; init; }
}

/// <summary>
/// Pagination as the specification asks for it in a REQUEST: an opaque cursor
/// and a requested page size. Not the response shape, which is
/// <see cref="UcpPagination"/> and carries <c>has_next_page</c> — the two are
/// easy to confuse and this binding confused them once.
/// </summary>
public sealed record UcpPageRequest
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
