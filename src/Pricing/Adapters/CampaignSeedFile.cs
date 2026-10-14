using System.Text.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Adapters;

/// <summary>A campaign as the committed seed carries it: what to create, and whether to schedule it.</summary>
public sealed record CampaignSeed(CampaignCode Code, CampaignDetails Details, CampaignStatus Status);

/// <summary>
/// The shape of <c>seed/campaigns.sample.json</c>. Unlike the tariffs and the
/// promotions, campaigns are not read from the file on every request — a
/// shopkeeper writes them, so the file only fills an empty table once, and the
/// table is the truth from then on (the stock seed's argument). Public because
/// the tests load it directly: the file that ships is the file that is validated,
/// against the promotions it names.
/// </summary>
public static class CampaignSeedFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<IReadOnlyList<CampaignSeed>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedCampaign>>(file, Json, cancellationToken) ?? [];

        return [.. raw.Select(Build)];
    }

    /// <summary>
    /// Fills an empty calendar from the seed: each campaign whose window is still
    /// open, scheduled when the seed says so. A calendar with anything in it is
    /// left alone. The stack's worker does it at start, and the assistant suite's
    /// laboratory (phase 13c, Tino's <c>list_campaigns</c>), so both seed the same.
    /// </summary>
    /// <param name="skipped">Told of each campaign left out because its window closed.</param>
    /// <returns>How many campaigns were added; none when the calendar was not empty.</returns>
    public static async Task<int> SeedAsync(
        string path, ICampaignRepository campaigns, IUnitOfWork unitOfWork, TimeProvider clock,
        Action<CampaignSeed>? skipped = null, CancellationToken cancellationToken = default)
    {
        if (await campaigns.AnyAsync(cancellationToken))
            return 0;

        var now = clock.GetUtcNow();
        var seeded = 0;
        foreach (var seed in await LoadAsync(path, cancellationToken))
        {
            if (!Campaign.IsOpenWindow(seed.Details.StartsAt, seed.Details.EndsAt, now))
            {
                skipped?.Invoke(seed);
                continue;
            }

            var campaign = Campaign.Create(clock, seed.Code, seed.Details);
            if (seed.Status == CampaignStatus.Scheduled)
                campaign.Schedule(clock);

            campaigns.Add(campaign);
            seeded++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return seeded;
    }

    private static CampaignSeed Build(SeedCampaign entry) => new(
        new CampaignCode(entry.Code),
        new CampaignDetails(
            new LocalizedText(entry.Name),
            new LocalizedText(entry.Line),
            entry.Kind,
            entry.CategoryCode,
            entry.StartsAt,
            entry.EndsAt,
            entry.Priority,
            entry.PromotionCodes),
        entry.Status);

    private sealed record SeedCampaign(
        string Code,
        Dictionary<string, string> Name,
        Dictionary<string, string> Line,
        CampaignKind Kind,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        int Priority,
        List<string> PromotionCodes,
        string? CategoryCode = null,
        CampaignStatus Status = CampaignStatus.Draft);
}
