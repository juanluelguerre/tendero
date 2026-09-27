using System.Diagnostics.Metrics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Knowledge.Features.ReviewClaims;

// ---------- The queue ----------

/// <summary>The claims waiting for a person, the weakest-supported first (ADR 0018, 5).</summary>
public sealed record ListClaimsForReviewQuery(int Limit = 100) : IQuery<IReadOnlyList<ClaimForReview>>;

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
    DateTimeOffset ProposedAt);

public sealed record QuoteForReview(string Culture, string Text, int Start, int End, double Support);

public sealed class ListClaimsForReviewHandler(IKnowledgeRepository knowledge)
    : IQueryHandler<ListClaimsForReviewQuery, IReadOnlyList<ClaimForReview>>
{
    public async Task<IReadOnlyList<ClaimForReview>> HandleAsync(ListClaimsForReviewQuery query, CancellationToken cancellationToken)
    {
        var claims = await knowledge.ClaimsAsync(ClaimStatus.Proposed, Math.Clamp(query.Limit, 1, 500), cancellationToken);
        var result = new List<ClaimForReview>(claims.Count);

        foreach (var claim in claims)
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
                claim.Provenance.Extractor, claim.Provenance.ModelId, claim.CreatedAt));
        }

        return result;
    }
}

// ---------- The decision ----------

public enum ReviewDecision
{
    Approve,
    Reject,
}

/// <summary>A person's decision on a claim — never a model's, never a chat message (invariant 12).</summary>
public sealed record ReviewClaimCommand(ClaimId ClaimId, ReviewDecision Decision) : ICommand<ReviewClaimResult>;

public enum ReviewOutcome
{
    Approved,
    Rejected,

    /// <summary>Already decided, or superseded by a newer text: there is nothing left to review.</summary>
    NotWaiting,

    NotFound,
}

public sealed record ReviewClaimResult(ReviewOutcome Outcome, ClaimId ClaimId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        ReviewOutcome.Approved or ReviewOutcome.Rejected => AuditOutcome.Allowed,
        ReviewOutcome.NotWaiting or ReviewOutcome.NotFound => AuditOutcome.Denied,
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

public sealed record ReviewClaimResponse(string ClaimId, string Outcome);

public sealed record ReviewClaimConflict(string Title, string Detail);

public sealed class ReviewClaimsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/knowledge/claims — the review queue, weakest support first.
        app.MapGet("/api/knowledge/claims",
            async Task<Ok<IReadOnlyList<ClaimForReview>>> (int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListClaimsForReviewQuery(limit ?? 100), ct)))
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
            ReviewOutcome.NotWaiting => TypedResults.Conflict(new ReviewClaimConflict(
                "The claim is not waiting for review.",
                "It was already decided, or its description changed and a newer claim replaced it.")),
            _ => TypedResults.Ok(new ReviewClaimResponse(
                result.ClaimId.ToString(), JsonNamingPolicy.CamelCase.ConvertName(result.Outcome.ToString()))),
        };
    }
}
