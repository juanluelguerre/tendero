using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

public enum PerformanceRanking
{
    Best,

    /// <summary>Among the products that sold in the period, the least: what sold
    /// nothing is in no order (S-3a, ruling 3).</summary>
    Worst,
}

/// <summary>
/// The products that sell most or least in a period, by units and then revenue —
/// the ranking the home uses (<see cref="SalesRanking"/>), narrowed to a category
/// and everything under it. Steward's <c>product_performance</c> (phase 12).
/// Ten rows by default, a ranking's natural length; fifty at most. A category the
/// shop never sold in reads as an empty ranking: checking that it exists would be
/// Ordering reading the catalogue.
/// </summary>
public sealed record GetProductPerformanceQuery(
    DashboardPeriod Period, PerformanceRanking Ranking, string? Category = null, int Limit = 10)
    : IQuery<ProductPerformance>;

/// <param name="Total">Every product that sold, not only the rows shown.</param>
public sealed record ProductPerformance(
    DashboardPeriod Period, PerformanceRanking Ranking, string? Category, int Total, IReadOnlyList<ProductSales> Rows);

/// <param name="Name">As the order recorded it: the snapshot, not today's name (ADR 0002).</param>
public sealed record ProductSales(string ProductId, string Name, int Units, Money Revenue);

public static class ProductPerformanceFigures
{
    public static ProductPerformance Of(
        DashboardPeriod period, PerformanceRanking ranking, string? category, int limit, IReadOnlyList<Order> placed)
    {
        var ranked = SalesRanking.Rank(placed, SalesRanking.ByProduct,
            category is { Length: > 0 } wanted ? line => line.IsIn(wanted) : null);

        // Worst is the same ranking read from the other end.
        var ordered = ranking == PerformanceRanking.Best ? ranked : [.. ranked.Reverse()];

        var currency = DashboardCurrency.Of(placed);

        return new ProductPerformance(period, ranking, category, ranked.Count,
            [.. ordered.Take(limit).Select(sold => new ProductSales(sold.Key, sold.Name, sold.Units, new Money(sold.Revenue, currency)))]);
    }
}

public sealed class ProductPerformanceValidator : AbstractValidator<GetProductPerformanceQuery>
{
    public ProductPerformanceValidator()
    {
        RuleFor(x => x.Period).IsInEnum();
        RuleFor(x => x.Ranking).IsInEnum();
        RuleFor(x => x.Limit).InclusiveBetween(1, ReadLimits.MaxRows);
    }
}

public sealed class ProductPerformanceHandler(IShopHistory history, TimeProvider clock)
    : IQueryHandler<GetProductPerformanceQuery, ProductPerformance>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ProductPerformance> HandleAsync(GetProductPerformanceQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.products");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));
        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);

        return ProductPerformanceFigures.Of(query.Period, query.Ranking, query.Category, query.Limit,
            await history.OrdersPlacedAsync(window.From, window.To, cancellationToken));
    }
}
