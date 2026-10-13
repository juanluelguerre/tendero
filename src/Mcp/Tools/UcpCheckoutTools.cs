using System.ComponentModel;
using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Catalog;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// Checkout's **MCP binding** (P11-1): UCP's five tools, over the operations the
/// REST binding calls — so the two transports cannot disagree.
///
/// **The one place `/mcp` is not anonymous** (ADR 0039). The shop's own tools and
/// UCP's catalogue stay read-only and open to anyone; these five need an agent's
/// token, tool by tool, because a checkout belongs to the agent that opened it.
/// UCP declares capabilities per service, not per transport, so declaring
/// checkout promised these tools here — and a promise in the profile is kept or
/// not made.
///
/// Business outcomes — a checkout, or an error response saying there is none —
/// come back as the tool's result, in `structuredContent`; protocol errors — no
/// profile, a profile nobody trusts, no idempotency key — as JSON-RPC errors
/// (UCP's MCP binding, §Error Handling).
/// </summary>
[McpServerToolType]
public sealed class UcpCheckoutTools
{
    [McpServerTool(Name = "create_checkout", UseStructuredContent = true, OutputSchemaType = typeof(UcpCheckoutAnswer), ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        UCP: open a checkout session for line items (item.id is a variant SKU from the
        catalogue tools). Returns the checkout with its status and messages. Requires an
        agent's bearer token and `meta.ucp-agent.profile`.
        """)]
    public static Task<JsonElement> CreateCheckoutAsync(
        UcpCheckoutOperations operations, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout: line_items, and optionally buyer, fulfillment, discounts, context.")]
        UcpCheckoutRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL; and optionally `idempotency-key`, so a retry opens no second session.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (call, culture, key) =>
            operations.CreateAsync(checkout, key, culture(checkout.Context?.Language), call, cancellationToken));

    [McpServerTool(Name = "get_checkout", UseStructuredContent = true, OutputSchemaType = typeof(UcpCheckoutAnswer), ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("UCP: the current state of a checkout session this agent opened.")]
    public static Task<JsonElement> GetCheckoutAsync(
        UcpCheckoutOperations operations, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`ucp-agent` with the platform `profile` URL.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (call, culture, _) =>
            operations.GetAsync(Id(id), culture(null), call, cancellationToken));

    [McpServerTool(Name = "update_checkout", UseStructuredContent = true, OutputSchemaType = typeof(UcpCheckoutAnswer), ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        UCP: replace a checkout session's line items, buyer, fulfillment and discount codes —
        a full replacement, not a patch.
        """)]
    public static Task<JsonElement> UpdateCheckoutAsync(
        UcpCheckoutOperations operations, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("The whole checkout as it should be now.")] UcpCheckoutRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL; and optionally `idempotency-key`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (call, culture, key) =>
            operations.UpdateAsync(Id(id), checkout, key, culture(checkout.Context?.Language), call, cancellationToken));

    [McpServerTool(Name = "complete_checkout", UseStructuredContent = true, OutputSchemaType = typeof(UcpCheckoutAnswer), ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        UCP: place the order, paying with exactly one instrument from a payment handler the
        checkout offered. Requires `meta.idempotency-key`: a retry with the same key never
        buys twice.
        """)]
    public static Task<JsonElement> CompleteCheckoutAsync(
        UcpCheckoutOperations operations, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`payment.instruments`: one, with its handler_id and credential; and `ap2.checkout_mandate` when AP2 was negotiated.")] UcpCompleteRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL, and `idempotency-key`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: true, cancellationToken, (call, culture, key) =>
            operations.CompleteAsync(Id(id), checkout, key, culture(null), call, cancellationToken));

    [McpServerTool(Name = "cancel_checkout", UseStructuredContent = true, OutputSchemaType = typeof(UcpCheckoutAnswer), ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("UCP: cancel a checkout session that is still open. Requires `meta.idempotency-key`.")]
    public static Task<JsonElement> CancelCheckoutAsync(
        UcpCheckoutOperations operations, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`ucp-agent` with the platform `profile` URL, and `idempotency-key`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: true, cancellationToken, (call, culture, key) =>
            operations.CancelAsync(Id(id), key, culture(null), call, cancellationToken));

    /// <summary>
    /// The part every tool shares: an agent, a well-formed <c>meta</c>, negotiation,
    /// and the answer spelled the MCP way.
    /// </summary>
    private static async Task<JsonElement> RunAsync(
        IHttpContextAccessor requests, IPrincipalAccessor principals, IPlatformProfiles profiles, IOptions<UcpOptions> options,
        IReadOnlyDictionary<string, JsonElement> meta, bool requireKey, CancellationToken cancellationToken,
        Func<UcpCheckoutCall, Func<string?, string>, string?, Task<UcpAnswer>> operation)
    {
        // The endpoint has already answered nobody with a 401 and a person with
        // a 403 (McpAuthorization); this is the tool not taking that on trust.
        if (!principals.Current.IsAgent)
            throw Protocol(new UcpAnswer(401, new UcpProtocolError("unauthorized", "UCP checkout tools need an agent's bearer token: a checkout belongs to the agent that opens it.")));

        string? profile;
        try
        {
            profile = UcpAgentMetadata.ProfileOf(meta) ?? throw new ArgumentException("`meta.ucp-agent.profile` is required for checkout.");
        }
        catch (ArgumentException exception)
        {
            throw Protocol(new UcpAnswer(400, new UcpProtocolError("invalid_profile_url", exception.Message)));
        }

        var key = meta.TryGetValue("idempotency-key", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (requireKey && string.IsNullOrWhiteSpace(key))
            throw Protocol(new UcpAnswer(400, new UcpProtocolError("idempotency_key_required", "`meta.idempotency-key` is required: a retry must not happen twice.")));

        var (negotiated, refusal) = await UcpCheckoutOperations.NegotiateAsync(profile, profiles, cancellationToken);
        var http = requests.HttpContext;
        var answer = refusal ?? await operation(
            new UcpCheckoutCall(negotiated!, new UcpLinks(http is null ? "" : $"{http.Request.Scheme}://{http.Request.Host}", options.Value.StorefrontUrl)),
            language => CultureNegotiation.Resolve(language, http?.Request.Headers.AcceptLanguage),
            key);

        // A protocol error is a JSON-RPC error; a business outcome — even "there is
        // no checkout" — is a result the agent reads.
        if (answer.Body is UcpProtocolError)
            throw Protocol(answer);

        return JsonSerializer.SerializeToElement(answer.Body, answer.Body.GetType());
    }

    /// <summary>
    /// A protocol error the JSON-RPC way (UCP §Error Codes): an `error`, never a
    /// result — `-32001` when what failed was discovery (the profile, or the
    /// version it speaks), `-32602` for a call that is malformed, `-32000` for
    /// the rest — with UCP's own `code` and `content` as its `data`, which is
    /// what a platform reads to tell one refusal from another.
    /// </summary>
    private static McpProtocolException Protocol(UcpAnswer answer)
    {
        var error = (UcpProtocolError)answer.Body;
        var code = Discovery.Contains(error.Code) ? -32001 : answer.Status == 400 ? -32602 : -32000;

        return new McpProtocolException(error.Content, (McpErrorCode)code) { Data = { ["code"] = error.Code, ["content"] = error.Content } };
    }

    private static readonly HashSet<string> Discovery =
        ["invalid_profile_url", "profile_unreachable", "profile_malformed", "version_unsupported"];

    private static Guid Id(string id) =>
        Guid.TryParse(id, out var parsed) ? parsed : throw new McpException($"'{id}' is not a checkout session id.");
}

/// <summary>
/// What a checkout tool answers, as its declared output: an object — a UCP
/// checkout, or UCP's error response saying there is none. Its shape is UCP's
/// own schemas' to describe, and both answers are built as JSON, so the tools
/// return a <see cref="JsonElement"/>. That type's inferred schema is <c>true</c>,
/// which is not an object schema: a 2026-07-28 client refused the whole tool
/// list over it. This says the one thing both answers share, and nothing a
/// client would then reject an error response for.
/// </summary>
public sealed class UcpCheckoutAnswer;

