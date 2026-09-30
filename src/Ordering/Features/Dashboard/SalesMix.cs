using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>What came through one channel in the period: its paid orders and their revenue.</summary>
public sealed record ChannelSales(OrderChannel? Channel, int Orders, decimal Revenue);

/// <summary>
/// What sold in one category in the period: units and the lines' revenue — the
/// price times the quantity, before shipping and the order's discounts, which
/// belong to the order and not to any one category.
/// </summary>
public sealed record CategorySales(string? CategoryCode, int Units, decimal Revenue);

/// <summary>
/// Where the period's sales came from and what they were (R-9, D-7). The channel
/// and the category are the ones the order recorded when it was placed; an order
/// placed before the channel was recorded is <see cref="Unrecorded"/>, and a line
/// sold before the category was, or of a product with none, is the category row
/// with no code — each its own row, never folded into a real one.
/// </summary>
/// <param name="ByChannel">The storefront, MCP and UCP, always in that order, zeros included.</param>
/// <param name="ByCategory">The most revenue first; the row with no category last.</param>
public sealed record SalesMix(
    DashboardPeriod Period,
    string Currency,
    IReadOnlyList<ChannelSales> ByChannel,
    ChannelSales Unrecorded,
    IReadOnlyList<CategorySales> ByCategory);

public sealed record GetSalesMixQuery(DashboardPeriod Period) : IQuery<SalesMix>;

/// <summary>The arithmetic, with no database: what counts as a sale is <see cref="SalesRanking.IsSale"/>, as everywhere.</summary>
public static class SalesMixFigures
{
    private static readonly OrderChannel[] Channels = [OrderChannel.Web, OrderChannel.Mcp, OrderChannel.Ucp];

    public static SalesMix Of(DashboardPeriod period, IReadOnlyList<Order> placed)
    {
        var sales = placed.Where(SalesRanking.IsSale).ToList();

        ChannelSales Through(OrderChannel? channel)
        {
            var these = sales.Where(order => order.Channel == channel).ToList();
            return new ChannelSales(channel, these.Count, these.Sum(order => order.Total.Amount));
        }

        return new SalesMix(
            period,
            DashboardCurrency.Of(sales),
            [.. Channels.Select(channel => Through(channel))],
            Through(null),
            [
                .. sales.SelectMany(order => order.Lines)
                    .GroupBy(line => line.CategoryCode)
                    .Select(group => new CategorySales(group.Key, group.Sum(line => line.Quantity), group.Sum(line => line.Total.Amount)))
                    .OrderBy(row => row.CategoryCode is null)
                    .ThenByDescending(row => row.Revenue)
                    .ThenBy(row => row.CategoryCode, StringComparer.Ordinal)
            ]);
    }
}

public sealed class SalesMixHandler(IShopHistory history, TimeProvider clock) : IQueryHandler<GetSalesMixQuery, SalesMix>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<SalesMix> HandleAsync(GetSalesMixQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.mix");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));

        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);
        return SalesMixFigures.Of(query.Period, await history.OrdersPlacedAsync(window.From, window.To, cancellationToken));
    }
}
