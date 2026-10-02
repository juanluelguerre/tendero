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
/// approving (spec decision 9).
///
/// Each row has two DI scopes of its own. The proposal is read and saved in one,
/// so a row whose result cannot be saved leaves nothing modified for the next
/// row's save to retry; its command runs in the other, so a command that throws
/// leaves nothing half-written either, and that scope names the proposal for the
/// command's audit row. Whatever goes wrong in a row is that row's answer, never
/// the batch's.
/// </summary>
public sealed class ApproveProposalsHandler(
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
        {
            // A client that left stops the batch between rows, never inside one.
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(await DecideAsync(id, approver, cancellationToken));
        }

        activity?.SetTag("assist.rows", rows.Count);
        activity?.SetTag("assist.applied", rows.Count(row => row.Outcome == DecisionOutcome.Applied));
        return new ProposalDecisions(rows);
    }

    private async Task<ProposalDecision> DecideAsync(ProposalId id, string approver, CancellationToken cancellationToken)
    {
        try
        {
            return await ApproveAsync(id, approver, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The rows before this one stay reported and the rows after it still
            // run; this one is Failed, or left Approved for a retry to finish.
            return new ProposalDecision(id, DecisionOutcome.Failed, $"This row could not be completed: {failure.Message}");
        }
    }

    private async Task<ProposalDecision> ApproveAsync(ProposalId id, string approver, CancellationToken cancellationToken)
    {
        await using var books = scopes.CreateAsyncScope();
        var proposals = books.ServiceProvider.GetRequiredService<IProposalRepository>();
        var unitOfWork = books.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var proposal = await proposals.FindAsync(id, cancellationToken);
        if (proposal is null)
            return new ProposalDecision(id, DecisionOutcome.NotFound, null);

        // Approved moments ago may still be running in another request; only an
        // approval old enough to have died is finished again.
        var retrying = proposal.MayRetry(clock);
        if (proposal.Status != ProposalStatus.Proposed && !retrying)
            return new ProposalDecision(id, DecisionOutcome.NotWaiting, null);

        await using var scope = scopes.CreateAsyncScope();
        var entry = scope.ServiceProvider.GetRequiredService<ProposableCommands>().Find(proposal.Command);
        using var document = JsonDocument.Parse(proposal.Payload);
        var payload = document.RootElement;

        // Changed when what the command would change is not what was proposed
        // against. An entry that no longer exists cannot say, and fails below.
        var changed = entry is not null
            && !Snapshots.Same(await entry.NowAsync(payload, cancellationToken), proposal.Before);
        var stale = changed && !retrying;

        if (stale)
            proposal.MarkStale(approver, clock);
        else if (retrying)
            proposal.Retry(approver, clock);
        else
            proposal.Approve(approver, clock);

        // The claim, saved before anything runs: the proposal's version makes it
        // one request's alone, and the one that saved second is told so. If the
        // process dies after it, the list shows "approved, result unknown".
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentWriteException)
        {
            return new ProposalDecision(id, DecisionOutcome.NotWaiting, null);
        }

        if (stale)
            return new ProposalDecision(id, DecisionOutcome.Stale, null);

        // From the claim on, the row finishes even if the client has left: a
        // command that ran and a result never recorded is the one state a person
        // could not read back.
        var application = retrying && changed
            // A retry cannot tell its own command having run before the process
            // died from a person's edit since: running again could overwrite one.
            ? ProposalApplication.Failed("It changed after it was approved — it may have been applied before the approval stopped. Nothing was run; check it before proposing it again.")
            : await ApplyAsync(entry, proposal, payload, scope.ServiceProvider, CancellationToken.None);
        if (application.Applied)
            proposal.RecordApplied();
        else
            proposal.RecordFailed(application.Reason!);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

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

        try
        {
            using var a = JsonDocument.Parse(now);
            using var b = JsonDocument.Parse(before);
            return JsonElement.DeepEquals(a.RootElement, b.RootElement);
        }
        catch (JsonException)
        {
            // A snapshot that cannot be read cannot be shown to be the same.
            return false;
        }
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
                    View(await dispatcher.SendAsync(Approval(body), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("ApproveProposals");

        app.MapPost("/api/steward/proposals/reject",
                async Task<Ok<ProposalDecisionsResponse>> (RejectProposalsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                    View(await dispatcher.SendAsync(Rejection(body), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("RejectProposals");
    }

    internal static ApproveProposalsCommand Approval(ApproveProposalsRequest body) =>
        new([.. (body.Ids ?? []).Select(id => new ProposalId(id))]);

    internal static RejectProposalsCommand Rejection(RejectProposalsRequest body) =>
        new([.. (body.Ids ?? []).Select(id => new ProposalId(id))], body.Reason);

    private static Ok<ProposalDecisionsResponse> View(ProposalDecisions decisions) =>
        TypedResults.Ok(new ProposalDecisionsResponse(
            [.. decisions.Rows.Select(row => new ProposalDecisionView(row.Id.ToString(), row.Outcome, row.Reason))]));
}
