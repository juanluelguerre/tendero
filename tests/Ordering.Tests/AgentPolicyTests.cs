using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The shop's own rules for agents (P11-6): a table the first denial decides, each
/// rule named in what it refuses, and every refusal counted by rule and by trust.
/// </summary>
public sealed class AgentPolicyTests
{
    private static readonly IOptions<AgentPolicyOptions> Defaults = Options.Create(new AgentPolicyOptions());

    private static AgentAct Act(AgentTrust trust = AgentTrust.Known, bool suspended = false, decimal total = 50m, int lastHour = 0) =>
        new(new AgentId("claude-desktop"), new AgentStanding(trust, suspended), new Money(total, "EUR"), null, lastHour);

    private static DeclarativeAgentPolicy Policy() =>
        new([new SuspendedAgentsPlaceNothing(), new OrderCapByTrust(Defaults), new OrdersPerAgentPerHour(Defaults)]);

    [Fact]
    public void An_ordinary_order_from_a_known_agent_is_allowed() =>
        Assert.True(Policy().Evaluate(Act()).Allowed);

    [Theory]
    [InlineData(AgentTrust.Untrusted, "100.00", true)]
    [InlineData(AgentTrust.Untrusted, "100.01", false)]
    [InlineData(AgentTrust.Known, "1000.00", true)]
    [InlineData(AgentTrust.Known, "1000.01", false)]
    [InlineData(AgentTrust.Verified, "100000.00", true)]
    public void Each_trust_level_has_its_cap_and_a_verified_agent_has_none(AgentTrust trust, string total, bool allowed)
    {
        var verdict = Policy().Evaluate(Act(trust, total: decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(allowed, verdict.Allowed);
        if (!allowed)
            Assert.Equal(OrderCapByTrust.RuleName, verdict.Rule);
    }

    [Fact]
    public void The_hourly_count_is_a_ceiling_the_next_order_would_cross()
    {
        Assert.True(Policy().Evaluate(Act(lastHour: 9)).Allowed);
        Assert.Equal(OrdersPerAgentPerHour.RuleName, Policy().Evaluate(Act(lastHour: 10)).Rule);
    }

    /// <summary>A suspended agent is refused as suspended, even when it is also over its cap: the first rule that denies is the reason given.</summary>
    [Fact]
    public void The_first_rule_that_denies_is_the_one_recorded_and_counted()
    {
        using var metrics = new InstrumentRecorder("tendero.agents.denials");

        var verdict = Policy().Evaluate(Act(AgentTrust.Untrusted, suspended: true, total: 5000m, lastHour: 50));

        Assert.Equal(SuspendedAgentsPlaceNothing.RuleName, verdict.Rule);
        Assert.Contains("claude-desktop", verdict.Detail, StringComparison.Ordinal);
        var denial = Assert.Single(metrics.Of("tendero.agents.denials"));
        Assert.Equal((SuspendedAgentsPlaceNothing.RuleName, "untrusted"), (denial["rule"], denial["agent.trust"]));
    }

    [Fact]
    public void An_allowed_act_is_not_counted()
    {
        using var metrics = new InstrumentRecorder("tendero.agents.denials");

        Policy().Evaluate(Act());

        Assert.Empty(metrics.Of("tendero.agents.denials"));
    }
}
