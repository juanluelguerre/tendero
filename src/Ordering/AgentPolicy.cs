using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering;

/// <summary>
/// An agent about to place an order, as the shop's own rules see it (P11-6): who it
/// is and how far the shop trusts it, what it is about to charge, the mandate it
/// carries, and how busy it has been.
/// </summary>
public sealed record AgentAct(AgentId Agent, AgentStanding Standing, Money Total, MandateScope? Mandate, int OrdersLastHour);

/// <summary>Allowed, or denied by one named rule with the numbers that decided it.</summary>
public sealed record AgentVerdict(string? Rule, string? Detail)
{
    public static readonly AgentVerdict Allow = new(null, null);

    public bool Allowed => Rule is null;

    public static AgentVerdict Deny(string rule, string detail) => new(rule, detail);
}

/// <summary>
/// One of the shop's rules for agents — limits no protocol defines, applied on top
/// of whatever the person's mandate allows (analysis §7, layer 3). A rule is a
/// class and a registration; its numbers are configuration.
/// </summary>
public interface IAgentRule
{
    /// <summary>The rule's name, as a denial records it and the metric labels it.</summary>
    string Name { get; }

    AgentVerdict Evaluate(AgentAct act);
}

/// <summary>The shop's verdict on an agent's act: a port, so a real anti-fraud service can take the place of the table.</summary>
public interface IAgentPolicy
{
    AgentVerdict Evaluate(AgentAct act);
}

public sealed class AgentPolicyOptions
{
    public const string SectionName = "AgentPolicy";

    /// <summary>
    /// The most one order may total, by how far the shop trusts the agent. A trust
    /// level not listed has no cap — <c>Verified</c>, by default: the shop checked
    /// who operates it, and the person's mandate still bounds what it spends.
    /// </summary>
    public Dictionary<AgentTrust, decimal> OrderCaps { get; set; } = new()
    {
        [AgentTrust.Untrusted] = 100m,
        [AgentTrust.Known] = 1000m,
    };

    /// <summary>How many orders one agent may place in an hour, whatever its trust.</summary>
    public int MaxOrdersPerHour { get; set; } = 10;
}

/// <summary>A suspended agent places nothing: the shopkeeper said so, with a reason, in the registry.</summary>
public sealed class SuspendedAgentsPlaceNothing : IAgentRule
{
    public const string RuleName = "agent_suspended";

    public string Name => RuleName;

    public AgentVerdict Evaluate(AgentAct act) =>
        act.Standing.Suspended ? AgentVerdict.Deny(Name, $"{act.Agent} is suspended") : AgentVerdict.Allow;
}

/// <summary>An order above what the shop lets an agent of this trust spend in one go.</summary>
public sealed class OrderCapByTrust(IOptions<AgentPolicyOptions> options) : IAgentRule
{
    public const string RuleName = "order_over_trust_cap";

    public string Name => RuleName;

    public AgentVerdict Evaluate(AgentAct act) =>
        options.Value.OrderCaps.TryGetValue(act.Standing.Trust, out var cap) && act.Total.Amount > cap
            ? AgentVerdict.Deny(Name, $"{act.Total.Amount} {act.Total.Currency} over the {cap} cap for {MetricLabels.Of(act.Standing.Trust)} agents")
            : AgentVerdict.Allow;
}

/// <summary>Too many orders from one agent in the last hour: the shape of a loop, or of a stolen credential.</summary>
public sealed class OrdersPerAgentPerHour(IOptions<AgentPolicyOptions> options) : IAgentRule
{
    public const string RuleName = "orders_per_hour";

    public string Name => RuleName;

    public AgentVerdict Evaluate(AgentAct act) =>
        act.OrdersLastHour >= options.Value.MaxOrdersPerHour
            ? AgentVerdict.Deny(Name, $"{act.OrdersLastHour} orders in the last hour, the most is {options.Value.MaxOrdersPerHour}")
            : AgentVerdict.Allow;
}

/// <summary>
/// The rules as a table: each registered rule, in registration order, and the first
/// that denies decides. Every denial is counted by rule and trust —
/// <c>tendero.agents.denials</c> — and audited with the command that tried.
/// </summary>
public sealed class DeclarativeAgentPolicy(IEnumerable<IAgentRule> rules) : IAgentPolicy
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Denials = Meter.CreateCounter<long>(
        "tendero.agents.denials", unit: "{denial}",
        description: "Agent orders the shop's own rules refused, by rule and by how far the shop trusts the agent.");

    public AgentVerdict Evaluate(AgentAct act)
    {
        foreach (var rule in rules)
        {
            var verdict = rule.Evaluate(act);
            if (verdict.Allowed)
                continue;

            Denials.Add(1, new("rule", verdict.Rule), new(CheckoutAttempts.TrustTag, MetricLabels.Of(act.Standing.Trust)));
            return verdict;
        }

        return AgentVerdict.Allow;
    }
}
