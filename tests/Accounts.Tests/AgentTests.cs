using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ManageAgents;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// Know Your Agent's registry (P11-2): what the shop thinks of an agent, apart
/// from who the token says it is — and every change a command with an audit row.
/// </summary>
public sealed class AgentTests
{
    private static readonly TestClock Clock = new();
    private static readonly AgentId Claude = new("claude-desktop");

    [Fact]
    public void A_registered_agent_is_at_least_known_active_and_says_so_in_its_standing()
    {
        var agent = Agent.Register(Clock, Claude, " Claude Desktop ", " Anthropic ");

        Assert.Equal(("Claude Desktop", "Anthropic", AgentTrust.Known, AgentStatus.Active), (agent.DisplayName, agent.Operator, agent.Trust, agent.Status));
        Assert.Equal(new AgentStanding(AgentTrust.Known, false), agent.Standing);
        Assert.Contains(agent.DomainEvents, e => e is AgentRegistered { Trust: AgentTrust.Known });
        Assert.Throws<InvalidOperationException>(() => Agent.Register(Clock, Claude, "x", "y", AgentTrust.Untrusted));
    }

    [Fact]
    public void Suspending_keeps_the_reason_until_it_is_reinstated_and_neither_happens_twice()
    {
        var agent = Agent.Register(Clock, Claude, "Claude Desktop", "Anthropic");

        agent.Suspend(Clock, " refunds abused ");
        Assert.Equal((AgentStatus.Suspended, "refunds abused", true), (agent.Status, agent.SuspensionReason, agent.Standing.Suspended));
        Assert.Throws<InvalidOperationException>(() => agent.Suspend(Clock, "again"));

        agent.Reinstate(Clock);
        Assert.Equal((AgentStatus.Active, (string?)null), (agent.Status, agent.SuspensionReason));
        Assert.Throws<InvalidOperationException>(() => agent.Reinstate(Clock));
    }

    [Fact]
    public void Trust_moves_between_known_and_verified_and_never_back_to_untrusted()
    {
        var agent = Agent.Register(Clock, Claude, "Claude Desktop", "Anthropic");
        agent.ClearDomainEvents();

        agent.ChangeTrust(Clock, AgentTrust.Verified);
        agent.ChangeTrust(Clock, AgentTrust.Verified);

        Assert.Equal(AgentTrust.Verified, agent.Trust);
        Assert.Single(agent.DomainEvents, e => e is AgentTrustChanged { From: AgentTrust.Known, To: AgentTrust.Verified });
        Assert.Throws<InvalidOperationException>(() => agent.ChangeTrust(Clock, AgentTrust.Untrusted));
    }

    [Fact]
    public async Task Registering_twice_is_refused_and_audited_as_a_denial()
    {
        var registry = new InMemoryAgents();
        var handler = new ManageAgentsHandler(registry, registry, Clock);

        var first = await handler.HandleAsync(new RegisterAgentCommand(Claude, "Claude Desktop", "Anthropic", AgentTrust.Known), TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(new RegisterAgentCommand(Claude, "Other", "Other", AgentTrust.Verified), TestContext.Current.CancellationToken);

        Assert.Equal((AgentChange.Registered, "known", "active"), (first.Change, first.Agent!.Trust, first.Agent.Status));
        Assert.Equal(AgentChange.AlreadyRegistered, second.Change);
        Assert.Equal((AuditOutcome.Denied, "AlreadyRegistered"), (((IAuditedResult)second).AuditOutcome, ((IAuditedResult)second).AuditReason));
        Assert.Equal(1, registry.Saves);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_saves_nothing_and_an_unknown_agent_is_not_found()
    {
        var registry = new InMemoryAgents(Agent.Register(Clock, Claude, "Claude Desktop", "Anthropic"));
        var handler = new ManageAgentsHandler(registry, registry, Clock);
        var ct = TestContext.Current.CancellationToken;

        var suspended = await handler.HandleAsync(new SuspendAgentCommand(Claude, "chargebacks"), ct);
        var again = await handler.HandleAsync(new SuspendAgentCommand(Claude, "chargebacks"), ct);
        var trusted = await handler.HandleAsync(new ChangeAgentTrustCommand(Claude, AgentTrust.Verified), ct);
        var reinstated = await handler.HandleAsync(new ReinstateAgentCommand(Claude), ct);
        var nobody = await handler.HandleAsync(new ReinstateAgentCommand(new AgentId("stranger")), ct);

        Assert.Equal(
            [AgentChange.Updated, AgentChange.Unchanged, AgentChange.Updated, AgentChange.Updated, AgentChange.NotFound],
            new[] { suspended, again, trusted, reinstated, nobody }.Select(result => result.Change));
        Assert.Equal(("verified", "active"), (reinstated.Agent!.Trust, reinstated.Agent.Status));
        Assert.Equal(3, registry.Saves);
        Assert.Equal(AuditOutcome.Allowed, ((IAuditedResult)again).AuditOutcome);
        Assert.Equal(AuditOutcome.Denied, ((IAuditedResult)nobody).AuditOutcome);
    }

    [Fact]
    public void A_registration_the_domain_would_refuse_is_refused_before_it_gets_there()
    {
        Assert.False(new RegisterAgentValidator().Validate(new RegisterAgentCommand(Claude, "Claude", "Anthropic", AgentTrust.Untrusted)).IsValid);
        Assert.False(new RegisterAgentValidator().Validate(new RegisterAgentCommand(new AgentId(""), "Claude", "Anthropic", AgentTrust.Known)).IsValid);
        Assert.False(new ChangeAgentTrustValidator().Validate(new ChangeAgentTrustCommand(Claude, AgentTrust.Untrusted)).IsValid);
        Assert.False(new SuspendAgentValidator().Validate(new SuspendAgentCommand(Claude, " ")).IsValid);
    }

    private sealed class InMemoryAgents(params Agent[] agents) : IAgentRepository, IUnitOfWork
    {
        private readonly List<Agent> agents = [.. agents];

        public int Saves { get; private set; }

        public Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default) =>
            Task.FromResult(this.agents.FirstOrDefault(agent => agent.Id == id));

        public Task<IReadOnlyList<Agent>> AllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Agent>>(this.agents);

        public void Add(Agent agent) => this.agents.Add(agent);

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
