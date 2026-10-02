using System.Diagnostics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Assist.Features.DecideProposals;

public sealed record ApproveProposalsCommand(IReadOnlyList<ProposalId> Ids) : ICommand<ProposalDecisions>;

public sealed record RejectProposalsCommand(IReadOnlyList<ProposalId> Ids, string Reason) : ICommand<ProposalDecisions>;

public enum DecisionOutcome
{
    Applied,
    Failed,
    Stale,
    Rejected,
    NotFound,

    /// <summary>Already decided: rejected, stale, applied or failed.</summary>
    NotWaiting,
}

public sealed record ProposalDecision(ProposalId Id, DecisionOutcome Outcome, string? Reason);

/// <summary>One row per distinct id, in the order asked: the review shows each
/// row's result in place.</summary>
public sealed record ProposalDecisions(IReadOnlyList<ProposalDecision> Rows);

public sealed class ApproveProposalsValidator : AbstractValidator<ApproveProposalsCommand>
{
    public ApproveProposalsValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
    }
}

public sealed class RejectProposalsValidator : AbstractValidator<RejectProposalsCommand>
{
    public RejectProposalsValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A rejection says why: the reason is what a prompt change is judged against.")
            .MaximumLength(1000);
    }
}

/// <summary>
/// Approves proposals and runs their commands in the same request, as the person
/// approving (spec decision 9). Each row has its own DI scope: its command's
/// unit of work is its own, so a row that throws leaves nothing half-written for
/// the next one to save, and its scope names the proposal for the audit row.
/// </summary>
public sealed class ApproveProposalsHandler(
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IServiceScopeFactory scopes,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<ApproveProposalsCommand, ProposalDecisions>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<ProposalDecisions> HandleAsync(ApproveProposalsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.approve");
        var approver = Decider.Of(principals);
        var rows = new List<ProposalDecision>();

        foreach (var id in command.Ids.Distinct())
            rows.Add(await ApproveAsync(id, approver, cancellationToken));

        activity?.SetTag("assist.rows", rows.Count);
        activity?.SetTag("assist.applied", rows.Count(row => row.Outcome == DecisionOutcome.Applied));
        return new ProposalDecisions(rows);
    }

    private async Task<ProposalDecision> ApproveAsync(ProposalId id, string approver, CancellationToken cancellationToken)
    {
        var proposal = await proposals.FindAsync(id, cancellationToken);
        if (proposal is null)
            return new ProposalDecision(id, DecisionOutcome.NotFound, null);
        if (proposal.Status is not (ProposalStatus.Proposed or ProposalStatus.Approved))
            return new ProposalDecision(id, DecisionOutcome.NotWaiting, null);

        await using var scope = scopes.CreateAsyncScope();
        var entry = scope.ServiceProvider.GetRequiredService<ProposableCommands>().Find(proposal.Command);
        using var document = JsonDocument.Parse(proposal.Payload);
        var payload = document.RootElement;

        if (proposal.Status == ProposalStatus.Proposed)
        {
            // Stale when what the command would change is not what was proposed
            // against. An entry that no longer exists cannot say, and fails below.
            if (entry is not null && !Snapshots.Same(await entry.NowAsync(payload, cancellationToken), proposal.Before))
            {
                proposal.MarkStale(approver, clock);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new ProposalDecision(id, DecisionOutcome.Stale, null);
            }

            // Saved before running: if the process dies now, the list shows
            // "approved, result unknown" and approving again finishes it.
            proposal.Approve(approver, clock);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var application = await ApplyAsync(entry, proposal, payload, scope.ServiceProvider, cancellationToken);
        if (application.Applied)
            proposal.RecordApplied();
        else
            proposal.RecordFailed(application.Reason!);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProposalDecision(id, application.Applied ? DecisionOutcome.Applied : DecisionOutcome.Failed, application.Reason);
    }

    private static async Task<ProposalApplication> ApplyAsync(
        IProposableCommand? entry, ActionProposal proposal, JsonElement payload, IServiceProvider scope, CancellationToken cancellationToken)
    {
        if (entry is null)
            return ProposalApplication.Failed($"No proposable command is named {proposal.Command}.");

        scope.GetRequiredService<ProposalScope>().Enter(proposal.Id);
        try
        {
            return await entry.ApplyAsync(payload, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // One row's failure stops none of the others; its reason is the row's.
            return ProposalApplication.Failed(failure.Message);
        }
    }
}

public sealed class RejectProposalsHandler(
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<RejectProposalsCommand, ProposalDecisions>
{
    public async Task<ProposalDecisions> HandleAsync(RejectProposalsCommand command, CancellationToken cancellationToken)
    {
        var rejecter = Decider.Of(principals);
        var rows = new List<ProposalDecision>();

        foreach (var id in command.Ids.Distinct())
        {
            var proposal = await proposals.FindAsync(id, cancellationToken);
            if (proposal is null)
            {
                rows.Add(new ProposalDecision(id, DecisionOutcome.NotFound, null));
                continue;
            }
            if (proposal.Status != ProposalStatus.Proposed)
            {
                rows.Add(new ProposalDecision(id, DecisionOutcome.NotWaiting, null));
                continue;
            }

            proposal.Reject(rejecter, command.Reason, clock);
            rows.Add(new ProposalDecision(id, DecisionOutcome.Rejected, command.Reason));
        }

        if (rows.Any(row => row.Outcome == DecisionOutcome.Rejected))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProposalDecisions(rows);
    }
}

/// <summary>
/// Two snapshots of "now" are the same when they say the same thing. Compared by
/// value, never as text: the stored one comes back from a jsonb column, which
/// keeps neither the spacing nor the key order it was written with.
/// </summary>
internal static class Snapshots
{
    public static bool Same(string? now, string? before)
    {
        if (now is null || before is null)
            return now is null && before is null;

        using var a = JsonDocument.Parse(now);
        using var b = JsonDocument.Parse(before);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }
}

/// <summary>Whoever the request authenticated as, never a value from the body —
/// so the proposal and the audit row name the same person.</summary>
internal static class Decider
{
    public static string Of(IPrincipalAccessor principals) =>
        principals.Current.DisplayName ?? principals.Current.Subject ?? "unknown";
}

// ---------- The wire ----------

public sealed record ApproveProposalsRequest(IReadOnlyList<Guid> Ids);

public sealed record RejectProposalsRequest(IReadOnlyList<Guid> Ids, string Reason);

public sealed record ProposalDecisionView(string Id, DecisionOutcome Outcome, string? Reason);

public sealed record ProposalDecisionsResponse(IReadOnlyList<ProposalDecisionView> Rows);

public sealed class DecideProposalsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/steward/proposals/approve",
                async Task<Ok<ProposalDecisionsResponse>> (ApproveProposalsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    View(await dispatcher.SendAsync(new ApproveProposalsCommand([.. body.Ids.Select(id => new ProposalId(id))]), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("ApproveProposals");

        app.MapPost("/api/steward/proposals/reject",
                async Task<Ok<ProposalDecisionsResponse>> (RejectProposalsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    View(await dispatcher.SendAsync(new RejectProposalsCommand([.. body.Ids.Select(id => new ProposalId(id))], body.Reason), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("RejectProposals");
    }

    private static Ok<ProposalDecisionsResponse> View(ProposalDecisions decisions) =>
        TypedResults.Ok(new ProposalDecisionsResponse(
            [.. decisions.Rows.Select(row => new ProposalDecisionView(row.Id.ToString(), row.Outcome, row.Reason))]));
}
