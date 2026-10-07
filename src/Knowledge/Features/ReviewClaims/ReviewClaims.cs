using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Knowledge.Features.ReviewClaims;

// ---------- The queue ----------

/// <summary>
/// The claims waiting for a person, the weakest-supported first (ADR 0018, 5), a
/// page at a time (R-7.2). It stopped at a hundred without saying so, and a queue
/// that hides its hundred-and-first claim is a queue that looks finished.
/// </summary>
/// <param name="Product">One product's claims, for its bulk review (S-5); every product's when null.</param>
/// <param name="Culture">The reviewer's language: what the catalogue holds is shown in it beside a claim that says otherwise.</param>
public sealed record ListClaimsForReviewQuery(
    ClaimSupportFilter Support = ClaimSupportFilter.All, int Page = Paging.FirstPage, int PageSize = Paging.DefaultPageSize,
    ProductId? Product = null, string Culture = "es")
    : IQuery<ClaimsForReviewPage>;

/// <summary>How many waiting claims one support filter shows.</summary>
public sealed record ClaimSupportCount(ClaimSupportFilter Support, int Count);

/// <param name="Total">How many the support filter matched: the queue's length, not the page's.</param>
/// <param name="Counts">Every filter, zero included, over the whole queue
/// whichever is shown: the weak count is the server's rule, and a count the
/// screen took from one page would be the page's.</param>
public sealed record ClaimsForReviewPage(
    IReadOnlyList<ClaimForReview> Items, int Total, int Page, int PageSize, IReadOnlyList<ClaimSupportCount> Counts);

public sealed class ListClaimsForReviewValidator : AbstractValidator<ListClaimsForReviewQuery>
{
    public ListClaimsForReviewValidator()
    {
        RuleFor(query => query.Support).IsInEnum();
        RuleFor(query => query.Page).IsAPage();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}

/// <summary>
/// One claim as the reviewer reads it: what it says, in both languages, what it
/// types, the words it rests on and where they sit in the description, how well
/// they support it, and who proposed it.
/// </summary>
public sealed record ClaimForReview(
    string ClaimId,
    string ProductId,
    string StatementSpanish,
    string StatementEnglish,
    string? AttributeCode,
    string? Value,
    double Support,
    IReadOnlyList<QuoteForReview> Quotes,
    string Extractor,
    string? Model,
    DateTimeOffset ProposedAt,
    /// <summary>Whether its support is under the line a person is for — the
    /// server's rule (`ProductClaim.WeakSupport`), sent so the screen marks it
    /// without keeping a copy of 0.8 (R-7.2).</summary>
    bool IsWeak,
    /// <summary>The picture it was read from, for a claim that rests on a model's
    /// description of one (phase 13a, I-4); null for a claim read from the shop's
    /// own text. Its quotes are then the model's words about the picture, and the
    /// screen shows the picture beside them.</summary>
    PictureForReview? Picture,
    /// <summary>Why it is opened and decided on its own, never approved with
    /// others; null when it may go together. The server's rule, sent so no screen
    /// keeps a copy of it.</summary>
    AloneReason? DecidedAlone,
    /// <summary>The value the catalogue holds for the attribute this claim types,
    /// when it is another one (phase 13a, I-4); null when the claim adds a value,
    /// agrees with it, or types nothing. Approving the claim would replace it.</summary>
    CatalogueContradiction? Contradicts);

/// <summary>A claim and the catalogue saying two values for one attribute — both named, neither corrected.</summary>
/// <param name="Claimed">The claim's value, as the claim writes it.</param>
/// <param name="Held">What the product holds, as a person reads it in the reviewer's language.</param>
public sealed record CatalogueContradiction(string AttributeCode, string Claimed, string Held);

public sealed record QuoteForReview(string Culture, string Text, int Start, int End, double Support);

/// <summary>The picture a claim was read from, and who read it.</summary>
/// <param name="ImageId">The picture, by the id the image store serves it under.</param>
/// <param name="ReadBy">The model that described it.</param>
/// <param name="PromptVersion">The instructions it was described under.</param>
public sealed record PictureForReview(string ImageId, string ReadBy, string PromptVersion);

/// <summary>Why a claim is never approved together with others.</summary>
public enum AloneReason
{
    /// <summary>Its support is under the line a person is for (S-5).</summary>
    WeakSupport,

