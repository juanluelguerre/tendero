using System.Text.Json;
using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// The shipped tariffs and promotions, checked against the shipped catalogue.
///
/// Same idea as the connector contract suite validating the catalogue sample:
/// what the repository hands a fresh clone is data, and data with a typo in it
/// is a rule that never fires. Nothing else in the build would notice — a
/// promotion scoped to a category that does not exist simply never applies, and
/// that looks exactly like a promotion whose conditions were not met.
/// </summary>
public sealed class PricingSeedFileTests
{
    private static string Seed(string file) => Path.Combine("seed", file);

    private static async Task<IReadOnlyList<PriceList>> PriceLists() =>
        await PricingSeedFile.LoadPriceListsAsync(Seed("pricelists.sample.json"));

    private static async Task<IReadOnlyList<Promotion>> Promotions() =>
        await PricingSeedFile.LoadPromotionsAsync(Seed("promotions.sample.json"));

    /// <summary>
    /// What the import makes purchasable: the variants a product's source tells
    /// apart (<c>{externalId}-{sku}</c>), or the default one it mints when the
    /// source tells none (<c>{externalId}-DEFAULT</c>).
    /// </summary>
    private static HashSet<string> CatalogueSkus()
    {
        using var file = File.OpenRead(Seed("products.sample.json"));
        using var document = JsonDocument.Parse(file);

        return
        [
            .. document.RootElement.EnumerateArray()
                .SelectMany(product => product.TryGetProperty("variants", out var variants)
                    ? variants.EnumerateArray().Select(variant => variant.GetProperty("sku").GetString()!)
                    : ["DEFAULT"],
                    (product, sku) => $"{product.GetProperty("item_id").GetString()}-{sku}")
        ];
    }

    private static HashSet<string> CategoryCodes()
    {
        using var file = File.OpenRead(Seed("categories.sample.json"));
        using var document = JsonDocument.Parse(file);

        return
        [
            .. document.RootElement.EnumerateArray()
                .Select(category => category.GetProperty("code").GetString()!)
        ];
    }

    [Fact]
    public async Task The_shipped_tariffs_load()
    {
        var lists = await PriceLists();

        Assert.Equal(["retail", "vip"], lists.Select(list => list.Code).Order(StringComparer.Ordinal));
        Assert.All(lists, list => Assert.NotEmpty(list.Entries));
        Assert.All(lists, list => Assert.All(list.Entries,
            entry => Assert.False(entry.Price.IsNegative)));
    }

