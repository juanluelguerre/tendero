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
/// Two of the four sections the specification defines are absent rather than
/// empty, and that is a statement: <c>payment_handlers</c> and
/// <c>signing_keys</c> belong to the transactional half in phase 11. A shop
/// that published an empty payment handler map would be advertising a checkout
/// it cannot honour, which is worse than saying nothing.
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
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required IReadOnlyDictionary<string, UcpCapabilityDeclaration> Capabilities { get; init; }
}

/// <summary>One transport binding of one service: how to reach it and what it speaks.</summary>
public sealed record UcpService
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Where a human reads the definition.</summary>
    [JsonPropertyName("spec")]
    public required string Spec { get; init; }

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
    /// </summary>
    [JsonPropertyName("schema")]
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

    [JsonPropertyName("spec")]
    public required string Spec { get; init; }

    /// <summary>Required for every capability, per the specification.</summary>
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
