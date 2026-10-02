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
/// health shows. Steward's <c>stock_to_restock</c> (phase 12, S-3a). The need is
/// the shop's: R-7.1's point (<see cref="Restocking"/>) is summed over the
/// warehouses and the pace is the shop's (<see cref="StockCoverage"/>). A warehouse
/// asked for narrows that list to the SKUs it stocks and says how many it holds;
/// it never sets one warehouse's units against the whole shop's pace.
/// </summary>
public sealed record GetRestockListQuery(string? Warehouse = null, int Limit = ReadLimits.MaxRows) : IQuery<RestockList>;

/// <param name="UnknownWarehouse">The warehouse asked for is not one the shop has —
/// not "nothing to restock": <paramref name="Warehouses"/> names the ones it has.</param>
/// <param name="Total">Every SKU to restock, not only the rows shown.</param>
public sealed record RestockList(
    string? Warehouse, bool UnknownWarehouse, IReadOnlyList<string> Warehouses, int RestockAt, int Total, IReadOnlyList<RestockRow> Rows);

/// <param name="Available">Across the shop.</param>
/// <param name="InWarehouse">In the warehouse asked for; null when none was.</param>
/// <param name="PerDay">Units a day across the shop over the last four weeks; null when it sold none.</param>
/// <param name="CoverageDays">How long the shop's stock lasts at that pace; null when it sold none.</param>
public sealed record RestockRow(string Sku, int Available, int? InWarehouse, decimal? PerDay, decimal? CoverageDays);

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
        var shelves = await stock.AllAsync(cancellationToken);
        IReadOnlyList<string> warehouses = [.. shelves.Select(item => item.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];

        if (query.Warehouse is { } asked && !warehouses.Contains(asked, StringComparer.OrdinalIgnoreCase))
        {
            activity?.SetTag("inventory.unknown_warehouse", true);
            return new RestockList(query.Warehouse, true, warehouses, Restocking.At, 0, []);
        }

        var toRestock = shelves
            .GroupBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => (
                Sku: group.Key,
                Available: group.Sum(item => item.Available),
                InWarehouse: InWarehouse(group, query.Warehouse)))
            .Where(entry => Restocking.Needed(entry.Available))
            // A warehouse narrows the shop's list to the SKUs it stocks.
            .Where(entry => query.Warehouse is null || entry.InWarehouse is not null)
            .ToList();

        var sold = await pace.SoldAsync([.. toRestock.Select(entry => entry.Sku)], now - StockCoverage.PaceWindow, now, cancellationToken);

        var rows = toRestock
            .Select(entry =>
            {
                var perDay = sold.TryGetValue(entry.Sku, out var pace) ? StockCoverage.PerDay(pace.Units, pace.OnSaleSince, now) : null;
                return new RestockRow(entry.Sku, entry.Available, entry.InWarehouse, perDay,
                    perDay is { } rate ? StockCoverage.Days(entry.Available, rate) : null);
            })
            // Sold out first, then what runs out soonest; what does not sell is not
            // urgent, so it goes last.
            .OrderBy(row => row.Available > 0)
            .ThenBy(row => row.CoverageDays is null)
            .ThenBy(row => row.CoverageDays)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();

        activity?.SetTag("inventory.total", rows.Count);
        return new RestockList(query.Warehouse, false, warehouses, Restocking.At, rows.Count, [.. rows.Take(query.Limit)]);
    }

    /// <summary>What the warehouse asked for holds of a SKU; null when it has no shelf
    /// for it (a sum over no shelves would say 0, as if it stocked it), or when none
    /// was asked for.</summary>
    private static int? InWarehouse(IEnumerable<StockItem> shelves, string? warehouse)
    {
        if (warehouse is null)
            return null;

        var held = shelves.Where(item => string.Equals(item.WarehouseCode, warehouse, StringComparison.OrdinalIgnoreCase)).ToList();
        return held.Count == 0 ? null : held.Sum(item => item.Available);
    }
}

public sealed record RestockProblem(string Title, string Detail, IReadOnlyList<string> Warehouses);

public sealed class RestockListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/dashboard/restock?warehouse=MAD&limit=50
        app.MapGet("/api/dashboard/restock",
                async Task<Results<Ok<RestockList>, BadRequest<RestockProblem>>> (string? warehouse, int? limit,
                    IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var list = await dispatcher.SendAsync(new GetRestockListQuery(warehouse, limit ?? ReadLimits.MaxRows), ct);
                    return list.UnknownWarehouse
                        ? TypedResults.BadRequest(new RestockProblem(
                            "The shop has no such warehouse.",
                            $"'{warehouse}' is not one of the shop's warehouses: {string.Join(", ", list.Warehouses)}.",
                            list.Warehouses))
                        : TypedResults.Ok(list);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithName("GetRestockList");
    }
}
