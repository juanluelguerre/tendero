using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;

namespace ElGuerre.Tendero.Assist.Features.ProposeActions;

/// <param name="Payload">The command's fields as JSON — what a model's tool call
/// carries. The registry entry reads it; nothing here trusts it.</param>
public sealed record ProposedAction(string Command, JsonElement Payload, string Reason);

/// <summary>Which model, under which prompt, in which conversation. Who asked is
/// not here: it is the principal's.</summary>
public sealed record ProposalSource(string? Profile, string? Model, string? PromptVersion, string? ConversationId);

/// <summary>
/// Stores what a model proposed (phase 12), each action checked by the entry of
/// the context that owns its command. No endpoint: Steward's proposal tools (S-3)
/// dispatch it, and an invalid action goes back to the model as an error.
/// </summary>
public sealed record ProposeActionsCommand(IReadOnlyList<ProposedAction> Actions, ProposalSource Source)
    : ICommand<ProposeActionsResult>;

/// <param name="Index">The action's place in the command, so a model can tell which one failed.</param>
public sealed record ProposedRow(int Index, ProposalId? Id, IReadOnlyList<string> Errors);

public sealed record ProposeActionsResult(IReadOnlyList<ProposedRow> Rows);

public sealed class ProposeActionsValidator : AbstractValidator<ProposeActionsCommand>
{
    /// <summary>The longest payload a proposal stores, as JSON text.</summary>
    public const int PayloadMaxLength = 16 * 1024;

    public ProposeActionsValidator()
    {
        RuleFor(x => x.Actions).NotEmpty();
        RuleFor(x => x.Actions.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch);
        RuleFor(x => x.Source.Profile).MaximumLength(200);
        RuleFor(x => x.Source.Model).MaximumLength(200);
        RuleFor(x => x.Source.PromptVersion).MaximumLength(200);
        RuleFor(x => x.Source.ConversationId).MaximumLength(200);
        RuleForEach(x => x.Actions).ChildRules(action =>
        {
            action.RuleFor(a => a.Command).NotEmpty().MaximumLength(100);
            action.RuleFor(a => a.Payload.GetRawText().Length)
                .LessThanOrEqualTo(PayloadMaxLength)
                .WithMessage($"A payload holds at most {PayloadMaxLength} characters.");
            action.RuleFor(a => a.Reason)
                .Must(reason => !string.IsNullOrWhiteSpace(reason))
                .WithMessage("Every proposal says why.")
                .MaximumLength(2000);
        });
    }
}

public sealed class ProposeActionsHandler(
    ProposableCommands registry,
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    IPrincipalAccessor principals,
    TimeProvider clock) : ICommandHandler<ProposeActionsCommand, ProposeActionsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<ProposeActionsResult> HandleAsync(ProposeActionsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.propose");
        activity?.SetTag("assist.actions", command.Actions.Count);

        var principal = principals.Current;
        var provenance = new ProposalProvenance(
            command.Source.Profile,
            command.Source.Model,
            command.Source.PromptVersion,
            command.Source.ConversationId,
            Activity.Current?.TraceId.ToString(),
            principal.DisplayName ?? principal.Subject ?? "unknown");

        var rows = new List<ProposedRow>(command.Actions.Count);
        foreach (var (action, index) in command.Actions.Select((action, index) => (action, index)))
        {
            if (registry.Find(action.Command) is not { } entry)
            {
                rows.Add(new ProposedRow(index, null, [$"No proposable command is named {action.Command}."]));
                continue;
            }

            var check = await entry.CheckAsync(action.Payload, cancellationToken);
            if (!check.IsValid)
            {
                rows.Add(new ProposedRow(index, null, check.Errors));
                continue;
            }

            var proposal = ActionProposal.Propose(
                entry.Name, action.Payload.GetRawText(), check.Subject!, check.Now, action.Reason, provenance, clock);
            proposals.Add(proposal);
            rows.Add(new ProposedRow(index, proposal.Id, []));
        }

        if (rows.Any(row => row.Id is not null))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("assist.proposed", rows.Count(row => row.Id is not null));
        return new ProposeActionsResult(rows);
    }
}
