using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Domain;

public sealed record CustomerRegistered(CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// What a customer said about marketing (P15-6): given or withdrawn, when, and the
/// version of the wording they said it to — what they agreed to is part of what
/// they agreed. A domain fact, unlike analytics consent, which is the browser's.
/// </summary>
public sealed record MarketingConsent(bool Given, DateTimeOffset At, string Wording);

/// <summary>
/// Where an imported customer came from (phase 16, S2b): the platform and its id
/// for them there. It is what a re-import finds them by, what an imported order is
/// given its owner by, and — kept after an erasure — what stops the next import
/// from bringing back somebody who asked to be forgotten.
/// </summary>
public sealed record CustomerOrigin(string Source, string ExternalId);

/// <summary>A guest turned into somebody with a name. The event carries both
/// facts because "this customer existed before they signed in" is the thing an
/// audit reader wants and cannot reconstruct afterwards.</summary>
public sealed record CustomerIdentified(
    CustomerId CustomerId, string Subject, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Somebody the shop can address, whether or not they have signed in.
///
/// **A guest is a Customer with no <see cref="Subject"/>, not the absence of
/// one.** That is the whole shape of this aggregate: an order placed by somebody
/// who never made an account still belongs to a person, and modelling that
/// person as null spreads the special case into every screen that shows an
/// order. It is the same call `CommercePrincipal.Anonymous` already makes — a
/// value, not a null.
///
/// The <see cref="Subject"/> is the identity provider's `sub`, and linking one
/// is the `LinkExternal` idempotency pattern from `Product` applied to people:
/// a subject is to a customer what an `ExternalReference` is to a product. Sign
/// in twice and nothing happens the second time.
///
/// **No roles here.** Roles live in the token and map to policies (ADR 0017,
/// reserved). A domain that stored one would have two sources of truth for
/// authorisation and would be wrong every time somebody's access changed.
/// </summary>
public sealed class Customer : AggregateRoot
{
    public CustomerId Id { get; private set; }

    /// <summary>The identity provider's `sub`. Null means a guest — somebody
    /// the shop knows about and cannot address by name.</summary>
    public string? Subject { get; private set; }

    public string? DisplayName { get; private set; }

    /// <summary>The language the shop last saw them in. It is stored because a
    /// returning customer should not have to choose twice.</summary>
    public string Culture { get; private set; } = default!;

    /// <summary>
    /// True once they chose <see cref="Culture"/> in their account (P15-6): from then
    /// on a sign-in no longer replaces it with the browser's.
    /// </summary>
    public bool CultureChosen { get; private set; }

    /// <summary>What the shop may send them: null until they say, then what they said, when, and to which wording.</summary>
    public MarketingConsent? Marketing { get; private set; }

    /// <summary>
    /// Their email, lowercase, when the shop has it: an imported customer brings one
    /// (phase 16, S2b). Personal data, cleared by an erasure. Null for a customer
    /// who signed in here, whose email is the identity provider's.
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>Where an imported customer came from; null for one who came to the shop here.</summary>
    public CustomerOrigin? ImportedFrom { get; private set; }

    /// <summary>
    /// When the account was removed (P15-9): the customer is kept, anonymised, so an
    /// order's owner still resolves; null for a customer who was never erased.
    /// </summary>
    public DateTimeOffset? ErasedAt { get; private set; }

    public bool IsErased => ErasedAt is not null;

    /// <summary>Marketing only on a consent given and not withdrawn.</summary>
    public bool MayBeSentMarketing => Marketing is { Given: true };

    /// <summary>
    /// Which tariff applies. It lives here rather than in `Pricing` because
    /// segment is a fact ABOUT a person, and Pricing is a pure function of
    /// values — the whole reason its engine is property-testable.
    /// </summary>
    public string Segment { get; private set; } = default!;

    /// <summary>
    /// The customer this one was merged into, when a guest turned out to be
    /// somebody who already had an account.
    ///
    /// Superseded rather than deleted, for the reason a discontinued variant is
    /// kept: an order placed as the guest still names this id, and rewriting
    /// history to tidy up a merge is how a refund goes to the wrong person.
    /// </summary>
    public CustomerId? SupersededBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsGuest => Subject is null;

    private Customer() { } // EF Core

    /// <summary>Somebody browsing. A `CustomerId` is minted at checkout so an
    /// order has an owner, and this is what it points at.</summary>
    public static Customer Guest(TimeProvider clock, string culture, string segment)
    {
        var now = clock.GetUtcNow();

        var customer = new Customer
        {
            Id = CustomerId.New(),
            Subject = null,
            Culture = Domain.Culture.Normalise(culture),
            Segment = Segments.Normalise(segment),
            CreatedAt = now,
            UpdatedAt = now
        };

        customer.Raise(new CustomerRegistered(customer.Id, now));
        return customer;
    }

    /// <summary>Somebody who signed in and the shop had never seen.</summary>
    public static Customer Identified(
        TimeProvider clock, string subject, string? displayName, string culture, string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var customer = Guest(clock, culture, segment);
        customer.Subject = subject;
        customer.DisplayName = displayName;
        customer.Raise(new CustomerIdentified(customer.Id, subject, customer.CreatedAt));

        return customer;
    }

    /// <summary>
    /// Somebody who bought from a store the merchant is leaving (phase 16, S2b):
    /// their email, the store's id for them, their name, and their marketing
    /// consent exactly as the store recorded it — dated then, and worded as what it
    /// was, a consent given to the store. With the identity provisioned for them
    /// (<paramref name="subject"/>) they sign in as themselves; without one they are
    /// a guest the shop can still address, and their orders still have an owner.
    /// </summary>
    public static Customer Imported(
        TimeProvider clock, CustomerOrigin origin, string email, string? displayName, string culture,
        MarketingConsent? marketing, string? subject)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var customer = subject is null
            ? Guest(clock, culture, Segments.Retail)
            : Identified(clock, subject, displayName, culture, Segments.Retail);

        customer.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        customer.Email = email.Trim().ToLowerInvariant();
        customer.ImportedFrom = origin;
        customer.Marketing = marketing;
        return customer;
    }

    /// <summary>
    /// Somebody the shop already had, found in a store the merchant is leaving by
    /// their email (phase 16, S2b): told where else they came from, so that store's
    /// orders find them. Idempotent; a different id from the same or another store
    /// is refused, because two store customers on one person is two people's orders
    /// landing on one.
    /// </summary>
    public void LinkOrigin(TimeProvider clock, CustomerOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        EnsureCurrent();

        if (ImportedFrom == origin)
            return;

        if (ImportedFrom is { } existing)
            throw new InvalidOperationException(
                $"Customer {Id} already came from {existing.Source} {existing.ExternalId}; it cannot also be {origin.Source} {origin.ExternalId}.");

        ImportedFrom = origin;
        Touch(clock);
    }

    /// <summary>
    /// A guest signed in and it is the same person.
    ///
    /// Idempotent by subject, like every operation in this repository that can
    /// arrive twice: signing in again links nothing and raises nothing. Linking
    /// a DIFFERENT subject is refused, because a customer with two identities is
    /// two people sharing an order history.
    /// </summary>
    public void LinkIdentity(TimeProvider clock, string subject, string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        EnsureCurrent();

        if (Subject == subject)
        {
            // Not a no-op entirely: a display name can change, and refusing to
            // update it would freeze whatever the provider returned the first time.
            if (displayName is not null && displayName != DisplayName)
            {
                DisplayName = displayName;
                Touch(clock);
            }

            return;
        }

        if (Subject is { } existing)
        {
            throw new InvalidOperationException(
                $"Customer {Id} is already {existing}; a customer cannot have two identities.");
        }

        Subject = subject;
        DisplayName = displayName;
        Touch(clock);
        Raise(new CustomerIdentified(Id, subject, UpdatedAt));
    }

    /// <summary>
    /// This guest turned out to be somebody who already had an account.
    ///
    /// The guest is kept and pointed at the survivor rather than deleted,
    /// because orders placed as the guest still name this id — and a merge that
    /// rewrote them is how a refund reaches the wrong person.
    /// </summary>
    public void Supersede(TimeProvider clock, CustomerId survivor)
    {
        EnsureCurrent();

        if (survivor == Id)
            throw new InvalidOperationException("A customer cannot supersede itself.");

        if (!IsGuest)
            throw new InvalidOperationException(
                $"Customer {Id} has an identity of its own and cannot be merged away.");

        SupersededBy = survivor;
        Touch(clock);
    }

    public void UseCulture(TimeProvider clock, string culture)
    {
        var normalised = Domain.Culture.Normalise(culture);
        if (normalised == Culture) return;

        Culture = normalised;
        Touch(clock);
    }

    /// <summary>
    /// The language the shop saw them in at sign-in, kept only until they choose
    /// one (P15-6): a choice made in the account is not overwritten by whichever
    /// browser they signed in from next.
    /// </summary>
    public void ObserveCulture(TimeProvider clock, string culture)
    {
        if (!CultureChosen)
            UseCulture(clock, culture);
    }

    /// <summary>The language the shop speaks to them in when a request names no other, chosen in the account (P15-6).</summary>
    public void ChooseCulture(TimeProvider clock, string culture)
    {
        EnsureSomebody();

        var normalised = Domain.Culture.Normalise(culture);
        if (!CultureNegotiation.Supported.Contains(normalised))
            throw new ArgumentException($"The shop does not speak '{culture}'.", nameof(culture));

        UseCulture(clock, normalised);
        if (CultureChosen) return;

        CultureChosen = true;
        Touch(clock);
    }

    /// <summary>
    /// Marketing given (P15-6): off until now, kept with the date and the wording it
    /// was given to, each change an audit row. Giving again to the same wording
    /// changes nothing; to new wording, it is a new consent.
    /// </summary>
    public void GiveMarketingConsent(TimeProvider clock, string wording) => Consent(clock, given: true, wording);

    /// <summary>Marketing withdrawn: a fact with its own date, not the absence of one.</summary>
    public void WithdrawMarketingConsent(TimeProvider clock, string wording) => Consent(clock, given: false, wording);

    private void Consent(TimeProvider clock, bool given, string wording)
    {
        EnsureSomebody();
        ArgumentException.ThrowIfNullOrWhiteSpace(wording);

        if (Marketing is { } current && current.Given == given && current.Wording == wording)
            return;

        // Withdrawing what was never given is still recorded: the shopper said no, and when.
        Marketing = new MarketingConsent(given, clock.GetUtcNow(), wording);
        Touch(clock);
    }

    /// <summary>
    /// The account removed (P15-9): who they were and what they chose is cleared —
    /// the identity's subject and name, the chosen language, the consent — and the
    /// customer is kept, marked erased, so the orders the law makes the shop keep
    /// still have an owner. A later sign-in with the same identity finds nobody
    /// and starts a new customer; this one cannot be anybody again.
    /// </summary>
    public void Erase(TimeProvider clock)
    {
        EnsureCurrent();

        Subject = null;
        DisplayName = null;
        Email = null;
        CultureChosen = false;
        Marketing = null;
        ErasedAt = clock.GetUtcNow();
        Touch(clock);
    }

    /// <summary>Preferences are a person's: a guest has nobody to keep them for.</summary>
    private void EnsureSomebody()
    {
        EnsureCurrent();
        if (IsGuest)
            throw new InvalidOperationException($"Customer {Id} is a guest and keeps no preferences.");
    }

    public void MoveToSegment(TimeProvider clock, string segment)
    {
        var normalised = Segments.Normalise(segment);
        if (normalised == Segment) return;

        Segment = normalised;
        Touch(clock);
    }

    /// <summary>A superseded customer is history. Letting one be edited would
    /// mean two rows disagreeing about the same person.</summary>
    private void EnsureCurrent()
    {
        if (SupersededBy is { } survivor)
            throw new InvalidOperationException(
                $"Customer {Id} was merged into {survivor} and cannot change.");

        if (IsErased)
            throw new InvalidOperationException($"Customer {Id} was erased and cannot change.");
    }

    private void Touch(TimeProvider clock) => UpdatedAt = clock.GetUtcNow();
}

/// <summary>
/// The tariffs a customer can be on. Two, at laboratory scale, and the same two
/// `Pricing` already ships as committed price lists.
///
/// A closed set rather than free text, because the segment selects a tariff: a
/// typo would silently quote everybody the default and look like a pricing bug.
/// </summary>
public static class Segments
{
    public const string Retail = "retail";
    public const string Vip = "vip";

    public static readonly string[] All = [Retail, Vip];

    public static string Normalise(string segment)
    {
        var trimmed = segment?.Trim().ToLowerInvariant();

        return Array.Exists(All, known => known == trimmed)
            ? trimmed!
            : throw new InvalidOperationException(
                $"Unknown segment '{segment}'. Known: {string.Join(", ", All)}.");
    }
}

/// <summary>Aliased so this file can use the SharedKernel's culture rules
/// without its own `Culture` property shadowing the type.</summary>
internal static class Culture
{
    public static string Normalise(string culture) => SharedKernel.Culture.Normalize(culture);
}