    /// <summary>
    /// It says another value than the catalogue holds for the same attribute
    /// (phase 13a, I-4). Approving it replaces that value, and which of the two is
    /// wrong is a person's call — never made for several at once.
    /// </summary>
    ContradictsTheCatalogue,

    /// <summary>
    /// It rests on what a model read in a picture (phase 13a, I-4), and the
    /// model was not sure enough of it: the person who approves it is the only
    /// independent check — and has to have it open, with the picture, to be one.
    ///
    /// **Not every claim from a picture.** A picture's own answer to a closed
    /// question, given with a probability at or above
    /// <see cref="ProductClaim.SureAnswer"/>, may be approved with others: that
    /// is the tray. A person still approves it (invariant 12).
    /// </summary>
    ReadFromAPicture,
}

/// <summary>Which claims read from a picture a person may approve together with others.</summary>
public static class PictureClaims
{
    /// <summary>
    /// A picture's own answer, and a sure one: built by code from what the
    /// picture answered (not extracted from prose about it), with the model's
    /// probability for that answer at or above the line.
    /// </summary>
    public static bool IsASureAnswer(ProductClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return claim.Provenance.Extractor == ClaimExtractors.Picture && claim.Support >= ProductClaim.SureAnswer;
    }
}

/// <summary>
/// A typed claim against what its product holds. A contradiction only where both
/// say a value for the same attribute and the values differ: a claim that adds a
/// value, agrees with it, or types nothing contradicts nothing.
/// </summary>
internal static class CatalogueAgreement
{
    public static CatalogueContradiction? Of(ProductClaim claim, IReadOnlyList<HeldValue>? held)
    {
        if (claim is not { AttributeCode: { } code, Value: { } value } || held is null)
            return null;

        var now = held.FirstOrDefault(item => string.Equals(item.AttributeCode, code, StringComparison.OrdinalIgnoreCase));
        return now is null || SameValue.As(value, now.Key) ? null : new CatalogueContradiction(code, value, now.Shown);
    }
}

public sealed class ListClaimsForReviewHandler(IKnowledgeRepository knowledge, ICatalogueValues catalogue)
    : IQueryHandler<ListClaimsForReviewQuery, ClaimsForReviewPage>
{
    public async Task<ClaimsForReviewPage> HandleAsync(ListClaimsForReviewQuery query, CancellationToken cancellationToken)
    {
        var page = await knowledge.ClaimsAsync(ClaimStatus.Proposed, query.Support, query.Page, query.PageSize, query.Product, cancellationToken);
        var result = new List<ClaimForReview>(page.Claims.Count);

        // Asked once for the page's products: twenty claims of one product are one question.
        var held = await catalogue.OfAsync(
            [.. page.Claims.Where(claim => claim.AttributeCode is not null).Select(claim => claim.ProductId).Distinct()],
            query.Culture, cancellationToken);

        foreach (var claim in page.Claims)
        {
            var quotes = new List<QuoteForReview>();
            PictureReading? reading = null;
            foreach (var evidence in claim.Evidence)
            {
                // A quote's culture is its document's; the documents are few per product.
                var document = await knowledge.FindDocumentAsync(evidence.Document, cancellationToken);
                quotes.Add(new QuoteForReview(document?.Culture ?? "", evidence.Quote.Text, evidence.Quote.Start, evidence.Quote.End, evidence.Support));
                reading ??= document?.Reading;
            }

            var contradiction = CatalogueAgreement.Of(claim, held.GetValueOrDefault(claim.ProductId));

            result.Add(new ClaimForReview(
                claim.Id.ToString(), claim.ProductId.ToString(),
                claim.Statement.In("es"), claim.Statement.In("en"),
                claim.AttributeCode, claim.Value, claim.Support, quotes,
                claim.Provenance.Extractor, claim.Provenance.ModelId, claim.CreatedAt,
                claim.IsWeaklySupported,
                reading is null ? null : new PictureForReview(reading.Picture.Value, reading.Model, reading.PromptVersion),
                AloneFor(claim, contradiction, reading),
                contradiction));
        }

        return new ClaimsForReviewPage(
            result, page.Total, query.Page, query.PageSize,
            [.. Enum.GetValues<ClaimSupportFilter>().Select(filter =>
                new ClaimSupportCount(filter, page.CountBySupport.GetValueOrDefault(filter)))]);
    }

    /// <summary>
    /// Why a claim is decided on its own, one reason: weak support first — it is
    /// the reason to read a claim before the rest, whatever it was read from —
    /// then a value the catalogue holds otherwise, then a picture.
    /// </summary>
    private static AloneReason? AloneFor(ProductClaim claim, CatalogueContradiction? contradiction, PictureReading? reading) =>
        claim.IsWeaklySupported ? AloneReason.WeakSupport
        : contradiction is not null ? AloneReason.ContradictsTheCatalogue
        : reading is not null && !PictureClaims.IsASureAnswer(claim) ? AloneReason.ReadFromAPicture
        : null;
}

// ---------- The decision ----------

public enum ReviewDecision
{
    Approve,
    Reject,
}

/// <summary>A person's decision on a claim — never a model's, never a chat message (invariant 12).</summary>
/// <param name="InBulk">Approved with others, through the bulk review (S-5): then a
/// weakly supported claim is refused — on its own audit row, as a denial.</param>
public sealed record ReviewClaimCommand(ClaimId ClaimId, ReviewDecision Decision, bool InBulk = false) : ICommand<ReviewClaimResult>;

public enum ReviewOutcome
{
    Approved,
    Rejected,

