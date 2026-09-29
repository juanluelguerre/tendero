using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using static ElGuerre.Tendero.Pricing.Tests.Build;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// Campaigns for the tests, with every default named: a week-long event that
/// starts a day after <see cref="Build.Now"/> and groups SUMMER25.
/// </summary>
internal static class Campaigns
{
    public static readonly DateTimeOffset DayAfter = Now.AddDays(1);

    public static TestClock Clock(DateTimeOffset? at = null) => new(at ?? Now);

    public static CampaignDetails Details(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        int priority = 10,
        CampaignKind kind = CampaignKind.Event,
        string? category = null,
        CampaignImage? image = null,
        string[]? promotions = null)
    {
        var starts = startsAt ?? DayAfter;
        return new CampaignDetails(
            Text("Tendero Days", "Tendero Days"),
            Text("Hasta −25 % en ropa", "Up to 25% off clothing"),
            kind,
            category,
            starts,
            endsAt ?? starts.AddDays(7),
            priority,
            promotions ?? ["SUMMER25"],
            image);
    }

    /// <summary>A draft, created at <see cref="Build.Now"/>.</summary>
    public static Campaign Draft(string code = "TENDERO_DAYS", CampaignDetails? details = null) =>
        Campaign.Create(Clock(), new CampaignCode(code), details ?? Details());

    /// <summary>Created and scheduled at <see cref="Build.Now"/>, its events cleared.</summary>
    public static Campaign Scheduled(string code = "TENDERO_DAYS", CampaignDetails? details = null)
    {
        var campaign = Draft(code, details);
        campaign.Schedule(Clock());
        campaign.ClearDomainEvents();
        return campaign;
    }
}
