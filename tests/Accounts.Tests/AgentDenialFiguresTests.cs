using ElGuerre.Tendero.Accounts.Features.AgentDenials;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The dashboard's view of what agents were refused (P11-7): counted by the rule or
/// the protocol code that said no, by agent, newest first — out of audit rows the
/// denials already wrote.
/// </summary>
public sealed class AgentDenialFiguresTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static AuditEntry Denied(string agent, string? reason, int minutesAgo) => new(
        Guid.NewGuid(), "ElGuerre.Tendero.Ordering.Features.CheckoutSessions.CompleteCheckoutSessionCommand", "{}",
        null, new AgentId(agent), agent, agent, AuditOutcome.Denied, reason, null, Now.AddMinutes(-minutesAgo));

    [Theory]
    [InlineData("agent_refused: orders_per_hour: 10 orders in the last hour, the most is 10", "orders_per_hour", "10 orders in the last hour, the most is 10")]
    [InlineData("mandate_scope_mismatch: approved 49.85 EUR, now 54.80 EUR", "mandate_scope_mismatch", "approved 49.85 EUR, now 54.80 EUR")]
    [InlineData("NotFound", "NotFound", null)]
    [InlineData(null, "denied", null)]
    public void A_denial_is_counted_under_the_rule_that_said_no(string? audited, string reason, string? detail) =>
        Assert.Equal((reason, detail), AgentDenialFigures.ReasonOf(audited));

    [Fact]
    public void Denials_are_counted_by_reason_and_agent_and_the_latest_come_first()
    {
        var summary = AgentDenialFigures.Summarise(DashboardPeriod.Day,
        [
            Denied("claude-desktop", "agent_refused: order_over_trust_cap: 120 EUR over the 100 cap for untrusted agents", 30),
            Denied("claude-desktop", "agent_refused: order_over_trust_cap: 150 EUR over the 100 cap for untrusted agents", 10),
            Denied("other", "mandate_expired: payment", 5),
        ]);

        Assert.Equal(("day", 3), (summary.Period, summary.Total));
        Assert.Equal([new DenialCount("order_over_trust_cap", 2), new DenialCount("mandate_expired", 1)], summary.ByReason);
        Assert.Equal([new AgentDenialCount("claude-desktop", 2), new AgentDenialCount("other", 1)], summary.ByAgent);
        Assert.Equal(("other", "CompleteCheckoutSession", "mandate_expired"), (summary.Recent[0].AgentId, summary.Recent[0].Command, summary.Recent[0].Reason));
    }
}
