using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Inventory.Features.StockHealth;

/// <summary>A warehouse's median coverage over the SKUs it holds that sell; null when none does.</summary>
public sealed record WarehouseCoverage(string WarehouseCode, decimal? MedianDays);

/// <summary>A SKU, what it has available across the warehouses, its pace per day and the days that covers.</summary>
public sealed record SkuCoverage(string Sku, int Available, decimal PerDay, decimal Days);

/// <summary>A SKU's sell-through for the period: sold over sold and left, 0 to 1.</summary>
public sealed record SkuSellThrough(string Sku, int Sold, int Left, decimal Rate);

/// <summary>
/// The stock as the dashboard reads it (R-9, D-7): what to restock and what is
/// sold out, as the stock screen counts them; what runs out in fewer than
/// fourteen days at the shop's pace, what did not sell in four weeks, the median
/// coverage per warehouse and the closest to running out; and the period's
/// sell-through — what moves and what sits.
/// </summary>
public sealed record StockHealth(
    DashboardPeriod Period,
    int ToRestock,
    int SoldOut,
    int UnderTwoWeeks,
    int NoSales,
    IReadOnlyList<WarehouseCoverage> Warehouses,
    IReadOnlyList<SkuCoverage> Closest,
    IReadOnlyList<SkuSellThrough> Fastest,
    IReadOnlyList<SkuSellThrough> Slowest);

public sealed record GetStockHealthQuery(DashboardPeriod Period) : IQuery<StockHealth>;

/// <summary>
/// The spec's definitions, with no database. Coverage (days) = units available ÷
/// units sold per day; the pace is the units sold in the last 28 days ÷ 28 — four
/// whole weeks, so no weekday counts twice — or ÷ the days on sale when fewer.
/// </summary>
public static class StockCoverage
{
    /// <summary>The window the pace is measured over.</summary>
    public static readonly TimeSpan PaceWindow = TimeSpan.FromDays(28);

    /// <summary>Under this, a SKU runs out soon.</summary>
    public const decimal SoonDays = 14m;

    /// <summary>Units sold per day, or null when nothing sold — *no sales*, never an endless coverage.</summary>
    public static decimal? PerDay(int unitsSold, DateTimeOffset? onSaleSince, DateTimeOffset now)
    {
        if (unitsSold <= 0) return null;

        var days = PaceWindow.TotalDays;
        if (onSaleSince is { } since && now - since < PaceWindow)
            // A day begun is a day on sale: this morning's product divides by one, not by nothing.
            days = Math.Max(1, Math.Ceiling((now - since).TotalDays));

        return Math.Round(unitsSold / (decimal)days, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The days what is available lasts at that pace; a sold-out SKU covers none.</summary>
    public static decimal Days(int available, decimal perDay) =>
        available <= 0 ? 0m : Math.Round(available / perDay, 1, MidpointRounding.AwayFromZero);

    /// <summary>Sold over sold and left, or null when there was neither.</summary>
    public static decimal? SellThrough(int sold, int left) =>
        sold + left <= 0 ? null : Math.Round(sold / (decimal)(sold + left), 4, MidpointRounding.AwayFromZero);

    public static decimal? Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : Math.Round((sorted[middle - 1] + sorted[middle]) / 2, 1, MidpointRounding.AwayFromZero);
    }
}

public sealed class StockHealthHandler(IStockRepository stock, ISalesPace pace, TimeProvider clock)
    : IQueryHandler<GetStockHealthQuery, StockHealth>
{
    private const int Rows = 5;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Inventory);

    public async Task<StockHealth> HandleAsync(GetStockHealthQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("inventory.dashboard.stock");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));

        var now = clock.GetUtcNow();
        var window = DashboardWindow.Ending(now, query.Period);
        var shelves = await stock.AllAsync(cancellationToken);
        var skus = shelves.Select(item => item.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var fourWeeks = await pace.SoldAsync(skus, now - StockCoverage.PaceWindow, now, cancellationToken);
        var period = await pace.SoldAsync(skus, window.From, window.To, cancellationToken);

        var available = shelves
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Available), StringComparer.OrdinalIgnoreCase);
        decimal? PerDay(string sku) =>
            fourWeeks.TryGetValue(sku, out var sold) ? StockCoverage.PerDay(sold.Units, sold.OnSaleSince, now) : null;

        var covered = available
            .Where(entry => entry.Value > 0 && PerDay(entry.Key) is not null)
            .Select(entry => new SkuCoverage(entry.Key, entry.Value, PerDay(entry.Key)!.Value, StockCoverage.Days(entry.Value, PerDay(entry.Key)!.Value)))
            .ToList();

        var throughput = available
            .Select(entry =>
            {
                var sold = period.TryGetValue(entry.Key, out var pace) ? pace.Units : 0;
                var left = Math.Max(entry.Value, 0);
                return StockCoverage.SellThrough(sold, left) is { } rate ? new SkuSellThrough(entry.Key, sold, left, rate) : null;
            })
            .OfType<SkuSellThrough>()
            .ToList();

        activity?.SetTag("inventory.skus", available.Count);

        return new StockHealth(
            query.Period,
            available.Values.Count(Restocking.Needed),
            available.Values.Count(Restocking.SoldOut),
            covered.Count(sku => sku.Days < StockCoverage.SoonDays),
            available.Count(entry => entry.Value > 0 && PerDay(entry.Key) is null),
            [
                .. shelves.GroupBy(item => item.WarehouseCode, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new WarehouseCoverage(group.Key, StockCoverage.Median(
                    [
                        .. group.Where(item => item.Available > 0 && PerDay(item.Sku) is not null)
                            .Select(item => StockCoverage.Days(item.Available, PerDay(item.Sku)!.Value))
                    ])))
            ],
            [.. covered.OrderBy(sku => sku.Days).ThenBy(sku => sku.Sku, StringComparer.Ordinal).Take(Rows)],
            [.. throughput.Where(row => row.Sold > 0).OrderByDescending(row => row.Rate).ThenBy(row => row.Sku, StringComparer.Ordinal).Take(Rows)],
            [.. throughput.Where(row => row.Left > 0).OrderBy(row => row.Rate).ThenBy(row => row.Sku, StringComparer.Ordinal).Take(Rows)]);
    }
}

public sealed class StockHealthEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/stock",
            async Task<Ok<StockHealth>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetStockHealthQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            // The shopkeeper's, as the stock screen is: what sells and what does
            // not is commercial information.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithName("GetStockHealth");
    }
}
