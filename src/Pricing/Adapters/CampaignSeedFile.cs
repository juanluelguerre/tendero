using System.Text.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Pricing.Domain;
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
