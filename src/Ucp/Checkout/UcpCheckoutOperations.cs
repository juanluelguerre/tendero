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
    ICommandDispatcher commands, IQueryDispatcher queries, IMerchantSigner signer, CheckoutMandates mandates,
    IIdempotencyStore keys, IPrincipalAccessor principals)
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

        // The protocol version first, on every request (§Request-Time Validation):
        // the shop declares one version and no `supported_versions`, so any other
        // is refused before a capability is compared — across versions the same
        // name is not the same shape.
        if (profile.Version != UcpSpecification.Version)
            return (null, new UcpAnswer(422, new UcpProtocolError("version_unsupported",
                $"Protocol version {profile.Version} is not supported. This shop implements version {UcpSpecification.Version}.")));

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

    public Task<UcpAnswer> CreateAsync(UcpCheckoutRequest request, string? idempotencyKey, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        OnceAsync("create_checkout", null, idempotencyKey, request, cancellationToken, async () =>
            Kept(await commands.SendAsync(new OpenCheckoutSessionCommand(
                UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), call.Active), culture), cancellationToken), call, created: true));

    public Task<UcpAnswer> UpdateAsync(Guid id, UcpCheckoutRequest request, string? idempotencyKey, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        OnceAsync("update_checkout", id, idempotencyKey, request, cancellationToken, async () =>
            Kept(await commands.SendAsync(new UpdateCheckoutSessionCommand(
                new CheckoutSessionId(id), UcpCheckoutMapper.Lines(request), Allowed(UcpCheckoutMapper.Details(request), call.Active), culture), cancellationToken), call));

    public async Task<UcpAnswer> GetAsync(Guid id, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        Answer(await queries.SendAsync(new GetCheckoutSessionQuery(new CheckoutSessionId(id), culture), cancellationToken), call);

    /// <summary>
    /// Completion: exactly one instrument, from a handler this shop offered. The
    /// sandbox handler's credential token names the fake provider's instrument.
    /// With AP2 negotiated, a checkout mandate is required, and it has to authorise
    /// this session's terms as they are now.
    ///
    /// The key is asked whose it is **before** the mandate is read: a retry of a
    /// completion that went through arrives at a session that is no longer open,
    /// and has to be answered the order it placed, not told its mandate is for a
    /// checkout that changed.
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

        return await OnceAsync("complete_checkout", id, idempotencyKey, request, cancellationToken, async () =>
        {
            MandateScope? scope = null;
            if (call.Ap2)
            {
                var (refusal, verified) = await VerifyMandateAsync(sessionId, request.Ap2?.CheckoutMandate, culture, call, cancellationToken);
                if (refusal is not null)
                    return (refusal, false);
                scope = verified;
            }

            return Kept(await commands.SendAsync(new CompleteCheckoutSessionCommand(
                sessionId, instrument.Credential!.Token!, idempotencyKey, culture, scope), cancellationToken), call);
        });
    }

    public Task<UcpAnswer> CancelAsync(Guid id, string? idempotencyKey, string culture, UcpCheckoutCall call, CancellationToken cancellationToken) =>
        OnceAsync("cancel_checkout", id, idempotencyKey, Nothing, cancellationToken, async () =>
            Kept(await commands.SendAsync(new CancelCheckoutSessionCommand(new CheckoutSessionId(id), culture), cancellationToken), call));

    /// <summary>
    /// **An operation that changes something, once for its key** (UCP, the REST
    /// binding's *Idempotency-Key*: store the key with the result, give that
    /// result back to a duplicate whose body matches, 409 for one whose body
    /// does not). The request is the operation, its session and its body; the
    /// key is the agent's, so two agents may choose the same one.
    ///
    /// What is kept is what changed something. A refusal changed nothing, so
    /// its key is free: an agent told a checkout is not ready fixes it and
    /// completes with the key it had. And an answer is kept as the JSON it was,
    /// so a signed checkout comes back as the bytes that were signed.
    ///
    /// Without a key the operation simply runs — the specification's *SHOULD*.
    /// Completing is the one that requires it, above.
    /// </summary>
    private async Task<UcpAnswer> OnceAsync(
        string operation, Guid? session, string? key, object request, CancellationToken cancellationToken,
        Func<Task<(UcpAnswer Answer, bool Changed)>> run)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (await run()).Answer;

        if (key.Length > Idempotency.MaximumKeyLength)
            return new UcpAnswer(400, new UcpProtocolError("invalid_request", $"An idempotency key is at most {Idempotency.MaximumKeyLength} characters."));

        var scope = $"ucp:{principals.Current.Agent}";
        var fingerprint = Idempotency.Fingerprint(operation, session?.ToString() ?? "", JsonSerializer.Serialize(request, request.GetType()));

        switch (await keys.ClaimAsync(scope, key, fingerprint, cancellationToken))
        {
            case IdempotencyClaim.Replayed kept:
                return new UcpAnswer(kept.Status, JsonNode.Parse(kept.Body)!);
            case IdempotencyClaim.Mismatched:
                return new UcpAnswer(409, new UcpProtocolError("idempotency_key_reused", "This idempotency key was used for another request: a key names one request, and a new request needs a new key."));
            case IdempotencyClaim.InFlight:
                return new UcpAnswer(409, new UcpProtocolError("idempotency_in_progress", "The request this idempotency key names is still running: ask again for its answer."));
        }

        try
        {
            var (answer, changed) = await run();
            if (changed)
                await keys.CompleteAsync(scope, key, answer.Status, JsonSerializer.Serialize(answer.Body, answer.Body.GetType()), cancellationToken);
            else
                await keys.ReleaseAsync(scope, key, cancellationToken);

            return answer;
        }
        catch
        {
            // Not the request's token: a cancelled request still has to give its key back.
            await keys.ReleaseAsync(scope, key, CancellationToken.None);
            throw;
        }
    }

    /// <summary>The body of an operation that has none: cancelling is its session and nothing else.</summary>
    private static readonly object Nothing = new { };

    /// <summary>An answer, and whether what it reports changed anything — which is what makes it worth keeping under a key.</summary>
    private (UcpAnswer Answer, bool Changed) Kept(CheckoutSessionResult result, UcpCheckoutCall call, bool created = false) =>
        (Answer(result, call, created), result.Outcome == CheckoutSessionOutcome.Ok);

    /// <summary>
    /// The mandate against the session as it is now (UCP §Business Verification):
    /// it verifies, and the checkout it carries is this one with these goods at
    /// these amounts. A refusal is the current checkout — freshly signed, so the
    /// platform can ask the person again — with the one error that says why.
    ///
    /// The session is read and then completed in two steps, so an update landing
    /// between them is not seen here. That is why what verified travels on as a
    /// <see cref="MandateScope"/>: placement checks the approved total again, in
    /// the transaction that places the order, and freezes the scope onto it (P11-5).
    /// </summary>
    private async Task<(UcpAnswer? Refusal, MandateScope? Scope)> VerifyMandateAsync(
        CheckoutSessionId id, string? presentation, string culture, UcpCheckoutCall call, CancellationToken cancellationToken)
    {
        var current = await queries.SendAsync(new GetCheckoutSessionQuery(id, culture), cancellationToken);
        if (current.Outcome != CheckoutSessionOutcome.Ok)
            return (Answer(current, call), null);

        var shown = UcpCheckoutMapper.ToWire(current.Session!, call.Active, call.Links);
        var verdict = mandates.Verify(presentation, call.Negotiated.Platform);
        var code = verdict.Error
                   ?? (UcpCheckoutTerms.Same(verdict.Mandate!.Checkout, Signed(shown)) ? null : Ap2ErrorCodes.MandateScopeMismatch);

        return code is null
            ? (null, ScopeOf(verdict.Mandate!))
            : (new UcpAnswer(200, Signed(UcpCheckoutMapper.ToWire(
                current.Session! with { Issues = [.. current.Session.Issues, new CheckoutIssue(code, "payment")] }, call.Active, call.Links))), null);
    }

    /// <summary>What the person approved, as the value Ordering freezes: the mandated checkout's total, in its currency.</summary>
    internal static MandateScope ScopeOf(VerifiedCheckoutMandate mandate)
    {
        var total = mandate.Checkout["totals"]!.AsArray()
            .Single(entry => entry?["type"]?.GetValue<string>() == "total")!["amount"]!.GetValue<long>();

        var currency = mandate.Checkout["currency"]!.GetValue<string>();

        return new MandateScope(
            mandate.Vct,
            mandate.IssuerKeyId,
            mandate.CheckoutHash,
            new Money(UcpCheckoutMapper.Major(total, currency), currency),
            mandate.ExpiresAt);
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
