using System.Text.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Pricing.Adapters;

public sealed class PricingSeedOptions
{
    public const string SectionName = "Pricing:Seed";

    public string PriceListsPath { get; set; } = Path.Combine("seed", "pricelists.sample.json");
    public string PromotionsPath { get; set; } = Path.Combine("seed", "promotions.sample.json");

    /// <summary>
    /// The segment of whoever has none. A named constant because it turns up in
    /// three places — the default tariff, promotions with no segment, and the
    /// anonymous principal — and three loose strings drift apart.
    /// </summary>
    public string DefaultSegment { get; set; } = "retail";

    /// <summary>Which tax calculator the shop uses. Going from <c>flat-vat</c>
    /// to <c>zero</c> is this line of configuration.</summary>
    public string TaxCalculator { get; set; } = FlatVatTaxCalculator.Key;
}

/// <summary>
/// Tariffs and promotions from committed files.
///
/// **And NOT from a table**, which is the deliberate departure from the roadmap.
/// The reason is written two days back in this same repository: in phase 2 the
/// attribute-definitions table was created and dropped the next day because
/// nothing read or wrote it. Nothing can edit a promotion yet — the backoffice
/// screen is read-only — so a table here would be the same mistake, made
/// knowingly.
///
/// What IS built is the port. When editing promotions becomes the feature, the
/// Postgres adapter registers in place of this one and nothing else changes,
/// which is the entire reason a port exists.
///
/// The side effect that matters: Pricing still does not know EF Core, and that
/// is why the engine can be verified with properties without standing anything
/// up.
/// </summary>
internal sealed class SeedFilePriceListReader(IOptions<PricingSeedOptions> options) : IPriceListReader
{
    private PriceBook? cached;

    public async Task<PriceBook> BookAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.PriceListsPath;

        // With no file, the catalogue price carries on. A shop with no tariffs
        // is a shop selling at list price, not a shop that is down.
        if (!File.Exists(path))
            return this.cached = PriceBook.Empty;

        return this.cached = new PriceBook(await PricingSeedFile.LoadPriceListsAsync(path, cancellationToken));
    }
}

internal sealed class SeedFilePromotionReader(IOptions<PricingSeedOptions> options) : IPromotionReader
{
    private IReadOnlyList<Promotion>? cached;

    public async Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.PromotionsPath;

        return this.cached = File.Exists(path)
            ? await PricingSeedFile.LoadPromotionsAsync(path, cancellationToken)
            : [];
    }
}

/// <summary>
/// The shape of both files. Public because the tests load it directly, the same
/// way the catalogue connector does with its sample: the file that ships is the
/// file that gets validated.
/// </summary>
public static class PricingSeedFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<IReadOnlyList<PriceList>> LoadPriceListsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedPriceList>>(file, Json, cancellationToken) ?? [];

        return
        [
            .. raw.Select(list => new PriceList(
                list.Code,
                list.Segment,
                list.Currency,
                list.Priority,
                [.. list.Entries.Select(entry => new PriceListEntry(entry.Sku, new Money(entry.Amount, list.Currency)))],
                list.ValidFrom,
                list.ValidTo))
        ];
    }

    public static async Task<IReadOnlyList<Promotion>> LoadPromotionsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedPromotion>>(file, Json, cancellationToken) ?? [];

        return [.. raw.Select(Build)];
    }

    private static Promotion Build(SeedPromotion entry) => new(
        entry.Code,
        new LocalizedText(entry.Name),
        new PromotionCondition(
            entry.Condition?.MinimumSubtotal is { } minimum
                ? new Money(minimum, entry.Currency)
                : null,
            entry.Condition?.CategoryCode,
            entry.Condition?.Sku,
            entry.Condition?.MinimumQuantity),
        BuildEffect(entry),
        entry.Combination,
        entry.Priority,
        entry.ExclusivityGroup,
        entry.Segment,
        entry.CouponCode,
        entry.ValidFrom,
        entry.ValidTo);

    private static PromotionEffect BuildEffect(SeedPromotion entry) => entry.Effect.Kind switch
    {
        "percent-off-line" => new PercentOffLine(Required(entry.Effect.Percent, entry.Code, "percent")),
        "amount-off-order" => new AmountOffOrder(
            new Money(Required(entry.Effect.Amount, entry.Code, "amount"), entry.Currency)),
        "buy-x-get-y" => new BuyXGetY(
            (int)Required(entry.Effect.Buy, entry.Code, "buy"),
            (int)Required(entry.Effect.Free, entry.Code, "free")),
        "free-shipping" => new FreeShipping(),

        // An unknown effect is rejected at load time and not at quoting time: a
        // failure on startup gets diagnosed; a discount that never shows up does
        // not.
        _ => throw new InvalidOperationException(
            $"Promotion '{entry.Code}' declares unknown effect '{entry.Effect.Kind}'.")
    };

    private static decimal Required(decimal? value, string code, string field) =>
        value ?? throw new InvalidOperationException($"Promotion '{code}' needs '{field}' for its effect.");

    private sealed record SeedPriceListEntry(string Sku, decimal Amount);

    private sealed record SeedPriceList(
        string Code,
        string Segment,
        string Currency,
        int Priority,
        DateTimeOffset? ValidFrom = null,
        DateTimeOffset? ValidTo = null)
    {
        public List<SeedPriceListEntry> Entries { get; init; } = [];
    }

    private sealed record SeedEffect(
        string Kind,
        decimal? Percent = null,
        decimal? Amount = null,
        decimal? Buy = null,
        decimal? Free = null);

    private sealed record SeedCondition(
        decimal? MinimumSubtotal = null,
        string? CategoryCode = null,
        string? Sku = null,
        int? MinimumQuantity = null);

    private sealed record SeedPromotion(
        string Code,
        Dictionary<string, string> Name,
        SeedEffect Effect,
        CombinationPolicy Combination,
        int Priority,
        string Currency = "EUR",
        SeedCondition? Condition = null,
        string? ExclusivityGroup = null,
        string? Segment = null,
        string? CouponCode = null,
        DateTimeOffset? ValidFrom = null,
        DateTimeOffset? ValidTo = null);
}
