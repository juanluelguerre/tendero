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

namespace ElGuerre.Tendero.Inventory.Features.ListStock;

/// <summary>
/// What is on every shelf, and the last few reservations.
///
/// One query for both because they are one screen and one question: a shopkeeper
/// looking at "2 available" wants to know immediately whether that is because
/// three are held for an order placed a minute ago. Splitting it into two
/// endpoints would make the interface ask twice for one answer.
/// </summary>
/// <param name="Filter">Which SKUs: those to restock, those sold out, or all (R-7.1).</param>
/// <param name="Text">Part of a SKU, matched here because the SKU is this context's.</param>
/// <param name="Skus">The SKUs whose product the catalogue matched by name or code.
/// A product's name is the catalogue's, so that half of a search is resolved
/// there and arrives as SKUs. A row is shown when either half matches it; with
/// neither, every SKU is.</param>
/// <param name="Page">A page of SKUs, not of rows: a SKU's warehouses are never
/// split across two pages (R-7.2).</param>
public sealed record ListStockQuery(
    string Culture,
    StockFilter Filter = StockFilter.All,
    string? Text = null,
    IReadOnlyList<string>? Skus = null,
    int Page = Paging.FirstPage,
    int PageSize = Paging.DefaultPageSize)
    : IQuery<ListStockResult>;

public sealed class ListStockValidator : AbstractValidator<ListStockQuery>
{
    public ListStockValidator()
    {
        RuleFor(query => query.Filter).IsInEnum();
        RuleFor(query => query.Page).IsAPage();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}

/// <summary>The stock screen's filters. Each counts SKUs, not rows: a SKU is one
/// thing to restock however many warehouses hold it.</summary>
public enum StockFilter { Restock, SoldOut, All }

public sealed record StockFilterCount(StockFilter Filter, int Count);

public sealed record StockRowView(
    string Sku,
    string WarehouseCode,
    string WarehouseName,
    int OnHand,
    int Reserved,
    int Available,
    DateTimeOffset UpdatedAt);

public sealed record ReservationView(
    string ReservationId,
    string OrderId,
    ReservationStatus Status,
    string? Reason,
    IReadOnlyList<string> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <param name="Rows">Every warehouse row of the page's SKUs, by SKU.</param>
/// <param name="Total">How many SKUs the filter and the search matched — the
/// chosen filter's count — not how many rows are on the page.</param>
/// <param name="Counts">Every filter's number for the SKUs asked about, whichever
/// filter is shown, so the chips keep their size (R-7.1).</param>
/// <param name="RestockAt">The line under which a SKU needs restocking, so the
/// screen can say it without keeping a copy of the rule.</param>
public sealed record ListStockResult(
    IReadOnlyList<StockRowView> Rows,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ReservationView> Reservations,
    IReadOnlyList<StockFilterCount> Counts,
    int RestockAt);

public sealed class ListStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventory/stock",
            async Task<Ok<ListStockResult>> (
                   string? culture, StockFilter? filter, string? q, string[]? sku, int? page, int? pageSize,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                // `sku` present but empty is a search that matched nothing, which
                // is not the same as no search: it answers no rows, not every row.
                var skus = http.Request.Query.ContainsKey("sku") ? sku ?? [] : null;
                var result = await dispatcher.SendAsync(new ListStockQuery(
                    resolved, filter ?? StockFilter.All, q, skus,
                    page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // Shopkeeper only. How much of something a shop has is commercial
            // information: it says what is selling and what is not, and the
            // storefront already gets the only part a customer needs — whether
            // it can be bought — through `inStock` on the search document.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Inventory")
            .WithName("ListStock");
    }
}

public sealed class ListStockHandler(
    IStockRepository stock, IWarehouseReader warehouses)
    : IQueryHandler<ListStockQuery, ListStockResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Inventory);

    /// <summary>
    /// Enough to see what just happened without turning the screen into a log.
    /// The full history is what the audit table is for (phase 7).
    /// </summary>
    private const int RecentReservations = 20;

    public async Task<ListStockResult> HandleAsync(
        ListStockQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("inventory.list_stock");

        activity?.SetTag("inventory.stock_filter", query.Filter.ToString());
        var text = query.Text?.Trim() is { Length: > 0 } typed ? typed : null;
        activity?.SetTag("inventory.narrowed", text is not null || query.Skus is not null);

        // The whole table, filtered and paged here: 198 rows in the laboratory
        // (two warehouses), and a SKU's level is a sum over its rows, which a page
        // of rows could not give. So it pages in memory, by SKU, after the sums
        // (R-7.2); paging in SQL waits for a table that does not fit, with this
        // number recorded.
        var asked = query.Skus?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Searched(string sku) =>
            (text is null && asked is null)
            || (asked?.Contains(sku) ?? false)
            || (text is not null && sku.Contains(text, StringComparison.OrdinalIgnoreCase));

        var inScope = (await stock.AllAsync(cancellationToken))
            .Where(row => Searched(row.Sku))
            .ToList();

        var available = inScope
            .GroupBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Available), StringComparer.OrdinalIgnoreCase);

        bool Shown(string sku) => query.Filter switch
        {
            StockFilter.Restock => Restocking.Needed(available[sku]),
            StockFilter.SoldOut => Restocking.SoldOut(available[sku]),
            _ => true,
        };

