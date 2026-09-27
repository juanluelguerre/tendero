using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Domain;

public readonly record struct ClaimId(Guid Value)
{
    public static ClaimId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public enum ClaimStatus
{
    /// <summary>Waiting for a person: nothing outside Knowledge sees it.</summary>
    Proposed,

    /// <summary>A person said yes. The only status that leaves the context (ADR 0018, 7).</summary>
    Approved,

    /// <summary>A person said no.</summary>
    Rejected,

    /// <summary>The text it rested on changed: a newer document replaced it.</summary>
    Superseded,
}

/// <summary>
/// The sentence a claim rests on, and how well it supports the claim — measured by
/// the support port, never declared by the extractor (ADR 0018, 5).
/// </summary>
public sealed record Evidence(SourceDocumentId Document, Quote Quote, double Support)
{
    // EF Core cannot bind a complex property (the quote) through a constructor.
    private Evidence() : this(default, null!, 0) { }
}

/// <summary>
/// Where a claim came from: which extractor, which model revision, which prompt
/// version, in which run. When a prompt changes, the claims it produced are found
/// by this record and proposed again.
/// </summary>
public sealed record Provenance(
    string Extractor,
    string? ModelId,
    string? ModelRevision,
    string? PromptVersion,
    string RunId,
    DateTimeOffset At);

public sealed record ClaimProposed(ClaimId ClaimId, ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A person approved a claim. The event carries values, not the claim: whoever
/// applies it — Catalog's attribute, Search's text — reads what it needs from
/// here, and never reaches into Knowledge (ADR 0014).
/// </summary>
public sealed record ClaimApproved(
    ClaimId ClaimId, ProductId ProductId, string? AttributeCode, string? Value, string ApprovedBy, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ClaimRejected(ClaimId ClaimId, ProductId ProductId, string RejectedBy, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A claim no longer stands because its text changed; if it was approved, what it applied must be revisited.</summary>
public sealed record ClaimSuperseded(ClaimId ClaimId, ProductId ProductId, bool WasApproved, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// One thing the shop can say about a product, with the sentence it rests on and
/// the person who stood behind it. **The AI writes claims, not products** (ADR
/// 0018): a model proposes one, a person approves or rejects it, and only then
/// does anything outside Knowledge change.
/// </summary>
public sealed class ProductClaim : AggregateRoot
{
    private static readonly TransitionTable<ClaimStatus> AllowedTransitions = new()
    {
        [ClaimStatus.Proposed] = [ClaimStatus.Approved, ClaimStatus.Rejected, ClaimStatus.Superseded],

        // An approved claim can only be overtaken by its text changing: taking a
        // fact back is a new claim that says the opposite, reviewed like any other.
        [ClaimStatus.Approved] = [ClaimStatus.Superseded],

        [ClaimStatus.Rejected] = [],
        [ClaimStatus.Superseded] = [],
    };

    private readonly List<Evidence> evidence = [];

    public ClaimId Id { get; private set; }
    public ProductId ProductId { get; private set; }
    public LocalizedText Statement { get; private set; } = null!;

    /// <summary>The attribute it types, when the text states a value for one Catalog defines.</summary>
    public string? AttributeCode { get; private set; }

    /// <summary>That value, invariant — already checked against the attribute's kind, unit and options before it got here.</summary>
    public string? Value { get; private set; }

    public Provenance Provenance { get; private set; } = null!;
    public ClaimStatus Status { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<Evidence> Evidence => this.evidence;

    /// <summary>
    /// How Catalog holds the value this claim applied — its culture-free key
    /// ("GLASS", "3", "true") — once a person approved it and Catalog accepted it.
    /// What a spec row is matched on (P10-7): the quote is shown beside a value only
    /// when the product holds exactly the value the claim put there, so a later
    /// reimport that changed it leaves the row with its catalogue source, not a
    /// quote that no longer supports it.
    /// </summary>
    public string? AppliedAs { get; private set; }

    /// <summary>Why Catalog would not hold the value — its answer, in one word — or null.</summary>
    public string? ApplicationRefused { get; private set; }

    /// <summary>
    /// What Catalog answered when the approval was applied. Not a transition: the
    /// claim stays approved either way — a person stood behind the statement, and
    /// it is still true as a sentence — but a refused one types nothing and is
    /// shown as a statement only.
    /// </summary>
    public void RecordApplication(string? heldAs, string? refusal)
    {
        if (Status != ClaimStatus.Approved)
            throw new InvalidOperationException($"Only an approved claim is applied; claim {Id} is {Status}.");
        if (AttributeCode is null)
            throw new InvalidOperationException($"Claim {Id} types no attribute; there is nothing to apply.");
        if ((heldAs is null) == (refusal is null))
            throw new ArgumentException("Catalog either holds the value or refused it.", nameof(refusal));

        AppliedAs = heldAs;
        ApplicationRefused = refusal;
    }

    /// <summary>
    /// What the review screen shows: the claim's <em>weakest</em> support. A
    /// confident claim resting on a thin quote is the failure worth surfacing.
    /// </summary>
    public double Support => this.evidence.Min(item => item.Support);

    private ProductClaim() { } // EF Core

    public static ProductClaim Propose(
        ProductId productId,
        LocalizedText statement,
        string? attributeCode,
        string? value,
        IReadOnlyList<Evidence> evidence,
        Provenance provenance,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(clock);

        if (evidence.Count == 0)
            throw new ArgumentException("A claim rests on at least one quote.", nameof(evidence));
        if (evidence.Any(item => item.Support is < 0 or > 1))
            throw new ArgumentException("Support is a probability, from 0 to 1.", nameof(evidence));
        if ((attributeCode is null) != (value is null))
            throw new ArgumentException("An attribute and its value come together, or neither does.", nameof(value));

        var claim = new ProductClaim
        {
            Id = ClaimId.New(),
            ProductId = productId,
            Statement = statement,
            AttributeCode = attributeCode,
            Value = value,
            Provenance = provenance,
            Status = ClaimStatus.Proposed,
            CreatedAt = clock.GetUtcNow(),
        };
        claim.evidence.AddRange(evidence);
        claim.Raise(new ClaimProposed(claim.Id, productId, claim.CreatedAt));
        return claim;
    }

    public void Approve(string reviewer, TimeProvider clock) =>
        Review(ClaimStatus.Approved, reviewer, clock, (id, at) => new ClaimApproved(id, ProductId, AttributeCode, Value, reviewer, at));

    public void Reject(string reviewer, TimeProvider clock) =>
        Review(ClaimStatus.Rejected, reviewer, clock, (id, at) => new ClaimRejected(id, ProductId, reviewer, at));

    /// <summary>The text it rested on changed. Not a review: no person decides it, the document did.</summary>
    public void Supersede(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var wasApproved = Status == ClaimStatus.Approved;
        AllowedTransitions.EnsureAllowed(Status, ClaimStatus.Superseded, $"claim {Id}");
        Status = ClaimStatus.Superseded;
        Raise(new ClaimSuperseded(Id, ProductId, wasApproved, clock.GetUtcNow()));
    }

    private void Review(ClaimStatus target, string reviewer, TimeProvider clock, Func<ClaimId, DateTimeOffset, IDomainEvent> raised)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewer);
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, target, $"claim {Id}");
        Status = target;
        ReviewedBy = reviewer;
        ReviewedAt = clock.GetUtcNow();
        Raise(raised(Id, ReviewedAt.Value));
    }
}
