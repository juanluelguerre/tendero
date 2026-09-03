using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>
/// Why it is coming back. A **closed** set of five, and the closure is the
/// point: this enum is the input to the return-reason analysis a later phase
/// promises, and free text would make that analysis a language model guessing at
/// what a shopper typed. Five buckets a person can pick from is a dataset; a
/// text box is a corpus.
/// </summary>
public enum ReturnReason
{
    /// <summary>The commonest reason in apparel by a distance, and the one
    /// better sizing information prevents.</summary>
    WrongSize,

    /// <summary>It is not what the page said it was — the gap the product
    /// reasoning layer exists to close.</summary>
    NotAsDescribed,

    Damaged,

    /// <summary>The wrong thing arrived. A warehouse problem, not a catalogue
    /// one, and separating the two is what makes either number actionable.</summary>
    WrongItem,

    /// <summary>No reason beyond changing their mind. Legitimate, common, and
    /// the one a shop cannot fix.</summary>
    ChangedMind
}

public enum ReturnStatus
{
    Requested,

    /// <summary>The shop said yes; the parcel has not arrived.</summary>
    Approved,

    /// <summary>The goods are back on the premises. This is what restocks —
    /// approval alone must never put stock back on the shelf.</summary>
    Received,

    Refunded,
    Rejected,

    /// <summary>The shopper changed their mind about changing their mind.</summary>
    Cancelled
}

/// <summary>
/// One line coming back. The SKU travels because inventory speaks in SKUs, and
/// the quantity because returning two of three is normal.
/// </summary>
public sealed record ReturnLine(
    VariantId VariantId,
    string Sku,
    int Quantity,
    ReturnReason Reason,
    string? Comment);

