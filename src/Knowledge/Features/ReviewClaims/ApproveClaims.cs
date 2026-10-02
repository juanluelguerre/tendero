using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Knowledge.Features.ReviewClaims;

/// <summary>
/// A product's claims approved together, after the review that shows every one of
/// them (phase 12, S-5; Steward spec, decision 5 — which amends R-2's refusal of
/// bulk approval for claims). Still reviewing claims, so in this slice: it
/// dispatches the one-claim command beside it. Not audited itself: each claim it
/// approves is a <see cref="ReviewClaimCommand"/>, audited one by one.
/// </summary>
public sealed record ApproveClaimsCommand(IReadOnlyList<ClaimId> Ids) : ICommand<ClaimApprovals>, IUnauditedCommand;

public enum BulkApprovalOutcome
{
    Approved,

    /// <summary>Its support is under the line a person is for: never approved in bulk,
    /// whatever the screen sent — it is opened and decided on its own.</summary>
    WeakSupport,

    /// <summary>Already decided, or replaced by a newer text.</summary>
    NotWaiting,

    NotFound,
}

public sealed record ClaimApproval(ClaimId ClaimId, BulkApprovalOutcome Outcome);

/// <summary>One row per distinct id, in the order asked: the review shows each in place.</summary>
public sealed record ClaimApprovals(IReadOnlyList<ClaimApproval> Rows);

public sealed class ApproveClaimsValidator : AbstractValidator<ApproveClaimsCommand>
{
    /// <summary>A product's claims are a handful; a hundred is the proposals' batch too.</summary>
    public const int MaxBatch = 100;

    public ApproveClaimsValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(MaxBatch);
    }
}

/// <summary>
/// Sends each claim through the one-claim command, in bulk, each in a DI scope of
/// its own: one claim's failure leaves nothing modified for the next one's save,
/// and each audit row names its claim.
/// </summary>
public sealed class ApproveClaimsHandler(IServiceScopeFactory scopes) : ICommandHandler<ApproveClaimsCommand, ClaimApprovals>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Knowledge);

    public async Task<ClaimApprovals> HandleAsync(ApproveClaimsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("knowledge.claims.approve_bulk");
        activity?.SetTag("knowledge.claims", command.Ids.Count);

        var rows = new List<ClaimApproval>(command.Ids.Count);
        foreach (var id in command.Ids.Distinct())
            rows.Add(new ClaimApproval(id, await ApproveAsync(id, cancellationToken)));

        activity?.SetTag("knowledge.claims.approved", rows.Count(row => row.Outcome == BulkApprovalOutcome.Approved));
        return new ClaimApprovals(rows);
    }

    /// <summary>
    /// The one-claim command decides every row, refusals included, so each is its
    /// own audit row — a weak claim pushed through a bulk approval is a denial on
    /// record (review of S-5, I-2). Two people deciding one claim at once is the
    /// one-claim path's case too: the last write wins, as it does there.
    /// </summary>
    private async Task<BulkApprovalOutcome> ApproveAsync(ClaimId id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new ReviewClaimCommand(id, ReviewDecision.Approve, InBulk: true), cancellationToken);

        return result.Outcome switch
        {
            ReviewOutcome.Approved => BulkApprovalOutcome.Approved,
            ReviewOutcome.WeakSupport => BulkApprovalOutcome.WeakSupport,
            ReviewOutcome.NotFound => BulkApprovalOutcome.NotFound,
            _ => BulkApprovalOutcome.NotWaiting,
        };
    }
}

// ---------- The wire ----------

public sealed record ApproveClaimsRequest(IReadOnlyList<Guid>? Ids);

public sealed record ClaimApprovalView(string ClaimId, BulkApprovalOutcome Outcome);

public sealed record ClaimApprovalsResponse(IReadOnlyList<ClaimApprovalView> Rows);

public sealed class ApproveClaimsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/knowledge/claims/approve { ids } — a product's claims after its
        // review; one row per claim, a weak one refused on its row.
        app.MapPost("/api/knowledge/claims/approve",
                async Task<Ok<ClaimApprovalsResponse>> (ApproveClaimsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var approvals = await dispatcher.SendAsync(
                        new ApproveClaimsCommand([.. (body.Ids ?? []).Select(id => new ClaimId(id))]), ct);
                    return TypedResults.Ok(new ClaimApprovalsResponse(
                        [.. approvals.Rows.Select(row => new ClaimApprovalView(row.ClaimId.ToString(), row.Outcome))]));
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("ApproveClaims");
    }
}
