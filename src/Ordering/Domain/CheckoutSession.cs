using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>
/// What is stored of a session's life. Three states, not UCP's six: the other
/// three — <c>incomplete</c>, <c>ready_for_complete</c>, <c>requires_escalation</c>
/// — are **derived** from what the session holds and what the shop can settle, so
/// they cannot drift from the facts they describe (P11-1).
/// </summary>
public enum CheckoutSessionStatus
{
    Open,
    Completed,
    Canceled,
}

/// <summary>Who is buying, as the agent said. Every field optional until completion asks for an email.</summary>
public sealed record CheckoutBuyer(string? FirstName, string? LastName, string? Email, string? Phone)
{
    public static readonly CheckoutBuyer Nobody = new(null, null, null, null);

    /// <summary>The name a parcel is addressed to when the destination names nobody.</summary>
    public string? FullName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } name ? name : null;
}

/// <summary>What a session still lacks before it can be completed — each one a message the agent can act on.</summary>
public enum CheckoutGap
{
    Lines,
    BuyerEmail,
    Destination,
    ShippingOption,
}

public sealed record CheckoutSessionCompleted(CheckoutSessionId SessionId, OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CheckoutSessionCanceled(CheckoutSessionId SessionId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A UCP checkout session (P11-1): what an agent is buying, for whom, sent where
/// and how — the state the protocol makes the business remember between calls,
/// which a cart has no place for.
///
/// **It owns a cart** for its lines, so a session inherits the cart's line rules
/// instead of restating them, and the order is placed from that cart by the same
/// <see cref="OrderPlacement"/> the storefront uses. **Prices are never stored**
/// (ADR 0016): the only number here is the fingerprint of the quote last shown,
/// which completion compares against a fresh one. **The mode is the credential's**
/// (ADR 0038), fixed when the session opens, like the order it will place.
/// </summary>
public sealed class CheckoutSession : AggregateRoot
{
    /// <summary>UCP's suggested default: long enough to finish a purchase, short enough not to hold anything for nobody.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private static readonly TransitionTable<CheckoutSessionStatus> AllowedTransitions = new()
    {
        [CheckoutSessionStatus.Open] = [CheckoutSessionStatus.Completed, CheckoutSessionStatus.Canceled],
        [CheckoutSessionStatus.Completed] = [],
        [CheckoutSessionStatus.Canceled] = [],
    };

    private readonly List<string> coupons = [];

    public CheckoutSessionId Id { get; private set; }

    /// <summary>The agent that opened it: the only principal that may read or change it.</summary>
    public AgentId Owner { get; private set; }

    public CartId CartId { get; private set; }

    public OrderMode Mode { get; private set; }

    public string Culture { get; private set; } = "es";

    public CheckoutSessionStatus Status { get; private set; }

    public CheckoutBuyer Buyer { get; private set; } = CheckoutBuyer.Nobody;

    /// <summary>Where it goes, once the agent has said.</summary>
    public Address? Destination { get; private set; }

    /// <summary>The shipping option chosen, by the code the rate provider offered.</summary>
    public string? ShippingOptionCode { get; private set; }

    /// <summary>The discount codes submitted, as submitted: UCP replaces the list on every update.</summary>
    public IReadOnlyList<string> Coupons => this.coupons;

    /// <summary>The fingerprint of the last quote the agent was shown (ADR 0016) — what completion holds it to.</summary>
    public string? ShownQuoteHash { get; private set; }

    public OrderId? OrderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    private CheckoutSession() { } // EF Core

    public static CheckoutSession Open(TimeProvider clock, AgentId owner, CartId cartId, OrderMode mode, string culture)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Value);

        var now = clock.GetUtcNow();
        return new CheckoutSession
        {
            Id = CheckoutSessionId.New(),
            Owner = owner,
            CartId = cartId,
            Mode = mode,
            Culture = SharedKernel.Culture.Normalize(culture),
            Status = CheckoutSessionStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + Lifetime,
        };
    }

    public bool HasExpiredAt(DateTimeOffset at) => Status == CheckoutSessionStatus.Open && at >= ExpiresAt;

    /// <summary>Only its owner sees a session: to anybody else it does not exist.</summary>
    public bool BelongsTo(CommercePrincipal principal) => principal.Agent == Owner;

    /// <summary>What the agent sent on an update, replacing what the session held — UCP's update is a full replacement.</summary>
    public void Describe(TimeProvider clock, CheckoutBuyer buyer, Address? destination, string? shippingOptionCode, IReadOnlyList<string> coupons, string culture)
    {
        EnsureOpen(clock);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(coupons);

        Buyer = buyer;
        Destination = destination;
        // An option means nothing without somewhere to send it: a new destination
        // may not be offered the old option, and the agent chooses again.
        ShippingOptionCode = destination is null ? null : NullIfBlank(shippingOptionCode);
        this.coupons.Clear();
        this.coupons.AddRange(coupons.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        Culture = SharedKernel.Culture.Normalize(culture);
        Touch(clock);
    }

    /// <summary>The fingerprint of the quote just shown; completion is held to it.</summary>
    public void Showed(string quoteHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteHash);
        ShownQuoteHash = quoteHash;
    }

    /// <summary>What is still missing before the session can be completed, given whether its cart has lines.</summary>
    public IReadOnlyList<CheckoutGap> Gaps(bool hasLines)
    {
        List<CheckoutGap> gaps = [];
        if (!hasLines)
            gaps.Add(CheckoutGap.Lines);
        if (string.IsNullOrWhiteSpace(Buyer.Email))
            gaps.Add(CheckoutGap.BuyerEmail);
        if (Destination is null)
            gaps.Add(CheckoutGap.Destination);
        else if (ShippingOptionCode is null)
            gaps.Add(CheckoutGap.ShippingOption);
        return gaps;
    }

    public void Complete(TimeProvider clock, OrderId orderId)
    {
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, CheckoutSessionStatus.Completed, $"checkout session {Id}");
        Status = CheckoutSessionStatus.Completed;
        OrderId = orderId;
        UpdatedAt = clock.GetUtcNow();
        Raise(new CheckoutSessionCompleted(Id, orderId, UpdatedAt));
    }

    public void Cancel(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, CheckoutSessionStatus.Canceled, $"checkout session {Id}");
        Status = CheckoutSessionStatus.Canceled;
        UpdatedAt = clock.GetUtcNow();
        Raise(new CheckoutSessionCanceled(Id, UpdatedAt));
    }

    private void EnsureOpen(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status != CheckoutSessionStatus.Open)
            throw new InvalidOperationException($"Checkout session {Id} is {Status}; only an open session changes.");
    }

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();
        ExpiresAt = UpdatedAt + Lifetime;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
