using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.ListOrders;

/// <summary>
/// One row of the orders table.
///
/// It is a summary and not an <c>OrderView</c>: a list of thirty orders that
/// each carried their lines, discounts and tax breakdown would be a page that
/// downloads a database to render six columns. The detail page asks for the
/// detail.
/// </summary>
/// <summary>
/// Enough of a line to recognise an order by. Not the line itself.
/// </summary>
public sealed record OrderLinePreview(string Sku, string Name, string? ImageId);

public sealed record OrderSummary(
    string OrderId,
    string Status,
    string Currency,
    decimal Total,
    int LineCount,
    string RecipientName,
    string City,
    bool IsPaid,
    bool IsCaptured,
    /// <summary>Why it stopped, when it did. Null for an order still in flight.
    /// The code is localized by the interface; the detail is SKUs and numbers.
    /// </summary>
    string? StopCode,
    string? StopDetail,

    /// <summary>
    /// The first few things in the order, so a person can tell one from another.
    ///
    /// **A preview and not the lines**, which is the distinction this record's
    /// own docblock already draws: thirty orders each carrying their lines,
    /// discounts and tax breakdown is a page that downloads a database to render
    /// six columns. Three names and three image keys is what recognition costs,
    /// and the detail page still asks for the detail.
    ///
    /// "9 lines, 624.48 EUR" describes an order that nobody remembers buying.
    /// </summary>
    IReadOnlyList<OrderLinePreview> Preview,

    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// The one projection, used by the shopkeeper's list, by a status move and
    /// by a shopper's own history.
    ///
    /// It was written out three times before the third caller arrived, which is
    /// the moment a copy becomes a divergence waiting to happen: a column added
    /// to one of them would have been silently missing from the other two.
    /// </summary>
    /// <summary>Three fits a row at every width the storefront renders. A fourth
    /// wraps on a phone, and the count beside them already says there are more.</summary>
    private const int PreviewLines = 3;

    public static OrderSummary Of(Order order) => new(
        // Flat. OrderId is a record struct and would serialise as {"value":"…"}.
        order.Id.ToString(),
        order.Status.ToString(),
        order.Currency,
        order.Total.Amount,
        order.Lines.Count,
        order.ShippingAddress.RecipientName,
        order.ShippingAddress.City,
        // Two booleans and not one status string: authorised and captured are
        // different facts, and a shopkeeper chasing unpaid shipments needs to
        // tell them apart.
        order.Payment is not null,
        order.Payment?.CaptureId is not null,
        order.Stop?.Code,
        order.Stop?.Detail,
        [
            .. order.Lines.Take(PreviewLines)
                .Select(line => new OrderLinePreview(line.Sku, line.ProductName, line.ImageId))
        ],
        order.CreatedAt);
}

public sealed record ListOrdersResult(IReadOnlyList<OrderSummary> Orders);

public sealed record ListOrdersQuery : IQuery<ListOrdersResult>;

public sealed class ListOrdersHandler(IOrderReader orders) : IQueryHandler<ListOrdersQuery, ListOrdersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ListOrdersResult> HandleAsync(
        ListOrdersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.list_orders");

        var rows = await orders.RecentAsync(cancellationToken);

        activity?.SetTag("ordering.order_count", rows.Count);

        return new ListOrdersResult([.. rows.Select(OrderSummary.Of)]);
    }
}

/// <summary>
/// Moving an order along. Two verbs, because they are the two a shopkeeper
/// actually presses — and each is a side of the same saga: shipping commits the
/// stock and takes the money, delivering opens the return window.
/// </summary>
public enum OrderMove { Ship, Deliver, Cancel }

public sealed record MoveOrderRequest(string Move, string? Reason);

public sealed record MoveOrderCommand(OrderId OrderId, OrderMove Move, string? Reason)
    : ICommand<MoveOrderResult>;

public enum MoveOrderOutcome { Moved, UnknownOrder, IllegalTransition }

public sealed record MoveOrderResult(MoveOrderOutcome Outcome, Order? Order, string? Detail);

public sealed class MoveOrderHandler(
    IOrderRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<MoveOrderCommand, MoveOrderResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<MoveOrderResult> HandleAsync(
        MoveOrderCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.move_order");
        activity?.SetTag("ordering.move", command.Move.ToString());

        var order = await orders.FindByIdAsync(command.OrderId, cancellationToken);

        if (order is null)
            return new MoveOrderResult(MoveOrderOutcome.UnknownOrder, null, null);

        try
        {
            switch (command.Move)
            {
                case OrderMove.Ship:
                    order.Ship(clock);
                    break;
                case OrderMove.Deliver:
                    order.Deliver(clock);
                    break;
                case OrderMove.Cancel:
                    order.Cancel(clock, OrderStop.ShopCancelled, command.Reason);
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            // `AllowedTransitions` is the single source of truth, and this is
            // what turns its refusal into an answer. The screen never has to
            // reimplement the table to know which buttons to show — it shows
            // them from the same status the table reads.
            return new MoveOrderResult(MoveOrderOutcome.IllegalTransition, order, exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MoveOrderResult(MoveOrderOutcome.Moved, order, null);
    }
}

public sealed class OrderAdminEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders")
            // What a shop has sold is commercial information, and a customer
            // reads their own order through its id (see GetOrder) rather than
            // through this.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Orders");

        orders.MapGet("/",
            async Task<Ok<ListOrdersResult>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListOrdersQuery(), ct)))
            .WithName("ListOrders");

        orders.MapPost("/{orderId:guid}/move",
            async Task<Results<Ok<OrderSummary>, BadRequest<string>, NotFound<string>, Conflict<string>>> (
                   Guid orderId, MoveOrderRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                if (!Enum.TryParse<OrderMove>(request.Move, ignoreCase: true, out var move))
                    return TypedResults.BadRequest(
                        $"A move is one of {string.Join(", ", Enum.GetNames<OrderMove>())}.");

                var result = await dispatcher.SendAsync(
                    new MoveOrderCommand(new OrderId(orderId), move, request.Reason), ct);

                return result.Outcome switch
                {
                    MoveOrderOutcome.UnknownOrder => TypedResults.NotFound($"There is no order {orderId}."),
                    MoveOrderOutcome.IllegalTransition => TypedResults.Conflict(result.Detail!),
                    _ => TypedResults.Ok(OrderSummary.Of(result.Order!))
                };
            })
            .WithName("MoveOrder");
    }

}
