using System.Text.Json.Serialization;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Negotiation;

namespace ElGuerre.Tendero.Ucp.Checkout;

/// <summary>What an operation answers, before a transport spells it: an HTTP status and a body, or a protocol error.</summary>
public sealed record UcpAnswer(int Status, object Body);

/// <summary>UCP's protocol error body (§Error Codes): not a checkout, and not a business outcome.</summary>
public sealed record UcpProtocolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// Checkout's behaviour, once, for both transports — the rule the catalogue set
/// (<see cref="Catalog.UcpCatalogOperations"/>): a transport marshals and maps
/// errors; negotiation, dispatch and translation are here.
/// </summary>
public static class UcpCheckoutOperations
{
    /// <summary>
    /// The capabilities active for this caller: its platform profile, fetched from a
    /// trusted host, intersected with what the shop declares. No checkout without a
    /// profile — negotiation is how an agent and a shop agree on a shape at all.
    /// </summary>
    public static async Task<(IReadOnlySet<string>? Active, UcpAnswer? Refusal)> NegotiateAsync(
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
        return (active.Where(name => name is CheckoutCapability.Name or FulfillmentCapability.Name or DiscountCapability.Name).ToHashSet(StringComparer.Ordinal), null);
    }

    public static async Task<UcpAnswer> CreateAsync(
        ICommandDispatcher dispatcher, UcpCheckoutRequest request, string culture, IReadOnlySet<string> active, UcpLinks links, CancellationToken cancellationToken) =>
        Answer(await dispatcher.SendAsync(new OpenCheckoutSessionCommand(
            UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), active), culture), cancellationToken), active, links, created: true);

    public static async Task<UcpAnswer> UpdateAsync(
        ICommandDispatcher dispatcher, Guid id, UcpCheckoutRequest request, string culture, IReadOnlySet<string> active, UcpLinks links, CancellationToken cancellationToken) =>
        Answer(await dispatcher.SendAsync(new UpdateCheckoutSessionCommand(
            new CheckoutSessionId(id), UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), active), culture), cancellationToken), active, links);

    public static async Task<UcpAnswer> GetAsync(
        IQueryDispatcher dispatcher, Guid id, string culture, IReadOnlySet<string> active, UcpLinks links, CancellationToken cancellationToken) =>
        Answer(await dispatcher.SendAsync(new GetCheckoutSessionQuery(new CheckoutSessionId(id), culture), cancellationToken), active, links);

    /// <summary>
    /// Completion: exactly one instrument, from a handler this shop offered. The
    /// sandbox handler's credential token names the fake provider's instrument.
    /// </summary>
    public static async Task<UcpAnswer> CompleteAsync(
        ICommandDispatcher dispatcher, Guid id, UcpCompleteRequest request, string? idempotencyKey, string culture,
        IReadOnlySet<string> active, UcpLinks links, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return new UcpAnswer(400, new UcpProtocolError("idempotency_key_required", "Completing a checkout needs an Idempotency-Key: a retry must not buy twice."));

        var instruments = request.Payment?.Instruments ?? [];
        if (instruments.Count != 1)
            return new UcpAnswer(400, new UcpProtocolError("invalid_request", "Complete takes exactly one payment instrument."));

        var instrument = instruments[0];
        if (instrument.HandlerId != UcpCheckoutMapper.SandboxHandlerId || string.IsNullOrWhiteSpace(instrument.Credential?.Token))
            return new UcpAnswer(400, new UcpProtocolError("invalid_request", $"The instrument must come from the handler '{UcpCheckoutMapper.SandboxHandlerId}' with a credential token."));

        return Answer(await dispatcher.SendAsync(new CompleteCheckoutSessionCommand(
            new CheckoutSessionId(id), instrument.Credential!.Token!, idempotencyKey, culture), cancellationToken), active, links);
    }

    public static async Task<UcpAnswer> CancelAsync(
        ICommandDispatcher dispatcher, Guid id, string culture, IReadOnlySet<string> active, UcpLinks links, CancellationToken cancellationToken) =>
        Answer(await dispatcher.SendAsync(new CancelCheckoutSessionCommand(new CheckoutSessionId(id), culture), cancellationToken), active, links);

    /// <summary>What was not negotiated is not read: no fulfillment extension, no destination; no discount extension, no codes.</summary>
    private static CheckoutDetails Allowed(CheckoutDetails details, IReadOnlySet<string> active) => details with
    {
        Destination = active.Contains(FulfillmentCapability.Name) ? details.Destination : null,
        ShippingOptionCode = active.Contains(FulfillmentCapability.Name) ? details.ShippingOptionCode : null,
        Coupons = active.Contains(DiscountCapability.Name) ? details.Coupons : [],
    };

    private static UcpAnswer Answer(CheckoutSessionResult result, IReadOnlySet<string> active, UcpLinks links, bool created = false) => result.Outcome switch
    {
        CheckoutSessionOutcome.Ok => new UcpAnswer(created ? 201 : 200, UcpCheckoutMapper.ToWire(result.Session!, active, links)),
        CheckoutSessionOutcome.NotReady or CheckoutSessionOutcome.NotPlaced => new UcpAnswer(200, UcpCheckoutMapper.ToWire(result.Session!, active, links)),
        CheckoutSessionOutcome.NotFound => new UcpAnswer(404, Error("not_found", "There is no checkout with that id for this agent.", "unrecoverable")),
        CheckoutSessionOutcome.NotOpen => new UcpAnswer(409, Error("checkout_not_open", "This checkout is completed, canceled or expired: nothing in it changes.", "unrecoverable")),
        CheckoutSessionOutcome.NothingPurchasable => new UcpAnswer(200, Error("item_unavailable", "None of the items can be bought right now.", "unrecoverable")),
        _ => new UcpAnswer(403, Error("unauthorized", "Only an agent opens a UCP checkout.", "unrecoverable")),
    };

    private static UcpErrorResponse Error(string code, string content, string severity) => new()
    {
        Ucp = new UcpEnvelopeWithStatus { Version = UcpSpecification.Version },
        Messages = [new UcpCheckoutMessage { Type = "error", Code = code, Content = content, Severity = severity }],
    };
}
