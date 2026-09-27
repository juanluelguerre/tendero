using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Ap2;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Negotiation;
using ElGuerre.Tendero.Ucp.Signing;

namespace ElGuerre.Tendero.Ucp.Checkout;

/// <summary>What an operation answers, before a transport spells it: an HTTP status and a body, or a protocol error.</summary>
public sealed record UcpAnswer(int Status, object Body);

/// <summary>UCP's protocol error body (§Error Codes): not a checkout, and not a business outcome.</summary>
public sealed record UcpProtocolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("content")] string Content);

/// <summary>What negotiation settled with this caller: the capabilities active, and the platform's profile — its keys verify its mandates.</summary>
public sealed record UcpNegotiated(IReadOnlySet<string> Active, PlatformProfile Platform);

/// <summary>One checkout call: what was negotiated, and where the shop's own pages are.</summary>
public sealed record UcpCheckoutCall(UcpNegotiated Negotiated, UcpLinks Links)
{
    public IReadOnlySet<string> Active => Negotiated.Active;

    /// <summary>AP2's mandates were negotiated: every checkout is signed, and none completes without a mandate.</summary>
    public bool Ap2 => Active.Contains(Ap2MandateCapability.Name);
}

/// <summary>
/// Checkout's behaviour, once, for both transports — the rule the catalogue set
/// (<see cref="Catalog.UcpCatalogOperations"/>): a transport marshals and maps
/// errors; negotiation, dispatch, translation and AP2's signatures are here.
/// </summary>
public sealed class UcpCheckoutOperations(
    ICommandDispatcher commands, IQueryDispatcher queries, IMerchantSigner signer, CheckoutMandates mandates)
{
    /// <summary>
    /// The capabilities active for this caller: its platform profile, fetched from a
    /// trusted host, intersected with what the shop declares. No checkout without a
    /// profile — negotiation is how an agent and a shop agree on a shape at all.
    /// </summary>
    public static async Task<(UcpNegotiated? Negotiated, UcpAnswer? Refusal)> NegotiateAsync(
        string? profileUrl, IPlatformProfiles profiles, CancellationToken cancellationToken)
    {
        if (profileUrl is null)
            return (null, new UcpAnswer(400, new UcpProtocolError("invalid_profile_url", "A UCP-Agent header naming the platform profile is required.")));

        var (profile, problem) = await profiles.GetAsync(profileUrl, cancellationToken);
        if (profile is null)
            return (null, problem switch
            {
                ProfileProblem.Unreachable => new UcpAnswer(424, new UcpProtocolError("profile_unreachable", "The platform profile could not be fetched.")),
                ProfileProblem.Malformed => new UcpAnswer(422, new UcpProtocolError("profile_malformed", "The platform profile is not a UCP profile.")),
                _ => new UcpAnswer(400, new UcpProtocolError("invalid_profile_url", "This shop does not fetch platform profiles from that address.")),
            });

        var active = UcpNegotiation.Intersect(UcpCapabilities.Declared, profile);
        if (!active.Contains(CheckoutCapability.Name))
            return (null, new UcpAnswer(200, Error("capabilities_incompatible", "The platform and this shop share no checkout capability.", "unrecoverable")));

        // Only what a checkout response carries: the catalogue is negotiated too,
        // and has no place in this one (UCP §Response Capability Selection).
        return (new UcpNegotiated(
            active.Where(name => name is CheckoutCapability.Name or FulfillmentCapability.Name or DiscountCapability.Name or Ap2MandateCapability.Name)
                .ToHashSet(StringComparer.Ordinal),
            profile), null);
    }

    public async Task<UcpAnswer> CreateAsync(UcpCheckoutRequest request, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        Answer(await commands.SendAsync(new OpenCheckoutSessionCommand(
            UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), call.Active), culture), cancellationToken), call, created: true);

    public async Task<UcpAnswer> UpdateAsync(Guid id, UcpCheckoutRequest request, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        Answer(await commands.SendAsync(new UpdateCheckoutSessionCommand(
            new CheckoutSessionId(id), UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), call.Active), culture), cancellationToken), call);

    public async Task<UcpAnswer> GetAsync(Guid id, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        Answer(await queries.SendAsync(new GetCheckoutSessionQuery(new CheckoutSessionId(id), culture), cancellationToken), call);

    /// <summary>
    /// Completion: exactly one instrument, from a handler this shop offered. The
    /// sandbox handler's credential token names the fake provider's instrument.
    /// With AP2 negotiated, a checkout mandate is required, and it has to authorise
    /// this session's terms as they are now.
    /// </summary>
    public async Task<UcpAnswer> CompleteAsync(
        Guid id, UcpCompleteRequest request, string? idempotencyKey, string culture, UcpCheckoutCall call, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return new UcpAnswer(400, new UcpProtocolError("idempotency_key_required", "Completing a checkout needs an Idempotency-Key: a retry must not buy twice."));

        var instruments = request.Payment?.Instruments ?? [];
        if (instruments.Count != 1)
            return new UcpAnswer(400, new UcpProtocolError("invalid_request", "Complete takes exactly one payment instrument."));

        var instrument = instruments[0];
        if (instrument.HandlerId != UcpCheckoutMapper.SandboxHandlerId || string.IsNullOrWhiteSpace(instrument.Credential?.Token))
            return new UcpAnswer(400, new UcpProtocolError("invalid_request", $"The instrument must come from the handler '{UcpCheckoutMapper.SandboxHandlerId}' with a credential token."));

        var sessionId = new CheckoutSessionId(id);
        if (call.Ap2 && await MandateRefusalAsync(sessionId, request.Ap2?.CheckoutMandate, culture, call, cancellationToken) is { } refusal)
            return refusal;

        return Answer(await commands.SendAsync(new CompleteCheckoutSessionCommand(
            sessionId, instrument.Credential!.Token!, idempotencyKey, culture), cancellationToken), call);
    }

    public async Task<UcpAnswer> CancelAsync(Guid id, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        Answer(await commands.SendAsync(new CancelCheckoutSessionCommand(new CheckoutSessionId(id), culture), cancellationToken), call);

    /// <summary>
    /// The mandate against the session as it is now (UCP §Business Verification):
    /// it verifies, and the checkout it carries is this one with these goods at
    /// these amounts. A refusal is the current checkout — freshly signed, so the
    /// platform can ask the person again — with the one error that says why.
    ///
    /// The session is read and then completed in two steps, so an update landing
    /// between them is not seen here; the completion re-quotes against what was
    /// last shown, and `P11-5` moves the approved amount into the order itself.
    /// </summary>
    private async Task<UcpAnswer?> MandateRefusalAsync(
        CheckoutSessionId id, string? presentation, string culture, UcpCheckoutCall call, CancellationToken cancellationToken)
    {
        var current = await queries.SendAsync(new GetCheckoutSessionQuery(id, culture), cancellationToken);
        if (current.Outcome != CheckoutSessionOutcome.Ok)
            return Answer(current, call);

        var shown = UcpCheckoutMapper.ToWire(current.Session!, call.Active, call.Links);
        var verdict = mandates.Verify(presentation, call.Negotiated.Platform);
        var code = verdict.Error
                   ?? (UcpCheckoutTerms.Same(verdict.Mandate!.Checkout, Signed(shown)) ? null : Ap2ErrorCodes.MandateScopeMismatch);

        return code is null
            ? null
            : new UcpAnswer(200, Signed(UcpCheckoutMapper.ToWire(
                current.Session! with { Issues = [.. current.Session.Issues, new CheckoutIssue(code, "payment")] }, call.Active, call.Links)));
    }

    /// <summary>What was not negotiated is not read: no fulfillment extension, no destination; no discount extension, no codes.</summary>
    private static CheckoutDetails Allowed(CheckoutDetails details, IReadOnlySet<string> active) => details with
    {
        Destination = active.Contains(FulfillmentCapability.Name) ? details.Destination : null,
        ShippingOptionCode = active.Contains(FulfillmentCapability.Name) ? details.ShippingOptionCode : null,
        Coupons = active.Contains(DiscountCapability.Name) ? details.Coupons : [],
    };

    private UcpAnswer Answer(CheckoutSessionResult result, UcpCheckoutCall call, bool created = false) => result.Outcome switch
    {
        CheckoutSessionOutcome.Ok => new UcpAnswer(created ? 201 : 200, Checkout(result.Session!, call)),
        CheckoutSessionOutcome.NotReady or CheckoutSessionOutcome.NotPlaced => new UcpAnswer(200, Checkout(result.Session!, call)),
        CheckoutSessionOutcome.NotFound => new UcpAnswer(404, Error("not_found", "There is no checkout with that id for this agent.", "unrecoverable")),
        CheckoutSessionOutcome.NotOpen => new UcpAnswer(409, Error("checkout_not_open", "This checkout is completed, canceled or expired: nothing in it changes.", "unrecoverable")),
        CheckoutSessionOutcome.NothingPurchasable => new UcpAnswer(200, Error("item_unavailable", "None of the items can be bought right now.", "unrecoverable")),
        _ => new UcpAnswer(403, Error("unauthorized", "Only an agent opens a UCP checkout.", "unrecoverable")),
    };

    /// <summary>
    /// A checkout as the wire carries it — signed when AP2 was negotiated, and then
    /// as the very JSON that was signed, so neither transport can serialise it into
    /// other bytes than the ones the signature covers.
    /// </summary>
    private object Checkout(CheckoutSessionView session, UcpCheckoutCall call)
    {
        var checkout = UcpCheckoutMapper.ToWire(session, call.Active, call.Links);
        return call.Ap2 ? Signed(checkout) : checkout;
    }

    private JsonObject Signed(UcpCheckout checkout) =>
        MerchantAuthorization.Sign(JsonSerializer.SerializeToNode(checkout)!.AsObject(), signer);

    private static UcpErrorResponse Error(string code, string content, string severity) => new()
    {
        Ucp = new UcpEnvelopeWithStatus { Version = UcpSpecification.Version },
        Messages = [new UcpCheckoutMessage { Type = "error", Code = code, Content = content, Severity = severity }],
    };
}
