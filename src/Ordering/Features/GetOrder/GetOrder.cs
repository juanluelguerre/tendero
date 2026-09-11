using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Contracts;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.GetOrder;

/// <summary>
/// One order, with whatever returns have been opened against it.
///
/// Both together because they are one page: the confirmation screen becomes the
/// returns screen a week later, and asking twice for what is always read at once
/// is a round trip that buys nothing.
/// </summary>
public sealed record GetOrderQuery(OrderId OrderId) : IQuery<OrderDetail?>;

public sealed record OrderDetail(OrderView Order, IReadOnlyList<ReturnView> Returns);

public sealed class GetOrderHandler(
    IOrderRepository orders, IReturnRequestRepository returns, OrderAccess access)
    : IQueryHandler<GetOrderQuery, OrderDetail?>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<OrderDetail?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.get_order");
        activity?.SetTag("ordering.order_id", query.OrderId.Value);

        var order = await orders.FindByIdAsync(query.OrderId, cancellationToken);

        if (order is null || !await access.CanReadAsync(order, cancellationToken))
            return null;

        return new OrderDetail(
            OrderView.From(order),
            [.. (await returns.ForOrderAsync(order.Id, cancellationToken)).Select(ReturnView.From)]);
    }
}

public sealed class GetOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders/{orderId:guid}",
            async Task<Results<Ok<OrderDetail>, NotFound<string>>> (
                   Guid orderId, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var detail = await dispatcher.SendAsync(new GetOrderQuery(new OrderId(orderId)), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return detail is null
                    ? TypedResults.NotFound($"There is no order {orderId}.")
                    : TypedResults.Ok(detail);
            })
            // Anonymous at the door, and not for everybody inside it. For a
            // GUEST's order **the id is the credential**: a guest has to reload
            // their confirmation page and come back a week later to send
            // something back, with no account to scope it to. `OrderId` is a
            // GUID v7 — the timestamp half is guessable, the 74 random bits are
            // not.
            //
            // An account holder's order is theirs and the shopkeeper's, and to
            // anybody else it is a 404 (OrderAccess, in the handler). This
            // comment used to say the line would change "the moment Accounts
            // exists"; it changed on 2026-09-11, a week after Accounts did.
            .AllowAnonymous()
            .WithTags("Orders")
            .WithName("GetOrder");
    }
}
