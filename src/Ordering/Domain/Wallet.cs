using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>A saved way to pay's identity inside its wallet: what the page names to remove it or make it the default.</summary>
public readonly record struct SavedMethodId(Guid Value)
{
    public static SavedMethodId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A card as the provider kept it: its token, and what may be shown — the brand,
/// the last four digits, the month and year it expires. The domain's own copy of
/// what the payment port answers, so the aggregate depends on the kernel alone.
/// </summary>
public sealed record CardOnFile(string Token, string Brand, string Last4, int ExpiryMonth, int ExpiryYear);

/// <summary>
/// One saved way to pay. <see cref="Token"/> is a credential to charge somebody's
/// card: never logged, never sent to a browser, never in an audit row.
/// </summary>
public sealed record SavedMethod(
    SavedMethodId Id,
    string ProviderKey,
    string Token,
    string Brand,
    string Last4,
    int ExpiryMonth,
    int ExpiryYear,
    DateTimeOffset SavedAt)
{
    /// <summary>Good through the last day of its expiry month; expired from the first of the next.</summary>
    public bool HasExpiredAt(DateTimeOffset now) =>
        now.Year > ExpiryYear || (now.Year == ExpiryYear && now.Month > ExpiryMonth);
}

/// <summary>A token the provider has still to forget: the method is gone from the wallet, the credential not yet from the provider.</summary>
public sealed record TokenToForget(string ProviderKey, string Token, DateTimeOffset Since);

/// <summary>
/// A signed-in customer's saved ways to pay (P15-4, docs/specs/shopper-account.md).
///
/// **In Ordering, because Ordering owns the payment port**: an Accounts that held
/// payment tokens would have to reference it, which is the wrong direction (decision
/// 5). Keyed by the customer, as the address book is in Accounts.
///
/// **A token outlives nothing.** Removing a method asks the provider to forget it
/// first; when the provider cannot be reached, the method is set aside — gone from
/// the wallet and from checkout at once — and the token waits in
/// <see cref="ToForget"/> for a worker to try again. The shop never keeps a card
/// number, a CVC or a full expiry date: what is here is what the provider handed
/// back.
///
/// **A default removed leaves none**, and the first method is not made one by
/// itself, for the reason the address book gives: a default is chosen.
/// </summary>
public sealed class Wallet : AggregateRoot
{
    /// <summary>Laboratory scale: a wallet, not a card collection.</summary>
    public const int MaxMethods = 5;

    private List<SavedMethod> methods = [];
    private List<TokenToForget> toForget = [];

    public CustomerId CustomerId { get; private set; }

    /// <summary>Oldest first: the order they were kept in.</summary>
    public IReadOnlyList<SavedMethod> Methods => this.methods;

    /// <summary>Tokens of methods removed whose provider has not yet confirmed forgetting them.</summary>
    public IReadOnlyList<TokenToForget> ToForget => this.toForget;

    public SavedMethodId? Default { get; private set; }

    public bool IsFull => this.methods.Count >= MaxMethods;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Wallet() { } // EF Core

    public static Wallet StartFor(CustomerId customer, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new Wallet { CustomerId = customer, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>A live method by its id; null for one that is not here, or was set aside to be forgotten.</summary>
    public SavedMethod? Find(SavedMethodId id) => this.methods.Find(method => method.Id == id);

    /// <summary>
    /// A card the provider kept, added last. Throws when the wallet is full — a
    /// handler refuses before it asks the provider to keep anything, so reaching it
    /// is a bug, not a request.
    /// </summary>
    public SavedMethod Add(TimeProvider clock, string providerKey, CardOnFile card, bool makeDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(card.Token);

        if (IsFull)
            throw new InvalidOperationException($"A wallet holds {MaxMethods} ways to pay at most.");

        var now = clock.GetUtcNow();
        var method = new SavedMethod(
            SavedMethodId.New(), providerKey, card.Token, card.Brand, card.Last4, card.ExpiryMonth, card.ExpiryYear, now);

        this.methods.Add(method);
        if (makeDefault)
            Default = method.Id;
        UpdatedAt = now;

        return method;
    }

    /// <summary>The method made the default, replacing whichever was. False when there is no such method.</summary>
    public bool UseByDefault(TimeProvider clock, SavedMethodId id)
    {
        if (Find(id) is null)
            return false;

        Default = id;
        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>
    /// The method removed after its provider forgot the token, and with it the
    /// default it was; nothing takes its place. Answers what was removed, or null
    /// when there was nothing to remove.
    /// </summary>
    public SavedMethod? Remove(TimeProvider clock, SavedMethodId id)
    {
        if (Find(id) is not { } method)
            return null;

        this.methods.Remove(method);
        if (Default == id)
            Default = null;
        UpdatedAt = clock.GetUtcNow();

        return method;
    }

    /// <summary>
    /// The provider could not be reached to forget it: the method leaves the wallet
    /// now, so it is never offered again, and its token waits to be forgotten.
    /// False when there is no such method.
    /// </summary>
    public bool SetAsideForForgetting(TimeProvider clock, SavedMethodId id)
    {
        if (Remove(clock, id) is not { } method)
            return false;

        this.toForget.Add(new TokenToForget(method.ProviderKey, method.Token, clock.GetUtcNow()));
        return true;
    }

    /// <summary>The provider confirmed it: the token is dropped from the wallet too.</summary>
    public void Forgotten(TimeProvider clock, string token)
    {
        if (this.toForget.RemoveAll(waiting => waiting.Token == token) > 0)
            UpdatedAt = clock.GetUtcNow();
    }
}
