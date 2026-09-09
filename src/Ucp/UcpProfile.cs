using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// The document served at <c>/.well-known/ucp</c>: what this shop can do, in a
/// shape a machine reads without being told anything in advance.
///
/// **Every field name is the specification's, and every one of them is written
/// out in a <see cref="JsonPropertyNameAttribute"/>.** The specification uses
/// snake_case — <c>payment_handlers</c>, <c>signing_keys</c> — and the rest of
/// this API answers in camelCase, so leaving the names to a serializer policy
/// would make the manifest's correctness depend on a setting somewhere else in
/// the process. A property name is a fact about somebody else's protocol here,
/// not a style choice about ours.
///
/// **Corrected 2026-09-08 against the specification's own JSON Schema**, which
/// is the second time this phase that reading beat reasoning. Two things this
/// file used to claim were wrong. The signing-key field is <c>keys</c>, a JWK
/// Set, and not <c>signing_keys</c>. And a business profile REQUIRES
/// <c>payment_handlers</c>: the argument written here — that an empty map
/// "advertises a checkout it cannot honour" — was simply not true. An empty map
/// advertises no payment handlers, which is exactly the case, and omitting the
/// key makes the document invalid rather than modest.
///
/// <c>keys</c> stays absent, because it is genuinely optional and there is
/// nothing to publish until phase 11 signs something.
/// </summary>
public sealed record UcpProfile
{
    [JsonPropertyName("ucp")]
    public required UcpDocument Ucp { get; init; }
}

/// <summary>
/// The protocol object: what version is spoken, where the transports are, and
/// which capabilities are on offer.
/// </summary>
public sealed record UcpDocument
{
    /// <summary>The specification version, as a date.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Services keyed by reverse-DNS namespace — <c>dev.ucp.shopping</c> — each
    /// holding one entry per transport binding it offers. It is a LIST because a
    /// shop may serve the same service over REST and over MCP, which Tendero
    /// will: the read tools already exist over MCP.
    /// </summary>
    [JsonPropertyName("services")]
    public required IReadOnlyDictionary<string, IReadOnlyList<UcpService>> Services { get; init; }

    /// <summary>
    /// Capabilities keyed by their own reverse-DNS id —
    /// <c>dev.ucp.shopping.catalog.search</c>. The verbs of the protocol, and
    /// the half an agent actually branches on.
    ///
    /// A LIST per key, exactly like <see cref="Services"/>, and this file had it
    /// as a single object until the schema said otherwise. The two registries
    /// have the same shape.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required IReadOnlyDictionary<string, IReadOnlyList<UcpCapabilityDeclaration>> Capabilities { get; init; }

    /// <summary>
    /// Required of a business profile, and empty here.
    ///
    /// Empty is not the same as absent and it is not the same as a promise: an
    /// empty registry says this shop settles no payments over UCP, which is true
    /// until phase 11 and is what the schema asks a merchant to say.
    /// </summary>
    [JsonPropertyName("payment_handlers")]
    public required IReadOnlyDictionary<string, IReadOnlyList<object>> PaymentHandlers { get; init; }
}

/// <summary>One transport binding of one service: how to reach it and what it speaks.</summary>
public sealed record UcpService
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Where a human reads the definition. Optional in a BUSINESS profile — the
    /// platform schema requires it, the merchant schema does not — and sent
    /// anyway, because a reader who lands on this document should be one click
    /// from what it means.
    /// </summary>
    [JsonPropertyName("spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Spec { get; init; }

    /// <summary><c>rest</c>, <c>mcp</c>, <c>a2a</c> or <c>embedded</c>.</summary>
    [JsonPropertyName("transport")]
    public required string Transport { get; init; }

    /// <summary>The base URL an agent calls.</summary>
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    /// <summary>
    /// Where the machine-readable schema lives. For Tendero this points into the
    /// committed OpenAPI document, which is the whole reason that document is
    /// generated from the running API and contract-tested: a schema URL is a
    /// promise, and this one is kept by a test rather than by attention.
    ///
    /// Optional for a business profile's MCP binding, and omitted rather than
    /// written as null — the first generated profile carried
    /// <c>"schema": null</c>, which is the very thing this file argues against
    /// two records above: agents branch on a key existing.
    /// </summary>
    [JsonPropertyName("schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; init; }
}

/// <summary>
/// One capability on offer. <c>extends</c> and <c>config</c> are omitted when
/// they are not used rather than serialised as null, because a manifest is read
/// by machines that branch on a key existing.
/// </summary>
public sealed record UcpCapabilityDeclaration
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Optional in a business profile, and sent anyway.</summary>
    [JsonPropertyName("spec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Spec { get; init; }

    /// <summary>
    /// **Required of a business profile**, because it is what a platform fetches
    /// to compose the capability during negotiation. It is the specification's
    /// canonical schema for this capability, not a document of ours: we are
    /// declaring which contract we speak, not inventing one.
    /// </summary>
    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    /// <summary>The capability this one extends, for a vendor extension.</summary>
    [JsonPropertyName("extends")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Extends { get; init; }

    /// <summary>Capability-specific configuration, when the capability defines any.</summary>
    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object>? Config { get; init; }
}
