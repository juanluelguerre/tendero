using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.WhatSells;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What sells, as the home says it (R-8): the one ranking the dashboard shares,
/// the best sellers by product, and the risers with their floor — rehearsals out
/// of all three, and no figure in the answer.
/// </summary>
public sealed class WhatSellsFiguresTests
{
    private static readonly ProductId Boots = ProductId.New();
    private static readonly ProductId Kettle = ProductId.New();
    private static readonly ProductId Lamp = ProductId.New();
    private static readonly ProductId Mug = ProductId.New();

    private static OrderLine Line(ProductId product, int quantity, decimal price = 10m, string? sku = null) =>
        new(product, VariantId.New(), sku ?? $"{product.Value:N}-SKU", "Product", null, null, new Money(price, "EUR"), quantity);

    private static Order Sale(params OrderLine[] lines) => OrderBuilder.Default().WithLines(lines).InStatus(OrderStatus.Confirmed);

    private static Order Rehearsal(params OrderLine[] lines) => OrderBuilder.Default().WithLines(lines).AsTest().InStatus(OrderStatus.Confirmed);

    private static string Id(ProductId product) => product.Value.ToString();

    [Fact]
    public void A_sale_is_a_live_order_that_was_paid_for_and_not_cancelled()
    {
        Assert.True(SalesRanking.IsSale(OrderBuilder.Default().InStatus(OrderStatus.PaymentAuthorized)));
        Assert.True(SalesRanking.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Delivered)));
        Assert.False(SalesRanking.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Pending)));
        Assert.False(SalesRanking.IsSale(OrderBuilder.Default().InStatus(OrderStatus.Cancelled)));
        Assert.False(SalesRanking.IsSale(OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed)));
    }

    /// <summary>The home ranks products, so a product's variants add up where the dashboard's SKUs stay apart.</summary>
    [Fact]
    public void Best_sellers_are_products_whose_variants_add_up()
    {
        List<Order> week =
        [
            Sale(Line(Boots, 2, sku: "BOOTS-38"), Line(Kettle, 3)),
            Sale(Line(Boots, 2, sku: "BOOTS-42")),
            Sale(Line(Lamp, 1)),
        ];

        var ranked = WhatSellsFigures.BestSellers(week, limit: 10);

        Assert.Equal([new RankedProduct(1, Id(Boots)), new RankedProduct(2, Id(Kettle)), new RankedProduct(3, Id(Lamp))], ranked);
        Assert.Equal(["BOOTS-38", "BOOTS-42"], SalesRanking.Rank(week, SalesRanking.BySku).Where(sold => sold.Key.StartsWith("BOOTS", StringComparison.Ordinal)).Select(sold => sold.Key));
    }

    [Fact]
    public void Best_sellers_leave_rehearsals_out_and_stop_at_the_limit()
    {
        var ranked = WhatSellsFigures.BestSellers([Rehearsal(Line(Mug, 50)), Sale(Line(Kettle, 2)), Sale(Line(Lamp, 1))], limit: 1);

        Assert.Equal([new RankedProduct(1, Id(Kettle))], ranked);
    }

    [Fact]
    public void Nothing_sold_is_an_empty_row()
    {
        Assert.Empty(WhatSellsFigures.BestSellers([], limit: 10));
        Assert.Empty(WhatSellsFigures.Trending([], [], limit: 10));
    }

    /// <summary>
    /// The floor: three units this window, or it is not a trend — one sale against
    /// none is not news. Exactly three clears it; two, however much they grew, does not.
    /// </summary>
    [Fact]
    public void A_rise_below_the_floor_is_not_a_trend()
    {
        List<Order> week = [Sale(Line(Kettle, WhatSellsFigures.TrendFloor)), Sale(Line(Lamp, WhatSellsFigures.TrendFloor - 1))];

        var trending = WhatSellsFigures.Trending(week, before: [], limit: 10);

        Assert.Equal([new RankedProduct(1, Id(Kettle))], trending);
    }

    /// <summary>
    /// Ranked by how many more units than the window before, not by the ratio and not
    /// by volume: a steady best seller is not news, and one that sold less is not rising.
    /// </summary>
    [Fact]
    public void Trends_are_ranked_by_growth_and_a_steady_seller_is_not_one()
    {
        List<Order> week = [Sale(Line(Boots, 40)), Sale(Line(Kettle, 12)), Sale(Line(Lamp, 5)), Sale(Line(Mug, 3))];
        List<Order> before = [Sale(Line(Boots, 40)), Sale(Line(Kettle, 2)), Sale(Line(Lamp, 1)), Sale(Line(Mug, 4))];

        var trending = WhatSellsFigures.Trending(week, before, limit: 10);

        // Kettle +10, Lamp +4; Boots +0 and Mug −1 are not rising.
        Assert.Equal([Id(Kettle), Id(Lamp)], trending.Select(item => item.ProductId));
        Assert.Equal([1, 2], trending.Select(item => item.Rank));
    }

    [Fact]
    public void Equal_growth_goes_to_the_one_that_sold_more_now()
    {
        List<Order> week = [Sale(Line(Kettle, 5)), Sale(Line(Lamp, 8))];
        List<Order> before = [Sale(Line(Kettle, 2)), Sale(Line(Lamp, 5))];

        Assert.Equal([Id(Lamp), Id(Kettle)], WhatSellsFigures.Trending(week, before, limit: 10).Select(item => item.ProductId));
    }

    /// <summary>A rehearsal neither makes a product rise nor lowers the week it is compared against.</summary>
    [Fact]
    public void Test_orders_move_no_trend()
    {
        var trending = WhatSellsFigures.Trending(
            [Rehearsal(Line(Mug, 30)), Sale(Line(Kettle, 4))],
            [Rehearsal(Line(Kettle, 30))],
            limit: 10);

        Assert.Equal([new RankedProduct(1, Id(Kettle))], trending);
    }
}
