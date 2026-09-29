using ElGuerre.Tendero.Pricing.Domain;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;
using static ElGuerre.Tendero.Pricing.Tests.Campaigns;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// Which campaigns the shop announces and which leads (R-8): running before
/// upcoming, then priority, then the earliest start — the server's rule, tested
/// against the clock before, during and after, with two overlapping.
/// </summary>
public sealed class CampaignLineupTests
{
    private static string[] Codes(IEnumerable<Campaign> campaigns) => [.. campaigns.Select(campaign => campaign.Code.Value)];

    [Fact]
    public void A_running_campaign_leads_an_upcoming_one_whatever_their_priorities()
    {
        var season = Scheduled("OTONO", Details(startsAt: Now.AddDays(-1), endsAt: Now.AddDays(60), priority: 1, kind: CampaignKind.Season));
        var comingEvent = Scheduled("TENDERO_DAYS", Details(startsAt: DayAfter, priority: 100));

        Assert.Equal(["OTONO", "TENDERO_DAYS"], Codes(CampaignLineup.Near([comingEvent, season], Now)));
    }

    [Fact]
    public void Of_two_running_at_once_the_higher_priority_leads()
    {
        var season = Scheduled("OTONO", Details(startsAt: Now.AddDays(-10), endsAt: Now.AddDays(60), priority: 10));
        var theEvent = Scheduled("TENDERO_DAYS", Details(startsAt: Now.AddDays(-1), priority: 100));

        Assert.Equal(["TENDERO_DAYS", "OTONO"], Codes(CampaignLineup.Near([season, theEvent], Now)));
    }

    [Fact]
    public void With_equal_priority_the_one_that_starts_first_leads()
    {
        var later = Scheduled("LATER", Details(startsAt: Now.AddDays(5)));
        var sooner = Scheduled("SOONER", Details(startsAt: Now.AddDays(2)));

        Assert.Equal(["SOONER", "LATER"], Codes(CampaignLineup.Near([later, sooner], Now)));
    }

    /// <summary>Everything equal, the code decides, so two identical campaigns never swap places between two answers.</summary>
    [Fact]
    public void With_everything_equal_the_code_decides()
    {
        var second = Scheduled("B_CAMPAIGN");
        var first = Scheduled("A_CAMPAIGN");

        Assert.Equal(["A_CAMPAIGN", "B_CAMPAIGN"], Codes(CampaignLineup.Near([second, first], Now)));
    }

    /// <summary>Thirty days ahead is announced; a minute beyond is not yet.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void An_upcoming_campaign_is_announced_within_the_horizon(int minutesBeyond, bool announced)
    {
        var campaign = Scheduled(details: Details(startsAt: Now + Campaign.Horizon + TimeSpan.FromMinutes(minutesBeyond)));

        Assert.Equal(announced, CampaignLineup.Near([campaign], Now).Count == 1);
    }

    [Fact]
    public void Drafts_ended_campaigns_and_closed_windows_are_never_announced()
    {
        var draft = Draft("DRAFT");
        var ended = Scheduled("ENDED");
        ended.End(Clock());
        var over = Scheduled("OVER");

        Assert.Empty(CampaignLineup.Near([draft, ended, over], DayAfter.AddDays(7)));
        Assert.Empty(CampaignLineup.Near([draft, ended], DayAfter));
    }
}
