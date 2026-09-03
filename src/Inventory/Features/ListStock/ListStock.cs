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
public sealed record ListStockQuery(string Culture) : IQuery<ListStockResult>;

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
    string Status,
    string? Reason,
    IReadOnlyList<string> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ListStockResult(
    IReadOnlyList<StockRowView> Rows, IReadOnlyList<ReservationView> Reservations);

public sealed class ListStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventory/stock",
            async Task<Ok<ListStockResult>> (
                   string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new ListStockQuery(resolved), ct);

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
    /// The full history is what the audit table is for, in phase 7.
    /// </summary>
    private const int RecentReservations = 20;

    public async Task<ListStockResult> HandleAsync(
        ListStockQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("inventory.list_stock");

        var rows = await stock.AllAsync(cancellationToken);
        var open = (await warehouses.AllAsync(cancellationToken))
            .ToDictionary(warehouse => warehouse.Code, StringComparer.OrdinalIgnoreCase);

        var reservations = await stock.RecentReservationsAsync(RecentReservations, cancellationToken);

        activity?.SetTag("inventory.row_count", rows.Count);

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
        [
            .. reservations.Select(reservation => new ReservationView(
                reservation.Id.ToString(),
                // A flat string. OrderId is a record struct and would serialise
                // as {"value":"…"} — the mistake CLAUDE.md warns no unit test
                // will catch.
                reservation.OrderId.ToString(),
                reservation.Status.ToString(),
                reservation.Reason,
                [.. reservation.Lines.Select(line => $"{line.Sku} ×{line.Quantity} · {line.WarehouseCode}")],
                reservation.CreatedAt,
                reservation.ExpiresAt))
        ]);
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

public sealed record CountStockResult(CountStockOutcome Outcome, int OnHand, int Available);

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

public sealed class CountStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/inventory/stock/{sku}/{warehouse}",
            async Task<Results<Ok<CountStockResponse>, NotFound<string>>> (
                   string sku, string warehouse, CountStockRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new CountStockCommand(sku, warehouse, request.OnHand), ct);

                return result.Outcome == CountStockOutcome.UnknownWarehouse
                    ? TypedResults.NotFound($"There is no warehouse '{warehouse}'.")
                    : TypedResults.Ok(new CountStockResponse(
                        sku, Warehouse.Normalise(warehouse), result.OnHand, result.Available));
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
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
