using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.ManageAgents;

// Know Your Agent's registry (P11-2): the agents a shopkeeper knows, how far each
// is trusted, and the switch that stops one. Every change is a command, so every
// change is an audit row with the shopkeeper's name on it.

// ---------- The registry ----------

public sealed record ListAgentsQuery : IQuery<IReadOnlyList<AgentView>>;

public sealed record AgentView(
    string AgentId,
    string DisplayName,
    string Operator,
    AgentTrust Trust,
    AgentStatus Status,
    string? SuspensionReason,
    DateTimeOffset RegisteredAt,
    DateTimeOffset UpdatedAt)
{
    public static AgentView From(Agent agent) => new(
        agent.Id.Value,
        agent.DisplayName,
        agent.Operator,
        agent.Trust,
        agent.Status,
        agent.SuspensionReason,
        agent.RegisteredAt,
        agent.UpdatedAt);
}

public sealed class ListAgentsHandler(IAgentRepository agents) : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentView>>
{
    public async Task<IReadOnlyList<AgentView>> HandleAsync(ListAgentsQuery query, CancellationToken cancellationToken) =>
        [.. (await agents.AllAsync(cancellationToken)).Select(AgentView.From)];
}

// ---------- The changes ----------

public enum AgentChange
{
    Registered,
    Updated,

    /// <summary>Nothing to do: already so. Not a refusal.</summary>
    Unchanged,

    /// <summary>Registering an id that is already registered.</summary>
    AlreadyRegistered,

    NotFound,
}

public sealed record AgentResult(AgentChange Change, AgentView? Agent) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome =>
        Change is AgentChange.AlreadyRegistered or AgentChange.NotFound ? AuditOutcome.Denied : AuditOutcome.Allowed;

    string? IAuditedResult.AuditReason =>
        Change is AgentChange.AlreadyRegistered or AgentChange.NotFound ? Change.ToString() : null;
}

public sealed record RegisterAgentCommand(AgentId AgentId, string DisplayName, string Operator, AgentTrust Trust) : ICommand<AgentResult>;

public sealed record ChangeAgentTrustCommand(AgentId AgentId, AgentTrust Trust) : ICommand<AgentResult>;

public sealed record SuspendAgentCommand(AgentId AgentId, string Reason) : ICommand<AgentResult>;

public sealed record ReinstateAgentCommand(AgentId AgentId) : ICommand<AgentResult>;

public sealed class RegisterAgentValidator : AbstractValidator<RegisterAgentCommand>
{
    public RegisterAgentValidator()
    {
        RuleFor(command => command.AgentId.Value).NotEmpty().MaximumLength(100);
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Operator).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Trust).NotEqual(AgentTrust.Untrusted)
            .WithMessage("A registered agent is at least known.");
    }
}

public sealed class ChangeAgentTrustValidator : AbstractValidator<ChangeAgentTrustCommand>
{
    public ChangeAgentTrustValidator() =>
        RuleFor(command => command.Trust).NotEqual(AgentTrust.Untrusted)
            .WithMessage("A registered agent is at least known; suspend it instead.");
}

public sealed class SuspendAgentValidator : AbstractValidator<SuspendAgentCommand>
{
    public SuspendAgentValidator() => RuleFor(command => command.Reason).NotEmpty().MaximumLength(300);
}

