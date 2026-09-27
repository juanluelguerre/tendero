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
    [McpServerTool(Name = "create_checkout", UseStructuredContent = true)]
    [Description("""
        UCP: open a checkout session for line items (item.id is a variant SKU from the
        catalogue tools). Returns the checkout with its status and messages. Requires an
        agent's bearer token and `meta.ucp-agent.profile`.
        """)]
    public static Task<JsonElement> CreateCheckoutAsync(
        ICommandDispatcher dispatcher, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout: line_items, and optionally buyer, fulfillment, discounts, context.")]
        UcpCheckoutRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (active, links, culture, _) =>
            UcpCheckoutOperations.CreateAsync(dispatcher, checkout, culture(checkout.Context?.Language), active, links, cancellationToken));

    [McpServerTool(Name = "get_checkout", UseStructuredContent = true)]
    [Description("UCP: the current state of a checkout session this agent opened.")]
    public static Task<JsonElement> GetCheckoutAsync(
        IQueryDispatcher dispatcher, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`ucp-agent` with the platform `profile` URL.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (active, links, culture, _) =>
            UcpCheckoutOperations.GetAsync(dispatcher, Id(id), culture(null), active, links, cancellationToken));

    [McpServerTool(Name = "update_checkout", UseStructuredContent = true)]
    [Description("""
        UCP: replace a checkout session's line items, buyer, fulfillment and discount codes —
        a full replacement, not a patch.
        """)]
    public static Task<JsonElement> UpdateCheckoutAsync(
        ICommandDispatcher dispatcher, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("The whole checkout as it should be now.")] UcpCheckoutRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: false, cancellationToken, (active, links, culture, _) =>
            UcpCheckoutOperations.UpdateAsync(dispatcher, Id(id), checkout, culture(checkout.Context?.Language), active, links, cancellationToken));

    [McpServerTool(Name = "complete_checkout", UseStructuredContent = true)]
    [Description("""
        UCP: place the order, paying with exactly one instrument from a payment handler the
        checkout offered. Requires `meta.idempotency-key`: a retry with the same key never
        buys twice.
        """)]
    public static Task<JsonElement> CompleteCheckoutAsync(
        ICommandDispatcher dispatcher, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`payment.instruments`: one, with its handler_id and credential.")] UcpCompleteRequest checkout,
        [Description("`ucp-agent` with the platform `profile` URL, and `idempotency-key`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: true, cancellationToken, (active, links, culture, key) =>
            UcpCheckoutOperations.CompleteAsync(dispatcher, Id(id), checkout, key, culture(null), active, links, cancellationToken));

    [McpServerTool(Name = "cancel_checkout", UseStructuredContent = true)]
    [Description("UCP: cancel a checkout session that is still open. Requires `meta.idempotency-key`.")]
    public static Task<JsonElement> CancelCheckoutAsync(
        ICommandDispatcher dispatcher, IHttpContextAccessor requests, IPrincipalAccessor principals,
        IPlatformProfiles profiles, IOptions<UcpOptions> options,
        [Description("The checkout session id.")] string id,
        [Description("`ucp-agent` with the platform `profile` URL, and `idempotency-key`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default) =>
        RunAsync(requests, principals, profiles, options, meta, requireKey: true, cancellationToken, (active, links, culture, _) =>
            UcpCheckoutOperations.CancelAsync(dispatcher, Id(id), culture(null), active, links, cancellationToken));

    /// <summary>
    /// The part every tool shares: an agent, a well-formed <c>meta</c>, negotiation,
    /// and the answer spelled the MCP way.
    /// </summary>
    private static async Task<JsonElement> RunAsync(
        IHttpContextAccessor requests, IPrincipalAccessor principals, IPlatformProfiles profiles, IOptions<UcpOptions> options,
        IReadOnlyDictionary<string, JsonElement> meta, bool requireKey, CancellationToken cancellationToken,
        Func<IReadOnlySet<string>, UcpLinks, Func<string?, string>, string?, Task<UcpAnswer>> operation)
    {
        if (!principals.Current.IsAgent)
            throw new McpException("UCP checkout tools need an agent's bearer token: a checkout belongs to the agent that opens it.");

        string? profile;
        try
        {
            profile = UcpAgentMetadata.ProfileOf(meta) ?? throw new ArgumentException("`meta.ucp-agent.profile` is required for checkout.");
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }

        var key = meta.TryGetValue("idempotency-key", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (requireKey && string.IsNullOrWhiteSpace(key))
            throw new McpException("`meta.idempotency-key` is required: a retry must not happen twice.");

        var (active, refusal) = await UcpCheckoutOperations.NegotiateAsync(profile, profiles, cancellationToken);
        var http = requests.HttpContext;
        var answer = refusal ?? await operation(
            active!,
            new UcpLinks(http is null ? "" : $"{http.Request.Scheme}://{http.Request.Host}", options.Value.StorefrontUrl),
            language => CultureNegotiation.Resolve(language, http?.Request.Headers.AcceptLanguage),
            key);

        // A protocol error is a JSON-RPC error; a business outcome — even "there is
        // no checkout" — is a result the agent reads.
        if (answer.Body is UcpProtocolError protocol)
            throw new McpException($"{protocol.Code}: {protocol.Content}");

        return JsonSerializer.SerializeToElement(answer.Body, answer.Body.GetType());
    }

    private static Guid Id(string id) =>
        Guid.TryParse(id, out var parsed) ? parsed : throw new McpException($"'{id}' is not a checkout session id.");
}
