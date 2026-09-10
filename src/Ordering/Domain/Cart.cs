using System.Buffers.Text;
using System.Security.Cryptography;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

public enum CartStatus
{
    Open,

    /// <summary>Converted into an order. A cart is never reopened: the order is
    /// the record of what happened to it.</summary>
    CheckedOut,

    /// <summary>It expired, or the shopper emptied it. It is kept rather than
    /// deleted because an abandoned cart is the thing that leaves a stock
    /// reservation behind, and the sweep needs something to read.</summary>
    Abandoned
}

/// <summary>
/// A line in a cart: what, and how many. **No price.**
///
/// That omission is the design. Prices are quoted live and frozen at order time
/// (ADR 0016), so a cart that stored a price would be a cart that lies the
/// moment a promotion starts or a tariff changes — and worse, it would be
/// tempting to total it. Everything monetary on a cart screen comes from
/// `POST /api/pricing/quote`, recomputed on every change.
///
/// What it does carry is a **display snapshot**: the product name and variant
/// label as the shopper saw them when they added it. Those are text, not money,
/// and re-resolving them on every render would mean a catalogue edit silently
/// rewriting what somebody put in their basket.
/// </summary>
public sealed record CartLine(
    ProductId ProductId,
    VariantId VariantId,
    string Sku,
    string ProductName,
    string? VariantLabel,
    string? ImageId,
    int Quantity)
{
    public CartLine WithQuantity(int quantity) => this with { Quantity = quantity };
}

/// <summary>
/// What somebody intends to buy, before they have.
///
/// **It is an aggregate inside `Ordering`, not a context of its own.** Cart to
/// order is one transactional conversion, and splitting it would buy a
/// distributed saga to move a row from one state to another. But a `Cart` is
/// emphatically not an `Order`: it has its own lifecycle, it can be anonymous,
/// it expires, and it never touches `Order.AllowedTransitions`.
///
/// **Guests are first class.** `CustomerId` is nullable and a `Token` addresses
/// the cart instead — which is what let the shop work before phase 7 existed,
/// and what an agent surface needs anyway: WebMCP inherits whatever session the
/// browser already had, guest included.
/// </summary>
public sealed class Cart : AggregateRoot
{
    /// <summary>
    /// Three states and two edges, written as a table because that is the idiom
    /// `Order` and `Reservation` already established. It is small enough to feel
    /// like ceremony and it is the reason a fourth state cannot be added by
    /// accident.
    /// </summary>
    private static readonly TransitionTable<CartStatus> AllowedTransitions = new()
    {
        [CartStatus.Open] = [CartStatus.CheckedOut, CartStatus.Abandoned],
        [CartStatus.CheckedOut] = [],
        [CartStatus.Abandoned] = []
    };

    /// <summary>
    /// How long an untouched cart lives. Long enough to come back tomorrow,
    /// short enough that the reservation sweep has something to act on — and it
    /// is refreshed on every change, so the clock measures neglect rather than
    /// age.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// A cart holds few things on purpose. It is not a wishlist, and an
    /// unbounded one is a denial-of-service surface on an endpoint that has to
    /// stay anonymous.
    /// </summary>
    public const int MaximumLines = 50;

    /// <summary>Per line. Six products in the catalogue; nobody needs 400 pans,
    /// and the number that stops a typo is the same number that stops abuse.</summary>
    public const int MaximumQuantity = 99;

    private readonly List<CartLine> lines = [];

    public CartId Id { get; private set; }

    /// <summary>
    /// Null until somebody signs in. Phase 7's `LinkIdentity` is the other half
    /// of this: linking or merging the account is an `Accounts` operation, and
    /// <see cref="Claim"/> is all `Ordering` owns of it.
    /// </summary>
    public CustomerId? CustomerId { get; private set; }

    /// <summary>
    /// The bearer credential a guest's browser holds. Whoever presents it sees
    /// the cart, so it is 256 bits from a cryptographic RNG rather than a GUID:
    /// a cart is not sensitive enough for a session, and it is far too sensitive
    /// for something sequential and guessable.
    /// </summary>
    public string Token { get; private set; } = default!;

    public string Culture { get; private set; } = "es";

    /// <summary>
    /// Fixed when the first line lands, and enforced from then on. One cart, one
    /// currency — the same rule `Order.Place` states, checked here so a shopper
    /// hears it while they can still act on it rather than at checkout.
    /// </summary>
    public string Currency { get; private set; } = default!;

    public CartStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public IReadOnlyList<CartLine> Lines => this.lines;

    public int ItemCount => this.lines.Sum(line => line.Quantity);

    public bool IsEmpty => this.lines.Count == 0;

    private Cart() { } // EF Core

    public static Cart Start(TimeProvider clock, string culture, string currency, CustomerId? customerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var now = clock.GetUtcNow();

        return new Cart
        {
            Id = CartId.New(),
            CustomerId = customerId,
            Token = NewToken(),
            Culture = SharedKernel.Culture.Normalize(culture),
            Currency = currency,
            Status = CartStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + Lifetime
        };
    }

