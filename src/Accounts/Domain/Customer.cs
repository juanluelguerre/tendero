using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Domain;

public sealed record CustomerRegistered(CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;

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
