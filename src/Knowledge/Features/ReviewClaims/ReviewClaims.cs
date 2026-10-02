using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
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
public sealed record ListClaimsForReviewQuery(
    ClaimSupportFilter Support = ClaimSupportFilter.All, int Page = Paging.FirstPage, int PageSize = Paging.DefaultPageSize,
    ProductId? Product = null)
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
    bool IsWeak);

public sealed record QuoteForReview(string Culture, string Text, int Start, int End, double Support);

public sealed class ListClaimsForReviewHandler(IKnowledgeRepository knowledge)
    : IQueryHandler<ListClaimsForReviewQuery, ClaimsForReviewPage>
{
    public async Task<ClaimsForReviewPage> HandleAsync(ListClaimsForReviewQuery query, CancellationToken cancellationToken)
    {
        var page = await knowledge.ClaimsAsync(ClaimStatus.Proposed, query.Support, query.Page, query.PageSize, query.Product, cancellationToken);
        var result = new List<ClaimForReview>(page.Claims.Count);

        foreach (var claim in page.Claims)
        {
            var quotes = new List<QuoteForReview>();
            foreach (var evidence in claim.Evidence)
            {
                // A quote's culture is its document's; the documents are few per product.
                var document = await knowledge.FindDocumentAsync(evidence.Document, cancellationToken);
                quotes.Add(new QuoteForReview(document?.Culture ?? "", evidence.Quote.Text, evidence.Quote.Start, evidence.Quote.End, evidence.Support));
            }

            result.Add(new ClaimForReview(
                claim.Id.ToString(), claim.ProductId.ToString(),
                claim.Statement.In("es"), claim.Statement.In("en"),
                claim.AttributeCode, claim.Value, claim.Support, quotes,
                claim.Provenance.Extractor, claim.Provenance.ModelId, claim.CreatedAt,
                claim.IsWeaklySupported));
        }

        return new ClaimsForReviewPage(
            result, page.Total, query.Page, query.PageSize,
            [.. Enum.GetValues<ClaimSupportFilter>().Select(filter =>
                new ClaimSupportCount(filter, page.CountBySupport.GetValueOrDefault(filter)))]);
    }
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

    NotFound,
}

public sealed record ReviewClaimResult(ReviewOutcome Outcome, ClaimId ClaimId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        ReviewOutcome.Approved or ReviewOutcome.Rejected => AuditOutcome.Allowed,
        ReviewOutcome.NotWaiting or ReviewOutcome.NotFound or ReviewOutcome.WeakSupport => AuditOutcome.Denied,
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
    TimeProvider clock) : ICommandHandler<ReviewClaimCommand, ReviewClaimResult>
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

        var principal = principals.Current;
        var reviewer = principal.DisplayName ?? principal.Subject ?? "unknown";

        if (command.Decision == ReviewDecision.Approve)
            claim.Approve(reviewer, clock);
        else
            claim.Reject(reviewer, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        ClaimsReviewed.Record(command.Decision);

        return new ReviewClaimResult(
            command.Decision == ReviewDecision.Approve ? ReviewOutcome.Approved : ReviewOutcome.Rejected, claim.Id);
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

    public static void Record(ReviewDecision decision) =>
        Reviewed.Add(1, new KeyValuePair<string, object?>("decision", MetricLabels.Of(decision)));
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
                   ClaimSupportFilter? support, int? page, int? pageSize, Guid? product, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListClaimsForReviewQuery(
                    support ?? ClaimSupportFilter.All, page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize,
                    product is { } id ? new ProductId(id) : null), ct)))
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