    /// <summary>Already decided, or superseded by a newer text: there is nothing left to review.</summary>
    NotWaiting,

    /// <summary>Weakly supported and approved in bulk: never — it is decided on its own (S-5).</summary>
    WeakSupport,

    /// <summary>Read from a picture and approved in bulk: never — the person who approves it has it open, with the picture (phase 13a, I-4).</summary>
    ReadFromAPicture,

    /// <summary>It says another value than the catalogue holds, and approved in bulk: never — which of the two is wrong is decided with both in front (phase 13a, I-4).</summary>
    ContradictsTheCatalogue,

    NotFound,
}

public sealed record ReviewClaimResult(ReviewOutcome Outcome, ClaimId ClaimId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        ReviewOutcome.Approved or ReviewOutcome.Rejected => AuditOutcome.Allowed,
        ReviewOutcome.NotWaiting or ReviewOutcome.NotFound or ReviewOutcome.WeakSupport or ReviewOutcome.ReadFromAPicture
            or ReviewOutcome.ContradictsTheCatalogue => AuditOutcome.Denied,
    };

    string? IAuditedResult.AuditReason =>
        Outcome is ReviewOutcome.Approved or ReviewOutcome.Rejected ? null : Outcome.ToString();
}

/// <summary>
/// Approves or rejects one claim. The reviewer is whoever the request
/// authenticated as — taken from the principal, never from the request body, so
/// the audit row and the claim name the same person.
/// </summary>
public sealed class ReviewClaimHandler(
    IKnowledgeRepository knowledge,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ICatalogueValues catalogue) : ICommandHandler<ReviewClaimCommand, ReviewClaimResult>
{
    public async Task<ReviewClaimResult> HandleAsync(ReviewClaimCommand command, CancellationToken cancellationToken)
    {
        var claim = await knowledge.FindClaimAsync(command.ClaimId, cancellationToken);
        if (claim is null)
            return new ReviewClaimResult(ReviewOutcome.NotFound, command.ClaimId);
        if (claim.Status != ClaimStatus.Proposed)
            return new ReviewClaimResult(ReviewOutcome.NotWaiting, command.ClaimId);

        // The server's rule, whatever a screen sent: a weak claim is opened and
        // decided on its own, never approved together with others.
        if (command.InBulk && command.Decision == ReviewDecision.Approve && claim.IsWeaklySupported)
            return new ReviewClaimResult(ReviewOutcome.WeakSupport, command.ClaimId);

        // And so is one read from a picture, unless it is the picture's own answer
        // and a sure one: otherwise the person is the only check that is not a model.
        // Nor one that says another value than the catalogue holds: approving
        // it replaces that value. Asked before the picture, as the queue says it.
        if (command.InBulk && command.Decision == ReviewDecision.Approve && await ContradictsAsync(claim, cancellationToken))
            return new ReviewClaimResult(ReviewOutcome.ContradictsTheCatalogue, command.ClaimId);

        if (command.InBulk && command.Decision == ReviewDecision.Approve && !PictureClaims.IsASureAnswer(claim)
            && await RestsOnAPictureAsync(claim, cancellationToken))
            return new ReviewClaimResult(ReviewOutcome.ReadFromAPicture, command.ClaimId);

        var principal = principals.Current;
        var reviewer = principal.DisplayName ?? principal.Subject ?? "unknown";

        if (command.Decision == ReviewDecision.Approve)
            claim.Approve(reviewer, clock);
        else
            claim.Reject(reviewer, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        ClaimsReviewed.Record(command.Decision, PictureClaims.IsASureAnswer(claim));

        return new ReviewClaimResult(
            command.Decision == ReviewDecision.Approve ? ReviewOutcome.Approved : ReviewOutcome.Rejected, claim.Id);
    }

    private async Task<bool> ContradictsAsync(ProductClaim claim, CancellationToken cancellationToken)
    {
        if (claim.AttributeCode is null)
            return false;

        // The comparison names no language: the culture only words what is shown.
        var held = await catalogue.OfAsync([claim.ProductId], "es", cancellationToken);
        return CatalogueAgreement.Of(claim, held.GetValueOrDefault(claim.ProductId)) is not null;
    }

    private async Task<bool> RestsOnAPictureAsync(ProductClaim claim, CancellationToken cancellationToken)
    {
        foreach (var evidence in claim.Evidence)
        {
            if (await knowledge.FindDocumentAsync(evidence.Document, cancellationToken) is { Kind: SourceKind.ProductImage })
                return true;
        }

        return false;
    }
}

/// <summary>
/// `tendero.knowledge.claims.reviewed` (the tracking plan): how many proposals a
/// person accepts. A falling approval rate is the extractor proposing what the
/// shop would not say — the prompt, the model or its settings to revisit.
/// </summary>
internal static class ClaimsReviewed
{
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Reviewed = Meter.CreateCounter<long>(
        "tendero.knowledge.claims.reviewed", unit: "{claim}", description: "Claims a person approved or rejected, by decision.");

    /// <param name="sureAnswer">Whether the claim was a picture's own answer the model was sure of: the ones a person may approve in bulk.</param>
    public static void Record(ReviewDecision decision, bool sureAnswer) =>
        Reviewed.Add(
            1,
            new KeyValuePair<string, object?>("decision", MetricLabels.Of(decision)),
            new KeyValuePair<string, object?>("sure_answer", sureAnswer ? "true" : "false"));
}

// ---------- The wire ----------

public sealed record ReviewClaimResponse(string ClaimId, ReviewOutcome Outcome);

public sealed record ReviewClaimConflict(string Title, string Detail);

public sealed class ReviewClaimsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/knowledge/claims?support=Weak&page=1&pageSize=20 — the review
        // queue, weakest support first. Every claim unless `support` says Weak:
        // which one a screen opens on is the screen's choice. `product` narrows it
        // to one product's, for its bulk review.
        app.MapGet("/api/knowledge/claims",
            async Task<Ok<ClaimsForReviewPage>> (
                ClaimSupportFilter? support, int? page, int? pageSize, Guid? product, string? culture,
                HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // The reviewer's language (ADR 0013): what the catalogue holds is worded in it.
                var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                var queue = await dispatcher.SendAsync(new ListClaimsForReviewQuery(
                    support ?? ClaimSupportFilter.All, page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize,
                    product is { } id ? new ProductId(id) : null, language), ct);
                http.Response.Headers.ContentLanguage = language;
                http.Response.Headers.Vary = "Accept-Language";
                return TypedResults.Ok(queue);
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("ListClaimsForReview");

        app.MapPost("/api/knowledge/claims/{id:guid}/approve",
                (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) => Review(id, ReviewDecision.Approve, dispatcher, ct))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("ApproveClaim");

        app.MapPost("/api/knowledge/claims/{id:guid}/reject",
                (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) => Review(id, ReviewDecision.Reject, dispatcher, ct))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("RejectClaim");
    }

    private static async Task<Results<Ok<ReviewClaimResponse>, NotFound, Conflict<ReviewClaimConflict>>> Review(
        Guid id, ReviewDecision decision, ICommandDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(new ReviewClaimCommand(new ClaimId(id), decision), ct);

        return result.Outcome switch
        {
            ReviewOutcome.NotFound => TypedResults.NotFound(),
            ReviewOutcome.NotWaiting or ReviewOutcome.WeakSupport => TypedResults.Conflict(new ReviewClaimConflict(
                "The claim is not waiting for review.",
                "It was already decided, or its description changed and a newer claim replaced it.")),
            _ => TypedResults.Ok(new ReviewClaimResponse(
                result.ClaimId.ToString(), result.Outcome)),
        };
    }
}
