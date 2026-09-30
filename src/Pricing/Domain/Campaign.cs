using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// A campaign's name as code, URL and filter carry it: upper snake case
/// (<c>TENDERO_DAYS</c>, <c>BLACK_FRIDAY</c>), the way a promotion's code and a
/// category's are written. It is the aggregate's key, and it never changes: a
/// listing's <c>?campaign=TENDERO_DAYS</c> has to mean the same event tomorrow.
/// </summary>
public readonly record struct CampaignCode
{
    public const int MaxLength = 40;

    public CampaignCode(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException(
                $"'{value}' is not a campaign code: upper snake case, a letter first, at most {MaxLength} characters (TENDERO_DAYS).",
                nameof(value));

        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Upper snake case: a letter first, then capitals, digits and single
    /// underscores between words. Written out rather than as a generated regex,
    /// whose code lands outside the domain's namespace and would be the one thing
    /// the domain depends on besides the kernel.
    /// </summary>
    public static bool IsValid(string? text)
    {
        if (text is not { Length: > 0 and <= MaxLength } || !char.IsAsciiLetterUpper(text[0]) || text[^1] == '_')
            return false;

        for (var index = 1; index < text.Length; index++)
        {
            var current = text[index];
            var allowed = char.IsAsciiLetterUpper(current) || char.IsAsciiDigit(current)
                          || (current == '_' && text[index - 1] != '_');
            if (!allowed)
                return false;
        }

        return true;
    }

    /// <summary>Reads what a URL or a form carries, in any case: <c>tendero_days</c> is TENDERO_DAYS.</summary>
    public static bool TryParse(string? text, out CampaignCode code)
    {
        var candidate = text?.Trim().ToUpperInvariant();
        if (!IsValid(candidate))
        {
            code = default;
            return false;
        }

        code = new CampaignCode(candidate!);
        return true;
    }

    public override string ToString() => Value;
}

/// <summary>
/// What kind of campaign it is, which is what the storefront draws. An
/// <see cref="Event"/> is short, has a clock and leads (Tendero Days, Black
/// Friday); a <see cref="Season"/> is weeks long, has no clock, and is what the
/// card shows when no event runs.
/// </summary>
public enum CampaignKind
{
    Event,
    Season,
}

/// <summary>
/// Where a campaign applies. A shop-wide one may lead the home; a category one is
/// the banner of that category's pages and never reaches the home. Derived from
/// <see cref="Campaign.CategoryCode"/> — null is the shop — so the two cannot
/// disagree.
/// </summary>
public enum CampaignScope
{
    Shop,
    Category,
}

/// <summary>
/// The stored lifecycle (ADR 0044): what the shopkeeper's actions set. Running is
/// deliberately absent — a scheduled campaign runs when its window opens and
/// nobody sets that, so it is a <see cref="CampaignPhase"/> read from the clock.
/// </summary>
public enum CampaignStatus
{
    /// <summary>Being prepared, or taken off the shop to be edited. The only status that may be edited.</summary>
    Draft,

    /// <summary>On the shop's calendar: shown while its window is near or open.</summary>
    Scheduled,

    /// <summary>Called off, or ended early. Terminal: a campaign that ended is part of the record.</summary>
    Ended,
}

/// <summary>
/// Where a campaign's window stands at one instant: before it, inside it, or
/// after it. **Computed when read, never stored** (R-7.3's rule for a fact that
/// time alone changes): a stored "running" would be wrong from the minute the
/// window opened until something rewrote it.
///
/// It is a fact of the window alone. Whether the shop SHOWS the campaign is the
/// window and the status together (<see cref="Campaign.IsLiveAt"/>): a draft
/// whose window is open is <see cref="Running"/> by the clock and on no page.
/// </summary>
public enum CampaignPhase
{
    Upcoming,
    Running,

    /// <summary>The window has closed. The public list never answers it; the backoffice does.</summary>
    Over,
}

/// <summary>A campaign's own picture, stored and served as a product's is (by id, <c>/api/images/{id}</c>), with its alt text in both languages.</summary>
public sealed record CampaignImage(ImageId Id, LocalizedText Alt);

/// <summary>
/// Everything a shopkeeper sets on a campaign besides its code: one value, so
/// creating and editing take the same thing and are checked by the same rule.
/// </summary>
/// <param name="CategoryCode">Null for the whole shop; a category code for that category's pages.</param>
/// <param name="PromotionCodes">The promotions it groups, as codes — values, never a <see cref="Promotion"/> (ADR 0014).</param>
public sealed record CampaignDetails(
    LocalizedText Name,
    LocalizedText Line,
    CampaignKind Kind,
    string? CategoryCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Priority,
    IReadOnlyList<string> PromotionCodes,
    CampaignImage? Image = null);

public sealed record CampaignCreated(CampaignCode Code, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CampaignUpdated(CampaignCode Code, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CampaignScheduled(CampaignCode Code, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CampaignUnscheduled(CampaignCode Code, DateTimeOffset OccurredAt) : IDomainEvent;

/// <param name="From">Draft when it was called off before it was ever scheduled; Scheduled when it was ended early.</param>
public sealed record CampaignEnded(CampaignCode Code, CampaignStatus From, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A named window over promotions (R-8, ADR 0044): <i>Tendero Days</i>, 1–7
/// October, groups SUMMER25 and SHIP0. **It is not a discount.** The promotions
/// keep their rules and the engine keeps choosing between them; a campaign only
/// says <i>these, now, under this name</i>, and decides what the shop leads with.
/// That is why the engine never sees one, and its properties stay untouched.
///
/// Pricing's first aggregate with a table: it has a lifecycle, an actor (the
/// shopkeeper) and a consistency boundary of its own — what ADR 0014 asks of a
/// context before it writes.
///
/// **Editing happens in Draft only.** A scheduled campaign is a promise the shop
/// is already making — its card may be on the home, its listing linked — so to
/// change one the shopkeeper unschedules it, edits it and schedules it again. Two
/// clicks, and no campaign ever changes its window or its promotions while a
/// shopper is looking at its clock.
/// </summary>
public sealed class Campaign : AggregateRoot
{
    private static readonly TransitionTable<CampaignStatus> AllowedTransitions = new()
    {
        [CampaignStatus.Draft] = [CampaignStatus.Scheduled, CampaignStatus.Ended],
        [CampaignStatus.Scheduled] = [CampaignStatus.Draft, CampaignStatus.Ended],
        [CampaignStatus.Ended] = [],
    };

    /// <summary>How far ahead the shop announces a campaign: the spec's thirty days.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(30);

    private List<string> promotionCodes = [];

    public CampaignCode Code { get; private set; }

    public LocalizedText Name { get; private set; } = null!;

    /// <summary>The sentence under the name: "Hasta −25 % en ropa y envío gratis desde 50 €".</summary>
    public LocalizedText Line { get; private set; } = null!;

    public CampaignKind Kind { get; private set; }

    /// <summary>Null for the whole shop; otherwise the category, upper case as the catalogue writes it.</summary>
    public string? CategoryCode { get; private set; }

    public CampaignScope Scope => CategoryCode is null ? CampaignScope.Shop : CampaignScope.Category;

    /// <summary>Inclusive: the campaign runs from this instant.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Exclusive: at this instant it has closed, as a promotion's <c>ValidTo</c> is.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Higher leads: between two running campaigns, the one the shop shows first.</summary>
    public int Priority { get; private set; }

    public IReadOnlyList<string> PromotionCodes => this.promotionCodes;

    public ImageId? ImageId { get; private set; }

    public LocalizedText? ImageAlt { get; private set; }

    public CampaignImage? Image => ImageId is { } id && ImageAlt is { } alt ? new CampaignImage(id, alt) : null;

    public CampaignStatus Status { get; private set; }

    /// <summary>When the shopkeeper ended it, if they did; the window is kept as it was planned.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Campaign() { } // EF Core

    /// <summary>A new campaign is a draft: nothing reaches the shop until it is scheduled.</summary>
    public static Campaign Create(TimeProvider clock, CampaignCode code, CampaignDetails details)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(code.Value, nameof(code));

        var now = clock.GetUtcNow();
        var campaign = new Campaign
        {
            Code = code,
            Status = CampaignStatus.Draft,
            CreatedAt = now,
        };
        campaign.Apply(details, now);
        campaign.Raise(new CampaignCreated(code, now));
        return campaign;
    }

    /// <summary>Whether the shopkeeper may change it now. Draft only — see the class's summary.</summary>
    public bool IsEditable => Status == CampaignStatus.Draft;

    /// <summary>Whether the table has an edge from here to <paramref name="to"/>.</summary>
    public bool CanMoveTo(CampaignStatus to) => AllowedTransitions.Allows(Status, to);

    /// <summary>
    /// Whether a window is one the shop can still honour at <paramref name="at"/>:
    /// it ends after it starts, and it has not closed. Setting a window that is
    /// already over, or scheduling one, promises something that can never run.
    /// </summary>
    public static bool IsOpenWindow(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset at) =>
        endsAt > startsAt && endsAt > at;

    public void Update(TimeProvider clock, CampaignDetails details)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!IsEditable)
            throw new InvalidOperationException($"Campaign {Code} is {Status}; only a draft is edited — unschedule it first.");

        var now = clock.GetUtcNow();
        Apply(details, now);
        Raise(new CampaignUpdated(Code, now));
    }

    public void Schedule(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();

        AllowedTransitions.EnsureAllowed(Status, CampaignStatus.Scheduled, $"campaign {Code}");
        if (!IsOpenWindow(StartsAt, EndsAt, now))
            throw new InvalidOperationException($"Campaign {Code} closed at {EndsAt:O}; a window that is over cannot be scheduled.");

        Status = CampaignStatus.Scheduled;
        UpdatedAt = now;
        Raise(new CampaignScheduled(Code, StartsAt, EndsAt, now));
    }

    /// <summary>Takes it off the shop's calendar, back to draft — the way to edit a scheduled campaign.</summary>
    public void Unschedule(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, CampaignStatus.Draft, $"campaign {Code}");
        Status = CampaignStatus.Draft;
        UpdatedAt = clock.GetUtcNow();
        Raise(new CampaignUnscheduled(Code, UpdatedAt));
    }

    /// <summary>Ends it now: a scheduled campaign ended early, or a draft called off. Final.</summary>
    public void End(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var from = Status;
        AllowedTransitions.EnsureAllowed(Status, CampaignStatus.Ended, $"campaign {Code}");
        Status = CampaignStatus.Ended;
        EndedAt = UpdatedAt = clock.GetUtcNow();
        Raise(new CampaignEnded(Code, from, UpdatedAt));
    }

    /// <summary>Where the window stands at <paramref name="at"/>. Start inclusive, end exclusive.</summary>
    public CampaignPhase PhaseAt(DateTimeOffset at) =>
        at < StartsAt ? CampaignPhase.Upcoming
        : at < EndsAt ? CampaignPhase.Running
        : CampaignPhase.Over;

    /// <summary>On the shop right now: scheduled, and inside its window.</summary>
    public bool IsLiveAt(DateTimeOffset at) => Status == CampaignStatus.Scheduled && PhaseAt(at) == CampaignPhase.Running;

    /// <summary>
    /// Whether the shop announces it at <paramref name="at"/>: scheduled, and
    /// either running or starting within the <see cref="Horizon"/>.
    /// </summary>
    public bool IsNearAt(DateTimeOffset at) =>
        Status == CampaignStatus.Scheduled
        && PhaseAt(at) switch
        {
            CampaignPhase.Running => true,
            CampaignPhase.Upcoming => StartsAt <= at + Horizon,
            _ => false,
        };

    public bool Groups(string promotionCode) =>
        this.promotionCodes.Contains(promotionCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What this campaign's promotions reach at <paramref name="at"/> — the "on
    /// offer" rule (<see cref="OfferReach.Of"/>) narrowed to the promotions it
    /// groups, and <see cref="OfferReach.Nothing"/> unless it is live. **No second
    /// rule**: a campaign's listing is the on-offer listing of its own promotions,
    /// so a coupon promotion reaches nothing here for the reason it reaches
    /// nothing there.
    /// </summary>
    public OfferReach ReachAt(IEnumerable<Promotion> promotions, DateTimeOffset at) =>
        IsLiveAt(at)
            ? OfferReach.Of(promotions.Where(promotion => Groups(promotion.Code)), at)
            : OfferReach.Nothing;

    private void Apply(CampaignDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(details.Name);
        ArgumentNullException.ThrowIfNull(details.Line);
        ArgumentNullException.ThrowIfNull(details.PromotionCodes);

        if (!IsOpenWindow(details.StartsAt, details.EndsAt, now))
            throw new InvalidOperationException(
                $"Campaign {Code}: a window ends after it starts and has not closed ({details.StartsAt:O} – {details.EndsAt:O}).");

        string[] codes =
        [
            .. details.PromotionCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal),
        ];

        // A campaign with nothing in it says nothing: it is a name over no offer.
        if (codes.Length == 0)
            throw new InvalidOperationException($"Campaign {Code} groups no promotion.");

        Name = details.Name;
        Line = details.Line;
        Kind = details.Kind;
        CategoryCode = string.IsNullOrWhiteSpace(details.CategoryCode) ? null : details.CategoryCode.Trim().ToUpperInvariant();
        // The same instants at offset zero: a window may be written in the shop's own
        // offset (the seed file's +01:00), and a time is stored in UTC (ADR 0046).
        StartsAt = details.StartsAt.ToUniversalTime();
        EndsAt = details.EndsAt.ToUniversalTime();
        Priority = details.Priority;
        this.promotionCodes = [.. codes];
        ImageId = details.Image?.Id;
        ImageAlt = details.Image?.Alt;
        UpdatedAt = now;
    }
}

/// <summary>
/// Which campaigns the shop announces, and in what order — **the server's rule,
/// not the page's** (R-8). The home draws the first; an agent reads the list.
/// </summary>
public static class CampaignLineup
{
    /// <summary>
    /// The scheduled campaigns running at <paramref name="at"/> or starting within
    /// the horizon, the leading one first: running before upcoming (what is on
    /// now beats what is coming), then by priority, highest first, then the
    /// earliest start, and the code last so two identical campaigns still come
    /// in one order.
    /// </summary>
    public static IReadOnlyList<Campaign> Near(IEnumerable<Campaign> campaigns, DateTimeOffset at) =>
    [
        .. campaigns
            .Where(campaign => campaign.IsNearAt(at))
            .OrderBy(campaign => campaign.PhaseAt(at) == CampaignPhase.Running ? 0 : 1)
            .ThenByDescending(campaign => campaign.Priority)
            .ThenBy(campaign => campaign.StartsAt)
            .ThenBy(campaign => campaign.Code.Value, StringComparer.Ordinal),
    ];
}