        // Ordinal, so the order is the same on every machine and two pages never
        // repeat or skip a SKU; a SKU is unique, so it is its own tie-break.
        var shown = available.Keys.Where(Shown).Order(StringComparer.Ordinal).ToList();
        var onPage = shown.Skip(Paging.Offset(query.Page, query.PageSize)).Take(query.PageSize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A stable sort: a SKU's warehouses keep the ledger's order.
        var rows = inScope.Where(row => onPage.Contains(row.Sku))
            .OrderBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();
        var open = (await warehouses.AllAsync(cancellationToken))
            .ToDictionary(warehouse => warehouse.Code, StringComparer.OrdinalIgnoreCase);

        var reservations = await stock.RecentReservationsAsync(RecentReservations, cancellationToken);

        activity?.SetTag("inventory.row_count", rows.Count);
        activity?.SetTag("inventory.sku_total", shown.Count);

        return new ListStockResult(
        [
            .. rows.Select(row => new StockRowView(
                row.Sku,
                row.WarehouseCode,
                // A warehouse that is not in the file any more still has rows,
                // and showing its code beats showing nothing: the stock is real
                // whatever the configuration says today.
                open.GetValueOrDefault(row.WarehouseCode)?.Name.In(query.Culture) ?? row.WarehouseCode,
                row.OnHand,
                row.Reserved,
                row.Available,
                row.UpdatedAt))
        ],
        shown.Count,
        query.Page,
        query.PageSize,
        [
            .. reservations.Select(reservation => new ReservationView(
                reservation.Id.ToString(),
                // A flat string. OrderId is a record struct and would serialise
                // as {"value":"…"} — the mistake CLAUDE.md warns no unit test
                // will catch.
                reservation.OrderId.ToString(),
                reservation.Status,
                reservation.Reason,
                [.. reservation.Lines.Select(line => $"{line.Sku} ×{line.Quantity} · {line.WarehouseCode}")],
                reservation.CreatedAt,
                reservation.ExpiresAt))
        ],
        [
            new StockFilterCount(StockFilter.Restock, available.Values.Count(Restocking.Needed)),
            new StockFilterCount(StockFilter.SoldOut, available.Values.Count(Restocking.SoldOut)),
            new StockFilterCount(StockFilter.All, available.Count),
        ],
        Restocking.At);
    }
}

/// <summary>
/// A stocktake: the shelf holds this many, whatever the system thought.
///
/// It is <c>Adjust</c> and not <c>Receive</c> because the two are different
/// facts — goods arriving versus the system having been wrong — and a warehouse
/// that cannot tell them apart cannot explain its own numbers. Both raise
/// <c>StockLevelChanged</c>, so a correction typed here reaches the search index
/// by exactly the path an order takes.
/// </summary>
public sealed record CountStockCommand(string Sku, string WarehouseCode, int OnHand)
    : ICommand<CountStockResult>;

public enum CountStockOutcome { Counted, UnknownWarehouse }

public sealed record CountStockResult(CountStockOutcome Outcome, int OnHand, int Available) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        CountStockOutcome.Counted => AuditOutcome.Allowed,
        CountStockOutcome.UnknownWarehouse => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome == CountStockOutcome.Counted ? null : Outcome.ToString();
}

public sealed record CountStockRequest(int OnHand);

/// <summary>
/// The counted row, flat, so the grid can replace the one it has without asking
/// for the whole table again.
///
/// It is a separate record from <c>CountStockResult</c> on purpose: the outcome
/// is how the handler tells the endpoint which status code to use, and putting
/// an enum on the wire would have serialised it as a number — the generated
/// TypeScript said <c>CountStockOutcome: number</c>, which is nobody's idea of
/// an answer.
/// </summary>
public sealed record CountStockResponse(string Sku, string WarehouseCode, int OnHand, int Available);

public sealed class CountStockValidator : AbstractValidator<CountStockCommand>
{
    public CountStockValidator()
    {
        RuleFor(command => command.Sku).NotEmpty();
        RuleFor(command => command.WarehouseCode).NotEmpty();
        RuleFor(command => command.OnHand)
            .GreaterThanOrEqualTo(0).WithMessage("A count cannot be negative.");
    }
}

public static class StockRefusals
{
    public static readonly Refusal UnknownWarehouse = new(
        "UnknownWarehouse", 404, "La tienda no tiene ese almacén.", "The shop has no such warehouse.");
}

public sealed class CountStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/inventory/stock/{sku}/{warehouse}",
            async Task<Results<Ok<CountStockResponse>, ProblemHttpResult>> (
                   string sku, string warehouse, CountStockRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new CountStockCommand(sku, warehouse, request.OnHand), ct);

                return result.Outcome == CountStockOutcome.UnknownWarehouse
                    ? TypedResults.Problem(statusCode: StockRefusals.UnknownWarehouse.Status, extensions: StockRefusals.UnknownWarehouse.Said(path: "$.warehouse"))
                    : TypedResults.Ok(new CountStockResponse(
                        sku, Warehouse.Normalise(warehouse), result.OnHand, result.Available));
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithMetadata(new Refuses(StockRefusals.UnknownWarehouse))
            .WithTags("Inventory")
            .WithName("CountStock");
    }
}

public sealed class CountStockHandler(IStockLedger ledger, IWarehouseReader warehouses)
    : ICommandHandler<CountStockCommand, CountStockResult>
{
    public async Task<CountStockResult> HandleAsync(
        CountStockCommand command, CancellationToken cancellationToken)
    {
        var code = Warehouse.Normalise(command.WarehouseCode);

        // The warehouse list is configuration, not stock: typing a code nobody
        // operates would otherwise silently create a shelf in a building that
        // does not exist.
        var known = (await warehouses.AllAsync(cancellationToken))
            .Any(warehouse => warehouse.Code == code);

        if (!known)
            return new CountStockResult(CountStockOutcome.UnknownWarehouse, 0, 0);

        var shelf = await ledger.CountAsync(command.Sku, code, command.OnHand, cancellationToken);

        return new CountStockResult(CountStockOutcome.Counted, shelf.OnHand, shelf.Available);
    }
}