/// <summary>
/// The four changes a shopkeeper makes to the registry. One handler because they
/// share everything but the one line that changes the aggregate — the load, the
/// not-found, the save, the answer — and four copies of that is how one of them
/// forgets to save.
/// </summary>
public sealed class ManageAgentsHandler(IAgentRepository agents, IUnitOfWork unitOfWork, TimeProvider clock) :
    ICommandHandler<RegisterAgentCommand, AgentResult>,
    ICommandHandler<ChangeAgentTrustCommand, AgentResult>,
    ICommandHandler<SuspendAgentCommand, AgentResult>,
    ICommandHandler<ReinstateAgentCommand, AgentResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<AgentResult> HandleAsync(RegisterAgentCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.register_agent");
        activity?.SetTag("agent.id", command.AgentId.Value);

        if (await agents.FindByIdAsync(command.AgentId, cancellationToken) is not null)
            return new AgentResult(AgentChange.AlreadyRegistered, null);

        var agent = Agent.Register(clock, command.AgentId, command.DisplayName, command.Operator, command.Trust);
        agents.Add(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentResult(AgentChange.Registered, AgentView.From(agent));
    }

    public Task<AgentResult> HandleAsync(ChangeAgentTrustCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(command.AgentId, "accounts.change_agent_trust", agent => agent.Trust != command.Trust, agent => agent.ChangeTrust(clock, command.Trust), cancellationToken);

    public Task<AgentResult> HandleAsync(SuspendAgentCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(command.AgentId, "accounts.suspend_agent", agent => agent.Status == AgentStatus.Active, agent => agent.Suspend(clock, command.Reason), cancellationToken);

    public Task<AgentResult> HandleAsync(ReinstateAgentCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(command.AgentId, "accounts.reinstate_agent", agent => agent.Status == AgentStatus.Suspended, agent => agent.Reinstate(clock), cancellationToken);

    private async Task<AgentResult> ChangeAsync(
        AgentId id, string operation, Func<Agent, bool> changes, Action<Agent> change, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity(operation);
        activity?.SetTag("agent.id", id.Value);

        var agent = await agents.FindByIdAsync(id, cancellationToken);
        if (agent is null)
            return new AgentResult(AgentChange.NotFound, null);

        // Suspending a suspended agent is not an error, and not a change either:
        // no event, no save, the same answer a second click deserves.
        if (!changes(agent))
            return new AgentResult(AgentChange.Unchanged, AgentView.From(agent));

        change(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentResult(AgentChange.Updated, AgentView.From(agent));
    }
}

// ---------- The wire ----------

/// <param name="Trust">Known or Verified; Untrusted is what an unregistered agent is, and the validator refuses it.</param>
public sealed record RegisterAgentRequest(string AgentId, string DisplayName, string Operator, AgentTrust Trust = AgentTrust.Known);

/// <param name="Trust">Known or Verified; to stop trusting an agent, suspend it.</param>
public sealed record ChangeAgentTrustRequest(AgentTrust Trust);

public sealed record SuspendAgentRequest(string Reason);

public sealed record AgentChangeResponse(AgentChange Change, AgentView Agent);

public static class AgentRefusals
{
    public static readonly Refusal AlreadyRegistered = new(
        "AlreadyRegistered", 409, "Ese agente ya está registrado.", "That agent is already registered.");
}

public sealed class ManageAgentsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var agents = app.MapGroup("/api/agents")
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Accounts");

        agents.MapGet("/", async Task<Ok<IReadOnlyList<AgentView>>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListAgentsQuery(), ct)))
            .WithName("ListAgents");

        agents.MapPost("/", async Task<Results<Ok<AgentChangeResponse>, ProblemHttpResult>> (
                RegisterAgentRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new RegisterAgentCommand(new AgentId(request.AgentId.Trim()), request.DisplayName, request.Operator, request.Trust), ct);

                return result.Change == AgentChange.AlreadyRegistered
                    ? TypedResults.Problem(
                        statusCode: AgentRefusals.AlreadyRegistered.Status, extensions: AgentRefusals.AlreadyRegistered.Said(path: "$.agentId"))
                    : TypedResults.Ok(Answer(result));
            })
            .WithMetadata(new Refuses(AgentRefusals.AlreadyRegistered))
            .WithName("RegisterAgent");

        agents.MapPost("/{agentId}/trust", async Task<Results<Ok<AgentChangeResponse>, NotFound>> (
                string agentId, ChangeAgentTrustRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Found(await dispatcher.SendAsync(new ChangeAgentTrustCommand(new AgentId(agentId), request.Trust), ct)))
            .WithName("ChangeAgentTrust");

        agents.MapPost("/{agentId}/suspend", async Task<Results<Ok<AgentChangeResponse>, NotFound>> (
                string agentId, SuspendAgentRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Found(await dispatcher.SendAsync(new SuspendAgentCommand(new AgentId(agentId), request.Reason), ct)))
            .WithName("SuspendAgent");

        agents.MapPost("/{agentId}/reinstate", async Task<Results<Ok<AgentChangeResponse>, NotFound>> (
                string agentId, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Found(await dispatcher.SendAsync(new ReinstateAgentCommand(new AgentId(agentId)), ct)))
            .WithName("ReinstateAgent");
    }

    private static Results<Ok<AgentChangeResponse>, NotFound> Found(AgentResult result) =>
        result.Change == AgentChange.NotFound ? TypedResults.NotFound() : TypedResults.Ok(Answer(result));

    private static AgentChangeResponse Answer(AgentResult result) =>
        new(result.Change, result.Agent!);
}
