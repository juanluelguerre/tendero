using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
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

/// <summary>
/// The mandate an agent placed the order under (P11-5): what kind, which platform
/// key signed it, and what it approved. Enough to answer "who said this could be
/// bought?" from the list; the evidence itself stays on the order.
/// </summary>
public sealed record OrderMandateSummary(string Kind, string Issuer, Money ApprovedTotal, DateTimeOffset? ExpiresAt);

public sealed record OrderSummary(
    string OrderId,
    OrderStatus Status,
    /// <summary><c>Live</c>, or <c>Test</c> for a rehearsal a test credential placed (ADR 0038).</summary>
    OrderMode Mode,
    /// <summary>Null unless an agent placed it under a verified mandate (P11-5).</summary>
    OrderMandateSummary? Mandate,
    Money Total,
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
    /// Why the stock of an order a sales channel took could not be held (phase 16, S3;
    /// ADR 0070), in the ledger's words: the channel took the money, a person must act.
    /// Null for every other order.
    /// </summary>
    string? Oversold,

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
        order.Status,
        order.Mode,
        order.Mandate is { } mandate
            ? new OrderMandateSummary(mandate.Kind, mandate.Issuer, new Money(mandate.ApprovedTotal, mandate.Currency), mandate.ExpiresAt)
            : null,
        order.Total,
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
        order.Oversold,
        [
            .. order.Lines.Take(PreviewLines)
                .Select(line => new OrderLinePreview(line.Sku, line.ProductName, line.ImageId))
        ],
        order.CreatedAt);
}

/// <summary>How many orders match one status, for the list's filter (the redesign's R-4.2).</summary>
public sealed record OrderStatusCount(OrderStatus Status, int Count);

/// <summary>
/// A page of the orders that match, newest first, how many match in all, and the
/// count for every status — zero included, so a filter can say "Cancelled 0"
/// rather than vanish.
/// </summary>
/// <param name="Total">Of the whole filter, not of the page (R-7.2).</param>
public sealed record ListOrdersResult(
    IReadOnlyList<OrderSummary> Orders, int Total, int Page, int PageSize, IReadOnlyList<OrderStatusCount> Counts);

/// <summary>Narrowed by status, and by the start of an id or any part of a name or a city.</summary>
public sealed record ListOrdersQuery(
    OrderStatus? Status = null, string? Text = null, int Page = Paging.FirstPage, int PageSize = Paging.DefaultPageSize)
    : IQuery<ListOrdersResult>;

public sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(query => query.Page).IsAPage();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}

public sealed class ListOrdersHandler(IOrderReader orders) : IQueryHandler<ListOrdersQuery, ListOrdersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ListOrdersResult> HandleAsync(
        ListOrdersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.list_orders");
        activity?.SetTag("ordering.status_filter", query.Status?.ToString());
        activity?.SetTag("ordering.text_filter", !string.IsNullOrWhiteSpace(query.Text));

        var list = await orders.ListAsync(
            new OrderListQuery(query.Status, query.Text, query.Page, query.PageSize), cancellationToken);

        activity?.SetTag("ordering.order_count", list.Orders.Count);
        activity?.SetTag("ordering.total", list.Total);

        return new ListOrdersResult(
            [.. list.Orders.Select(OrderSummary.Of)],
            list.Total,
            query.Page,
            query.PageSize,
            [.. Enum.GetValues<OrderStatus>().Select(status =>
                new OrderStatusCount(status, list.CountByStatus.GetValueOrDefault(status)))]);
    }
}

/// <summary>
/// Moving an order along. Two verbs, because they are the two a shopkeeper
/// actually presses — and each is a side of the same saga: shipping commits the
/// stock and takes the money, delivering opens the return window.
/// </summary>
public enum OrderMove { Ship, Deliver, Cancel }

public sealed record MoveOrderRequest(OrderMove Move, string? Reason);

public sealed record MoveOrderCommand(OrderId OrderId, OrderMove Move, string? Reason)
    : ICommand<MoveOrderResult>;

public enum MoveOrderOutcome { Moved, UnknownOrder, IllegalTransition }

public sealed record MoveOrderResult(MoveOrderOutcome Outcome, Order? Order, string? Detail) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        MoveOrderOutcome.Moved => AuditOutcome.Allowed,
        MoveOrderOutcome.UnknownOrder or MoveOrderOutcome.IllegalTransition => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome == MoveOrderOutcome.Moved ? null : Detail ?? Outcome.ToString();
}

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

/// <summary>What moving an order is refused for, by code.</summary>
public static class OrderRefusals
{
    public static readonly Refusal UnknownOrder = new(
        "UnknownOrder", 404, "No hay ningún pedido con ese identificador.", "There is no order with that id.");

    /// <summary>The state machine's own no (invariant 5): the table says which moves an order has from where it is.</summary>
    public static readonly Refusal IllegalTransition = new(
        "IllegalTransition", 409, "El pedido no admite ese paso desde donde está.", "The order does not take that step from where it is.");
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

        // GET /api/orders?status=Confirmed&q=01a0e&page=1&pageSize=20
        // The status is read by its name, in any case, and a name the set does
        // not have is the binding's 400 (ADR 0043), so the handler never sees one.
        orders.MapGet("/",
            async Task<Ok<ListOrdersResult>> (
                   WireEnum<OrderStatus>? status, string? q, int? page, int? pageSize, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ListOrdersQuery(status?.Value, q, page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize), ct)))
            .WithName("ListOrders");

        orders.MapPost("/{orderId:guid}/move",
            async Task<Results<Ok<OrderSummary>, ProblemHttpResult>> (
                   Guid orderId, MoveOrderRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new MoveOrderCommand(new OrderId(orderId), request.Move, request.Reason), ct);

                return result.Outcome switch
                {
                    MoveOrderOutcome.UnknownOrder => TypedResults.Problem(statusCode: OrderRefusals.UnknownOrder.Status, extensions: OrderRefusals.UnknownOrder.Said(path: "$.orderId")),
                    MoveOrderOutcome.IllegalTransition => TypedResults.Problem(statusCode: OrderRefusals.IllegalTransition.Status, extensions: OrderRefusals.IllegalTransition.Said(path: "$.to", reason: result.Detail)),
                    _ => TypedResults.Ok(OrderSummary.Of(result.Order!))
                };
            })
            .WithMetadata(new Refuses(OrderRefusals.UnknownOrder, OrderRefusals.IllegalTransition))
            .WithName("MoveOrder");
    }

}