    [Fact]
    public async Task Every_tariff_line_points_at_something_the_shop_actually_sells()
    {
        var catalogue = CatalogueSkus();
        var lists = await PriceLists();

        var orphans = lists
            .SelectMany(list => list.Entries.Select(entry => $"{list.Code}:{entry.Sku}"))
            .Where(entry => !catalogue.Contains(entry.Split(':')[1]))
            .ToArray();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// The VIP tariff has to be worth having. A "VIP" price above retail is not
    /// a bug the compiler can see, and it is exactly the kind of thing a hurried
    /// edit produces.
    /// </summary>
    [Fact]
    public async Task The_vip_tariff_is_never_dearer_than_retail()
    {
        var lists = await PriceLists();
        var retail = lists.Single(list => list.Code == "retail");
        var vip = lists.Single(list => list.Code == "vip");

        Assert.All(vip.Entries, entry =>
            Assert.True(entry.Price <= retail.PriceFor(entry.Sku)!.Value,
                $"VIP pays more than retail for {entry.Sku}."));
    }

    [Fact]
    public async Task The_shipped_promotions_load_with_all_four_effects_represented()
    {
        var promotions = await Promotions();

        Assert.Equal(
            ["amount-off-order", "buy-x-get-y", "free-shipping", "percent-off-line"],
            promotions.Select(promotion => promotion.Effect.Kind).Distinct().Order(StringComparer.Ordinal));

        // And all three combination policies, so the demo has something to show
        // in every column of the backoffice table.
        Assert.Equal(
            Enum.GetValues<CombinationPolicy>().Order(),
            promotions.Select(promotion => promotion.Combination).Distinct().Order());
    }

    [Fact]
    public async Task Every_promotion_is_named_in_both_cultures()
    {
        var promotions = await Promotions();

        Assert.All(promotions, promotion =>
        {
            Assert.Contains("es", promotion.Name.Cultures);
            Assert.Contains("en", promotion.Name.Cultures);
        });
    }

    [Fact]
    public async Task Every_promotion_scoped_to_a_category_names_one_that_exists()
    {
        var categories = CategoryCodes();
        var promotions = await Promotions();

        var unknown = promotions
            .Select(promotion => promotion.Condition.CategoryCode)
            .OfType<string>()
            .Where(code => !categories.Contains(code))
            .ToArray();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Codes are the identity a quote's fingerprint is built on and what an
    /// audit row will name. Two promotions sharing one is two rules nobody can
    /// tell apart afterwards.
    /// </summary>
    [Fact]
    public async Task Promotion_codes_are_unique()
    {
        var promotions = await Promotions();

        Assert.Equal(
            promotions.Count,
            promotions.Select(promotion => promotion.Code).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// An exclusivity group with a single member is a promotion that suppresses
    /// nothing — either a leftover or a rule half written. Groups exist to hold
    /// rivals.
    /// </summary>
    [Fact]
    public async Task Every_exclusivity_group_has_at_least_two_rivals_in_it()
    {
        var promotions = await Promotions();

        var lonely = promotions
            .Where(promotion => promotion.ExclusivityGroup is not null)
            .GroupBy(promotion => promotion.ExclusivityGroup!)
            .Where(group => group.Count() < 2)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(lonely);
    }

    /// <summary>
    /// A promotion whose window has already closed is dead weight in the seed.
    /// Black Friday 2026 is in the file precisely to be the one that is not
    /// running, so this asserts the window is in the FUTURE rather than the past.
    /// </summary>
    [Fact]
    public async Task No_shipped_promotion_has_a_window_that_already_closed()
    {
        var promotions = await Promotions();
        var repositoryDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.All(promotions, promotion =>
            Assert.True(promotion.ValidTo is null || promotion.ValidTo > repositoryDate,
                $"{promotion.Code} expired before it was ever committed."));
    }

    /// <summary>
    /// A proposal is written in the committed file's shape (phase 16, P16-19):
    /// every shipped promotion, written and read back, is the same promotion —
    /// what changes what anybody pays (<see cref="Promotion.Canonical"/>) and its
    /// name in every language.
    /// </summary>
    [Fact]
    public async Task What_is_written_reads_back_as_the_same_promotions()
    {
        var shipped = await Promotions();
        var path = Path.Combine(Path.GetTempPath(), $"promotions-{Guid.NewGuid():N}.json");
        try
        {
            await PricingSeedFile.WritePromotionsAsync(path, shipped, TestContext.Current.CancellationToken);
            var read = await PricingSeedFile.LoadPromotionsAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(shipped.Select(promotion => promotion.Canonical()), read.Select(promotion => promotion.Canonical()));
            Assert.Equal(shipped.Select(promotion => promotion.Name.Values), read.Select(promotion => promotion.Name.Values));
            Assert.Contains("\"Rebajas de verano\"", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A shop that sells in another currency reads its proposal back in it — the
    /// currency an amount off is in, else the minimum's — and a promotion with no
    /// condition is written with none, as a person would write it. Found by phase
    /// 16 S2b's Stryker close: the shipped file is all euros and all conditioned.
    /// </summary>
    [Fact]
    public async Task A_proposal_keeps_its_currency_and_writes_no_condition_where_there_is_none()
    {
        IReadOnlyList<Promotion> dollars =
        [
            new("FIVE", LocalizedText.From("en", "Five off"), PromotionCondition.Always,
                new AmountOffOrder(new Money(5m, "USD")), CombinationPolicy.Stackable, 10),
            new("TEN", LocalizedText.From("en", "Ten percent over fifty"), new PromotionCondition(new Money(50m, "USD"), null, null, null),
                new PercentOffLine(10m), CombinationPolicy.ExclusiveGlobal, 20),
        ];
        var path = Path.Combine(Path.GetTempPath(), $"promotions-{Guid.NewGuid():N}.json");
        try
        {
            await PricingSeedFile.WritePromotionsAsync(path, dollars, TestContext.Current.CancellationToken);
            var read = await PricingSeedFile.LoadPromotionsAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(dollars.Select(promotion => promotion.Canonical()), read.Select(promotion => promotion.Canonical()));
            using var written = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal([false, true], written.RootElement.EnumerateArray().Select(entry => entry.TryGetProperty("condition", out _)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Beside the committed file wherever the host put it — the API's content root
    /// has no seed folder, the lesson <c>SeedFileEnvironment</c> records — and never
    /// over it.
    /// </summary>
    [Fact]
    public async Task A_proposal_is_written_beside_the_committed_promotions()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var committed = Path.Combine(folder, "promotions.sample.json");
        await File.WriteAllTextAsync(committed, "[]", TestContext.Current.CancellationToken);
        try
        {
            var proposals = new SeedFilePromotionProposals(Microsoft.Extensions.Options.Options.Create(new PricingSeedOptions { PromotionsPath = committed }));

            var file = await proposals.WriteAsync(await Promotions(), TestContext.Current.CancellationToken);

            Assert.Equal("promotions.proposed.json", file);
            Assert.Equal(7, (await PricingSeedFile.LoadPromotionsAsync(Path.Combine(folder, file), TestContext.Current.CancellationToken)).Count);
            Assert.Equal("[]", await File.ReadAllTextAsync(committed, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
