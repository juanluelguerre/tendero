using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Inventory.Features.StockHealth;

/// <summary>
/// Every SKU to restock with its days of cover — not the five closest the stock
/// health shows. Steward's <c>stock_to_restock</c> (phase 12, S-3a). The R-7.1
/// rule (<see cref="Restocking"/>) decides who is on the list and
/// <see cref="StockCoverage"/> for how long it lasts, summed across warehouses as
/// the stock health counts it, unless a warehouse is asked for.
/// </summary>
public sealed record GetRestockListQuery(string? Warehouse = null, int Limit = ReadLimits.MaxRows) : IQuery<RestockList>;

/// <param name="Total">Every SKU to restock, not only the rows shown.</param>
public sealed record RestockList(string? Warehouse, int RestockAt, int Total, IReadOnlyList<RestockRow> Rows);

/// <param name="PerDay">Units a day over the last four weeks; null when it sold none.</param>
/// <param name="CoverageDays">How long what is left lasts at that pace; null when it sold none.</param>
public sealed record RestockRow(string Sku, int Available, decimal? PerDay, decimal? CoverageDays);

public sealed class RestockListValidator : AbstractValidator<GetRestockListQuery>
{
    public RestockListValidator() => RuleFor(x => x.Limit).InclusiveBetween(1, ReadLimits.MaxRows);
}

public sealed class RestockListHandler(IStockRepository stock, ISalesPace pace, TimeProvider clock)
    : IQueryHandler<GetRestockListQuery, RestockList>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Inventory);

    public async Task<RestockList> HandleAsync(GetRestockListQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("inventory.dashboard.restock");
        activity?.SetTag("inventory.warehouse", query.Warehouse);

        var now = clock.GetUtcNow();
        var shelves = (await stock.AllAsync(cancellationToken))
            .Where(item => query.Warehouse is null || string.Equals(item.WarehouseCode, query.Warehouse, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var toRestock = shelves
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Sku: group.Key, Available: group.Sum(item => item.Available)))
            .Where(entry => Restocking.Needed(entry.Available))
            .ToList();

        var sold = await pace.SoldAsync([.. toRestock.Select(entry => entry.Sku)], now - StockCoverage.PaceWindow, now, cancellationToken);

        var rows = toRestock
            .Select(entry =>
            {
                var perDay = sold.TryGetValue(entry.Sku, out var pace) ? StockCoverage.PerDay(pace.Units, pace.OnSaleSince, now) : null;
                return new RestockRow(entry.Sku, entry.Available, perDay, perDay is { } rate ? StockCoverage.Days(entry.Available, rate) : null);
            })
            // Sold out first, then what runs out soonest; what does not sell is not
            // urgent, so it goes last.
            .OrderBy(row => row.Available > 0)
            .ThenBy(row => row.CoverageDays is null)
            .ThenBy(row => row.CoverageDays)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();

        activity?.SetTag("inventory.total", rows.Count);
        return new RestockList(query.Warehouse, Restocking.At, rows.Count, [.. rows.Take(query.Limit)]);
    }
}

public sealed class RestockListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/dashboard/restock?warehouse=MAD&limit=50
        app.MapGet("/api/dashboard/restock",
                async Task<Ok<RestockList>> (string? warehouse, int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                    TypedResults.Ok(await dispatcher.SendAsync(new GetRestockListQuery(warehouse, limit ?? ReadLimits.MaxRows), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithName("GetRestockList");
    }
}
