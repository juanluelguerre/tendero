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

public sealed class GetOrderHandler(IOrderRepository orders, IReturnRequestRepository returns)
    : IQueryHandler<GetOrderQuery, OrderDetail?>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<OrderDetail?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.get_order");
        activity?.SetTag("ordering.order_id", query.OrderId.Value);

        var order = await orders.FindByIdAsync(query.OrderId, cancellationToken);

        if (order is null)
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
            // **The id is the credential**, and that is a decision rather than an
            // omission. A guest has to be able to reload their confirmation page
            // and come back a week later to send something back, and until phase
            // 7 there is no account to scope it to. `OrderId` is a GUID v7:
            // sortable, which means the timestamp half is guessable, and 74 bits
            // of it are not — a number nobody enumerates.
            //
            // It is also the weakest thing on this endpoint, and it stops being
            // true the moment `Accounts` exists. The line to change is this one.
            .AllowAnonymous()
            .WithTags("Orders")
            .WithName("GetOrder");
    }
}