    /// <summary>
    /// Adds a line, or adds to the one that is already there.
    ///
    /// Adding the same SKU twice is one line with more of it, and not two lines.
    /// That is not tidiness: `QuoteCart` refuses a repeated SKU outright,
    /// because a quote whose lines do not match the caller's could never have
    /// its fingerprint verified at checkout.
    /// </summary>
    public void Add(TimeProvider clock, CartLine line)
    {
        EnsureOpen();

        if (line.Quantity <= 0)
            throw new InvalidOperationException("A line needs a positive quantity.");

        var existing = this.lines.FindIndex(candidate => Same(candidate.Sku, line.Sku));

        if (existing >= 0)
        {
            SetQuantityAt(existing, this.lines[existing].Quantity + line.Quantity);
        }
        else
        {
            if (this.lines.Count >= MaximumLines)
                throw new InvalidOperationException($"A cart takes at most {MaximumLines} lines.");

            if (line.Quantity > MaximumQuantity)
                throw new InvalidOperationException(
                    $"A line takes at most {MaximumQuantity} units; {line.Sku} asked for {line.Quantity}.");

            this.lines.Add(line);
        }

        Touch(clock);
    }

    /// <summary>
    /// Sets a line's quantity outright. Zero removes it — which is what a "-"
    /// pressed at one unit means, and asking the interface to call a different
    /// operation for the last one is how off-by-one bugs get written.
    /// </summary>
    public void SetQuantity(TimeProvider clock, string sku, int quantity)
    {
        EnsureOpen();

        var index = this.lines.FindIndex(line => Same(line.Sku, sku));

        if (index < 0)
            throw new InvalidOperationException($"There is no line for {sku} in this cart.");

        SetQuantityAt(index, quantity);
        Touch(clock);
    }

    public void Remove(TimeProvider clock, string sku) => SetQuantity(clock, sku, 0);

    /// <summary>
    /// A guest signed in. The cart becomes theirs; the token keeps working,
    /// because taking it away mid-session would empty the basket of anybody
    /// whose sign-in did not complete.
    /// </summary>
    public void Claim(TimeProvider clock, CustomerId customerId)
    {
        EnsureOpen();

        if (CustomerId is { } owner && owner != customerId)
            throw new InvalidOperationException(
                $"Cart {Id} already belongs to {owner}; merging two carts is an Accounts operation.");

        CustomerId = customerId;
        Touch(clock);
    }

    /// <summary>The shopper's language changed. It is stored because a cart
    /// converted into an order freezes the culture the order keeps.</summary>
    public void UseCulture(TimeProvider clock, string culture)
    {
        EnsureOpen();

        Culture = SharedKernel.Culture.Normalize(culture);
        Touch(clock);
    }

    /// <summary>
    /// It became an order. There is no `CartCheckedOut` event, and that is
    /// deliberate: the conversion is one transaction in one handler, so nothing
    /// downstream needs telling. `OrderPlaced` is the fact the rest of the
    /// system reacts to — raising a second event for the same moment would give
    /// the outbox two ways to describe it.
    /// </summary>
    public void MarkCheckedOut(TimeProvider clock)
    {
        if (IsEmpty)
            throw new InvalidOperationException("An empty cart cannot become an order.");

        TransitionTo(clock, CartStatus.CheckedOut);
    }

    public void Abandon(TimeProvider clock) => TransitionTo(clock, CartStatus.Abandoned);

    public bool HasExpiredAt(DateTimeOffset at) => Status == CartStatus.Open && at >= ExpiresAt;

    /// <summary>
    /// The lines as pricing wants them: SKU and quantity, in a stable order.
    ///
    /// Ordered by SKU because the quote's fingerprint is computed over them, and
    /// a fingerprint that changed when two lines swapped places would reject a
    /// checkout for no reason a shopper could understand.
    /// </summary>
    public IReadOnlyList<(string Sku, int Quantity)> PricingLines() =>
    [
        .. this.lines
            .OrderBy(line => line.Sku, StringComparer.Ordinal)
            .Select(line => (line.Sku, line.Quantity))
    ];

    private void SetQuantityAt(int index, int quantity)
    {
        if (quantity < 0)
            throw new InvalidOperationException("A quantity cannot be negative.");

        if (quantity == 0)
        {
            this.lines.RemoveAt(index);
            return;
        }

        if (quantity > MaximumQuantity)
            throw new InvalidOperationException(
                $"A line takes at most {MaximumQuantity} units; {this.lines[index].Sku} asked for {quantity}.");

        this.lines[index] = this.lines[index].WithQuantity(quantity);
    }

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();

        // Expiry measures neglect, not age: every change buys another week.
        ExpiresAt = UpdatedAt + Lifetime;
    }

    private void EnsureOpen()
    {
        if (Status != CartStatus.Open)
            throw new InvalidOperationException($"Cart {Id} is {Status} and cannot be changed.");
    }

    private void TransitionTo(TimeProvider clock, CartStatus target)
    {
        AllowedTransitions.EnsureAllowed(Status, target, $"cart {Id}");

        Status = target;
        UpdatedAt = clock.GetUtcNow();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// URL-safe, unpadded, 256 bits. It travels in a header and in browser
    /// storage, so `+` and `/` would need escaping in both.
    /// </summary>
    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
