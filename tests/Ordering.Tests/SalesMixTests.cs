using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Where the sales came from (R-9, D-7): the storefront, an agent over MCP or one
/// over UCP; and what they were, by the category each line was sold in. An order or
/// a line older than the facts is its own row, never folded into a real one.
/// </summary>
public sealed class SalesMixTests
{
    private static readonly TestClock Clock = new();

    private static OrderLine Line(decimal price, int quantity, string? category) =>
        new(ProductId.New(), VariantId.New(), "SKU", "Product", null, null, new Money(price, "EUR"), quantity, category);

    private static Order Sale(OrderChannel? channel, params OrderLine[] lines)
    {
        var builder = OrderBuilder.Default().WithLines(lines.Length > 0 ? lines : [Line(10m, 1, "MUGS")]).At(Clock);
        return (channel is { } through ? builder.Through(through) : builder).InStatus(OrderStatus.Confirmed);
    }

    [Fact]
    public void Sales_split_by_the_channel_they_came_through()
    {
        List<Order> orders = [Sale(OrderChannel.Web), Sale(OrderChannel.Web), Sale(OrderChannel.Mcp), Sale(OrderChannel.Ucp)];

        var mix = SalesMixFigures.Of(DashboardPeriod.Week, orders);

        Assert.Equal(
            [new ChannelSales(OrderChannel.Web, 2, 29.90m), new ChannelSales(OrderChannel.Mcp, 1, 14.95m), new ChannelSales(OrderChannel.Ucp, 1, 14.95m)],
            mix.ByChannel);
    }

    [Fact]
    public void Orders_before_the_channel_was_recorded_are_counted_apart()
    {
        var mix = SalesMixFigures.Of(DashboardPeriod.Week, [Sale(OrderChannel.Web), Sale(channel: null)]);

        Assert.Equal((1, 1), (mix.ByChannel.Single(row => row.Channel == OrderChannel.Web).Orders, mix.Unrecorded.Orders));
    }

    [Fact]
    public void Sales_by_category_rank_by_revenue_and_lines_without_one_are_their_own_row()
    {
        List<Order> orders =
        [
            Sale(OrderChannel.Web, Line(10m, 2, "MUGS"), Line(40m, 1, "COOKWARE")),
            Sale(OrderChannel.Web, Line(5m, 1, "MUGS"), Line(99m, 1, category: null)),
        ];

        var mix = SalesMixFigures.Of(DashboardPeriod.Week, orders);

        Assert.Equal(
            [new CategorySales("COOKWARE", 1, 40m), new CategorySales("MUGS", 3, 25m), new CategorySales(null, 1, 99m)],
            mix.ByCategory);
    }

    [Fact]
    public void Test_orders_and_unpaid_ones_are_not_in_the_mix()
    {
        List<Order> orders =
        [
            OrderBuilder.Default().AsTest().Through(OrderChannel.Mcp).At(Clock).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().Through(OrderChannel.Web).At(Clock).InStatus(OrderStatus.Pending),
        ];

        var mix = SalesMixFigures.Of(DashboardPeriod.Week, orders);

        Assert.Equal((0, 0), (mix.ByChannel.Sum(row => row.Orders), mix.ByCategory.Count));
    }
}
