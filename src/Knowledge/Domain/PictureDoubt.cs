using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Domain;

public readonly record struct PictureDoubtId(Guid Value)
{
    public static PictureDoubtId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public enum PictureDoubtStatus
{
    /// <summary>Waiting for a person to look at the picture.</summary>
    Open,

    /// <summary>A person said what it is.</summary>
    Decided,

    /// <summary>The picture was read again and no longer says it: nobody decided.</summary>
    Withdrawn,
}

/// <summary>What a person says of a picture that was doubted.</summary>
public enum PictureDoubtDecision
{
    /// <summary>The picture shows the product as it is: the two colours are one to the shop.</summary>
    PictureIsFine,

    /// <summary>The picture is wrong and another is to take its place.</summary>
    ReplaceThePicture,

    /// <summary>The picture is right, and of another variant than the one it is filed under.</summary>
    BelongsToAnotherVariant,
}

/// <summary>
/// What a picture answered to a closed question, beside what the catalogue holds
/// for the variants the picture is of — as values (ADR 0014): codes for code to
/// compare, labels for a person to read.
/// </summary>
/// <param name="AttributeCode">What was asked: the colour, today.</param>
/// <param name="Seen">The option the model chose.</param>
/// <param name="Held">The option the catalogue holds.</param>
/// <param name="Confidence">The probability the model gave its answer, when its server said.</param>
/// <param name="Related">Whether the catalogue says the two options may be mistaken for each other — stainless steel and grey.</param>
/// <param name="OfAnotherVariant">Whether the colour seen is one another variant of the same product holds.</param>
public sealed record ColourSeen(
    string AttributeCode, string Seen, LocalizedText SeenLabel, string Held, LocalizedText HeldLabel, double? Confidence, bool Related,
    bool OfAnotherVariant = false);

public sealed record PictureDoubtRaised(PictureDoubtId DoubtId, ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A person decided a doubt. It carries, as values (ADR 0014), what whoever acts
/// on the decision needs and may not come back to Knowledge for: which picture,
/// what was asked of it and what was seen. Optional, because a decision stored
/// before the event carried them is still in somebody's outbox.
/// </summary>
public sealed record PictureDoubtDecided(
    PictureDoubtId DoubtId, ProductId ProductId, PictureDoubtDecision Decision, string DecidedBy, DateTimeOffset OccurredAt,
    string? PictureId = null, string? AttributeCode = null, string? Seen = null) : IDomainEvent;

/// <summary>
/// A doubt about one picture of a product (phase 13a, I-4d): the model that read
/// it is sure it shows another colour than the variants it is of hold.
///
/// **Not a claim.** A claim says something of the product and, approved, is
/// written to it — and approving *colour: grey* would rewrite a colour a person
/// chose so that it matches a drawing. What is in doubt here is the picture: it
/// is the wrong one, or drawn wrong, or filed under the wrong variant. So it has
/// its own record and its own decisions, and it writes nothing anywhere: it
/// **informs**, and a product with an open doubt is published and sold as any
/// other (the author's ruling, 2026-10-08). One decision is acted on, outside
/// Knowledge: *it belongs to another variant* reaches Catalog through the outbox,
/// which files the picture under the colour seen (ADR 0057).
/// </summary>
public sealed class PictureDoubt : AggregateRoot
{
    /// <summary>
    /// The confidence at and above which a difference is a doubt. Under it the
    /// model does not know — it answered *olive* at 0.63 for a forest green
    /// backpack — and its answer is kept in the reading and raises nothing. Of
    /// forty seed pictures, three differed: 0.96, 0.94 and 0.63.
    /// </summary>
    public const double SureDifference = 0.90;

    private static readonly TransitionTable<PictureDoubtStatus> AllowedTransitions = new()
    {
        [PictureDoubtStatus.Open] = [PictureDoubtStatus.Decided, PictureDoubtStatus.Withdrawn],
        [PictureDoubtStatus.Decided] = [],
        [PictureDoubtStatus.Withdrawn] = [],
    };

    public PictureDoubtId Id { get; private set; }
    public ProductId ProductId { get; private set; }

    /// <summary>The picture, by its content's id.</summary>
    public string PictureId { get; private set; } = "";

    public string AttributeCode { get; private set; } = "";
    public string Seen { get; private set; } = "";
    public LocalizedText SeenLabel { get; private set; } = null!;
    public string Held { get; private set; } = "";
    public LocalizedText HeldLabel { get; private set; } = null!;
    public double Confidence { get; private set; }

    /// <summary>
    /// The colour seen is one another variant of the same product holds: most
    /// likely the picture is that variant's, filed under this one. What a person
    /// is told before they decide — it decides nothing itself.
    /// </summary>
    public bool SeenOnAnotherVariant { get; private set; }

    public string ReadByModel { get; private set; } = "";
    public string ReadWithPrompt { get; private set; } = "";

    public PictureDoubtStatus Status { get; private set; }
    public PictureDoubtDecision? Decision { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset RaisedAt { get; private set; }

    private PictureDoubt() { } // EF Core

    /// <summary>
    /// The doubt an answer raises, or null when it raises none: the same colour,
    /// two names the catalogue says are one, or a model that was not sure — or
    /// whose server gave no probability, which is not being sure.
    /// </summary>
    public static PictureDoubt? Raise(ProductId productId, PictureReading reading, ColourSeen answer, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.Equals(answer.Seen, answer.Held, StringComparison.OrdinalIgnoreCase)
            || answer.Related
            || answer.Confidence is not { } confidence
            || confidence < SureDifference)
        {
            return null;
        }

        var doubt = new PictureDoubt
        {
            Id = PictureDoubtId.New(),
            ProductId = productId,
            PictureId = reading.Picture.Value,
            AttributeCode = answer.AttributeCode,
            Seen = answer.Seen,
            SeenLabel = answer.SeenLabel,
            Held = answer.Held,
            HeldLabel = answer.HeldLabel,
            Confidence = confidence,
            SeenOnAnotherVariant = answer.OfAnotherVariant,
            ReadByModel = reading.Model,
            ReadWithPrompt = reading.PromptVersion,
            Status = PictureDoubtStatus.Open,
            RaisedAt = clock.GetUtcNow(),
        };
        doubt.Raise(new PictureDoubtRaised(doubt.Id, productId, doubt.RaisedAt));
        return doubt;
    }

    /// <summary>Whether this is the doubt an answer would raise: the same question, the same colour seen, the same colour held.</summary>
    public bool IsAbout(ColourSeen answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return string.Equals(AttributeCode, answer.AttributeCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Seen, answer.Seen, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Held, answer.Held, StringComparison.OrdinalIgnoreCase);
    }

    public void Decide(PictureDoubtDecision decision, string decidedBy, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, PictureDoubtStatus.Decided, $"picture doubt {Id}");
        Status = PictureDoubtStatus.Decided;
        Decision = decision;
        DecidedBy = decidedBy;
        DecidedAt = clock.GetUtcNow();
        Raise(new PictureDoubtDecided(Id, ProductId, decision, decidedBy, DecidedAt.Value, PictureId, AttributeCode, Seen));
    }

    /// <summary>The picture was read again and the doubt is not what it says now. Not a decision: no person made it.</summary>
    public void Withdraw(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        AllowedTransitions.EnsureAllowed(Status, PictureDoubtStatus.Withdrawn, $"picture doubt {Id}");
        Status = PictureDoubtStatus.Withdrawn;
    }
}
