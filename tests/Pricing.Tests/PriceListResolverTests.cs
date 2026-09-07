using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Engine;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

public sealed class PriceListResolverTests
{
    private readonly PriceListResolver resolver = new();

    private static PriceList List(
        string code, string segment, int priority,
        (string Sku, decimal Amount)[] entries,
        DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(code, segment, Build.Eur, priority,
            [.. entries.Select(entry => new PriceListEntry(entry.Sku, Build.Money(entry.Amount)))],
            from, to);

    private ResolvedPrice Resolve(PriceBook book, string sku, string segment, decimal catalogPrice = 100m) =>
        this.resolver.Resolve(book, new PriceRequest(sku, Build.Money(catalogPrice), segment, Build.Now));

    /// <summary>
    /// The fallback is not a detail. Without it, a freshly imported product has
    /// no price until somebody adds it to a tariff, and a shop with unbuyable
    /// articles is worse than a shop with no tariffs.
    /// </summary>
    [Fact]
    public void A_sku_no_list_knows_keeps_its_catalogue_price()
    {
        var price = Resolve(PriceBook.Empty, "UNLISTED", "retail", catalogPrice: 42m);

        Assert.Equal(42m, price.Unit.Amount);
        Assert.Equal(ResolvedPrice.Catalog, price.Source);
    }

    [Fact]
    public void The_segment_decides_which_list_answers()
    {
        var book = new PriceBook(
        [
            List("retail", "retail", 100, [("A", 79.95m)]),
            List("vip", "vip", 10, [("A", 71.95m)])
        ]);

        Assert.Equal(79.95m, Resolve(book, "A", "retail").Unit.Amount);
        Assert.Equal(71.95m, Resolve(book, "A", "vip").Unit.Amount);
    }

    /// <summary>
    /// A VIP list that does not carry the SKU does not make the article
    /// unbuyable for a VIP: the chain falls through to the catalogue price, the
    /// same way LocalizedText falls through to another culture.
    /// </summary>
    [Fact]
    public void A_gap_in_a_segment_list_falls_through_instead_of_failing()
    {
        var book = new PriceBook([List("vip", "vip", 10, [("A", 71.95m)])]);

        var price = Resolve(book, "B", "vip", catalogPrice: 29.90m);

        Assert.Equal(29.90m, price.Unit.Amount);
        Assert.Equal(ResolvedPrice.Catalog, price.Source);
    }

    [Fact]
    public void Lowest_priority_wins_and_the_code_breaks_the_tie()
    {
        var book = new PriceBook(
        [
            List("bbb", "retail", 10, [("A", 20m)]),
            List("aaa", "retail", 10, [("A", 10m)]),
            List("late", "retail", 99, [("A", 5m)])
        ]);

        var price = Resolve(book, "A", "retail");

        Assert.Equal(10m, price.Unit.Amount);
        Assert.Equal("aaa", price.Source);
    }

    [Fact]
    public void A_list_outside_its_window_does_not_answer()
    {
        var book = new PriceBook(
        [
            List("sale", "retail", 1, [("A", 10m)],
                from: Build.Now.AddDays(1), to: Build.Now.AddDays(2)),
            List("retail", "retail", 100, [("A", 20m)])
        ]);

        Assert.Equal(20m, Resolve(book, "A", "retail").Unit.Amount);
    }

    /// <summary>
    /// A tariff in another currency is not a better price, it is incoherent
    /// data. It is skipped rather than mixed: multi-currency is deferred on
    /// purpose, and deferred means checked, not ignored.
    /// </summary>
    [Fact]
    public void A_tariff_in_another_currency_is_ignored_rather_than_mixed()
    {
        var book = new PriceBook(
        [
            new PriceList("usd", "retail", "USD", 1, [new PriceListEntry("A", new Money(5m, "USD"))])
        ]);

        var price = Resolve(book, "A", "retail", catalogPrice: 20m);

        Assert.Equal(Build.Eur, price.Unit.Currency);
        Assert.Equal(ResolvedPrice.Catalog, price.Source);
    }
}