public sealed record ReturnRequested(
    ReturnRequestId ReturnId, OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ReturnApproved(
    ReturnRequestId ReturnId, OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ReturnRejected(
    ReturnRequestId ReturnId, OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// The goods are back. **This is the event that restocks**, and it is separate
/// from approval on purpose: a shop that put stock back when it said yes would
/// be selling parcels that are still in the post.
/// </summary>
public sealed record ReturnReceived(
    ReturnRequestId ReturnId, OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ReturnRefunded(
    ReturnRequestId ReturnId, OrderId OrderId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Goods coming back, as its own aggregate.
///
/// **Returns are not a state of the order**, and that is the phase's load-bearing
/// modelling decision. `Order.AllowedTransitions` has seven states and twelve
/// edges and is readable at a glance — legibility this repository has already
/// paid for. Adding `Returned`, `PartiallyReturned`, `RefundPending`, `Refunded`
/// and `RefundFailed` roughly doubles it, and then does not work anyway: returns
/// are **per line**, and an order-level machine cannot express "two of the three
/// shirts came back" without a combinatorial explosion.
///
/// The window opens on `OrderDelivered`, which is not an invention. That event
/// was added in phase 1 with a comment saying it existed precisely so the return
/// loop would have something to hang from; this slice cashes a design that was
/// already made.
/// </summary>
public sealed class ReturnRequest : AggregateRoot
{
    private static readonly Dictionary<ReturnStatus, ReturnStatus[]> AllowedTransitions = new()
    {
        [ReturnStatus.Requested] = [ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.Cancelled],
        [ReturnStatus.Approved]  = [ReturnStatus.Received, ReturnStatus.Cancelled],

        // Rejected from Received too: the parcel arrived and the goods are not
        // what was described, or are damaged in a way the reason did not claim.
        // Refusing after inspection is the whole reason receiving and refunding
        // are two steps.
        [ReturnStatus.Received]  = [ReturnStatus.Refunded, ReturnStatus.Rejected],

        [ReturnStatus.Refunded]  = [],
        [ReturnStatus.Rejected]  = [],
        [ReturnStatus.Cancelled] = []
    };

    /// <summary>
    /// How long after delivery a return may be asked for. Fourteen days is the
    /// EU statutory minimum for distance selling, which makes it the one number
    /// here that is not a preference.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private readonly List<ReturnLine> _lines = [];

    public ReturnRequestId Id { get; private set; }
    public OrderId OrderId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public ReturnStatus Status { get; private set; }

    /// <summary>Why the SHOP said no. Distinct from the reasons on the lines,
    /// which are why the shopper sent it back.</summary>
    public string? Resolution { get; private set; }

    public Money? RefundAmount { get; private set; }
    public string? RefundReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ReturnLine> Lines => _lines;

    private ReturnRequest() { } // EF Core

    /// <summary>
    /// Opens a return against a delivered order.
    ///
    /// It takes the order rather than an id because every check needs it: only a
    /// delivered order can be returned, only within the window, and only for
    /// lines and quantities it actually contained. Passing an id and trusting
    /// the caller would let a shopper return three of a shirt they bought one of.
    /// </summary>
    public static ReturnRequest Open(
        TimeProvider clock, Order order, IReadOnlyList<ReturnLine> lines)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status != OrderStatus.Delivered)
            throw new InvalidOperationException(
                $"Order {order.Id} is {order.Status}; only a delivered order can be returned.");

        if (lines.Count == 0)
            throw new InvalidOperationException("A return needs at least one line.");

        if (lines.Any(line => line.Quantity <= 0))
            throw new InvalidOperationException("A return line needs a positive quantity.");

        var bought = order.SkuQuantities().ToDictionary(
            pair => pair.Sku, pair => pair.Quantity, StringComparer.OrdinalIgnoreCase);

        foreach (var group in lines.GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase))
        {
            if (!bought.TryGetValue(group.Key, out var ordered))
                throw new InvalidOperationException($"Order {order.Id} has no line for {group.Key}.");

            var asked = group.Sum(line => line.Quantity);

            if (asked > ordered)
                throw new InvalidOperationException(
                    $"{group.Key}: {asked} asked to return, {ordered} bought.");
        }

        var now = clock.GetUtcNow();

        var request = new ReturnRequest
        {
            Id = ReturnRequestId.New(),
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Status = ReturnStatus.Requested,
            CreatedAt = now,
            UpdatedAt = now
        };

        request._lines.AddRange(lines);
        request.Raise(new ReturnRequested(request.Id, order.Id, now));

        return request;
    }

    /// <summary>
    /// Whether the window is still open at a given instant.
    ///
    /// A static question about an ORDER rather than a method on the request,
    /// because the caller asking is deciding whether one may be created — at
    /// which point there is nothing to ask.
    /// </summary>
    public static bool WindowIsOpen(Order order, DateTimeOffset at) =>
        order.Status == OrderStatus.Delivered && at <= order.UpdatedAt + Window;

    public void Approve(TimeProvider clock) =>
        TransitionTo(clock, ReturnStatus.Approved, now => new ReturnApproved(Id, OrderId, now));

    public void Reject(TimeProvider clock, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Resolution = reason;
        TransitionTo(clock, ReturnStatus.Rejected, now => new ReturnRejected(Id, OrderId, reason, now));
    }

    public void Cancel(TimeProvider clock) => TransitionTo(clock, ReturnStatus.Cancelled, null);

    /// <summary>The parcel arrived. This is what restocks — see
    /// <see cref="ReturnReceived"/>.</summary>
    public void Receive(TimeProvider clock) =>
        TransitionTo(clock, ReturnStatus.Received, now => new ReturnReceived(Id, OrderId, now));

    /// <summary>
    /// The money went back. The amount and the provider's reference are recorded
    /// here rather than being recomputed later: what was refunded is a fact, and
    /// re-deriving it from an order whose totals may since have been read
    /// differently is how a credit note stops matching a bank statement.
    /// </summary>
    public void Refund(TimeProvider clock, Money amount, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (amount.IsNegative || amount.IsZero)
            throw new InvalidOperationException("A refund needs a positive amount.");

        RefundAmount = amount;
        RefundReference = reference;

        TransitionTo(clock, ReturnStatus.Refunded, now => new ReturnRefunded(Id, OrderId, amount, now));
    }

    /// <summary>
    /// What the shop owes back for these lines, computed from the ORDER's frozen
    /// prices.
    ///
    /// Line net of discount, and shipping is not refunded — a declared
    /// simplification, and the one most likely to be wrong for a given
    /// jurisdiction. Stating it here beats discovering it in an invoice: the
    /// order's own discount total is apportioned across the returned lines by
    /// their share of the subtotal, which is the same largest-remainder problem
    /// `Money.Allocate` already solves for order-level discounts.
    /// </summary>
    public Money RefundableFrom(Order order)
    {
        var currency = order.Currency;

        var returned = _lines.Aggregate(Money.Zero(currency), (sum, line) =>
        {
            var ordered = order.Lines.FirstOrDefault(candidate =>
                string.Equals(candidate.Sku, line.Sku, StringComparison.OrdinalIgnoreCase));

            return ordered is null ? sum : sum + ordered.UnitPrice * line.Quantity;
        });

        if (returned.IsZero || order.Totals.Subtotal.IsZero)
            return returned.Round(Rounding.AwayFromZero);

        // The share of the order-level discount and of the tax that these lines
        // carried. Proportional to the subtotal, because that is what the
        // promotion engine allocated against in the first place.
        var share = returned.Amount / order.Totals.Subtotal.Amount;

        var discount = order.Totals.DiscountTotal * share;
        var tax = order.Totals.TaxTotal * share;

        return (returned - discount + tax).Round(Rounding.AwayFromZero);
    }

    private void TransitionTo(
        TimeProvider clock, ReturnStatus target, Func<DateTimeOffset, IDomainEvent>? eventFactory)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"Illegal transition {Status} -> {target} for return {Id}.");

        Status = target;
        UpdatedAt = clock.GetUtcNow();

        // Cancelling raises nothing: nobody downstream acts on a shopper
        // changing their mind before the parcel moved. The nullable factory is
        // the one Order had and lost, and it earns its place here.
        if (eventFactory is not null)
            Raise(eventFactory(UpdatedAt));
    }
}
