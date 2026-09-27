using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// Two values phase 11 put in the kernel, held where they live (Stryker's phase-11
/// close found the first held only by Ordering's tests): an agent nobody registered,
/// and the window a dashboard figure covers.
/// </summary>
public sealed class AgentStandingAndWindowTests
{
    /// <summary>Untrusted, and not suspended: there is nothing to suspend (ADR 0022).</summary>
    [Fact]
    public void An_agent_nobody_registered_is_untrusted_and_not_suspended() =>
        Assert.Equal(new AgentStanding(AgentTrust.Untrusted, false), AgentStanding.Unknown);

    [Theory]
    [InlineData(DashboardPeriod.Day, 1)]
    [InlineData(DashboardPeriod.Week, 7)]
    [InlineData(DashboardPeriod.Month, 30)]
    public void A_window_ends_now_and_the_previous_one_is_just_as_long_before_it(DashboardPeriod period, int days)
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        var window = DashboardWindow.Ending(now, period);

        Assert.Equal((now.AddDays(-days), now, now.AddDays(-2 * days)), (window.From, window.To, window.PreviousFrom));
    }
}
