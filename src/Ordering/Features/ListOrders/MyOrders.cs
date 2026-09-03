using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.ListOrders;

/// <summary>
/// A shopper's own orders.
///
/// **A separate slice from `ListOrders`, deliberately.** That one is the
/// shopkeeper's and is allowed to return everything; this one must never. They
/// look similar enough that one query with a nullable customer filter is the
/// obvious move, and it is the wrong one — a null that means "everybody" sitting
/// one refactor away from a shopper's screen is a data leak the type system
/// would not have mentioned.
///
/// The customer comes from the principal and there is no parameter for it. An
/// endpoint that took a customer id would be an endpoint that reads anybody's
/// order history to whoever guesses a GUID, and the GUID is in their own order
/// confirmation.
///
/// This is what phase 5 meant by "an order is read by its id" being temporary:
/// the id was the credential until there was an account behind it.
/// </summary>
public sealed record MyOrdersQuery : IQuery<MyOrdersResult>;

public sealed record MyOrdersResult(IReadOnlyList<OrderSummary> Orders);

public sealed class MyOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/orders/mine
        //
        // Under /api/orders and not /api/accounts/me/orders, because the orders
        // belong to Ordering: routing them through Accounts would put a context
        // in front of a resource it does not own, and the URL would be the first
        // place that boundary blurred.
        app.MapGet("/api/orders/mine",
            async Task<Ok<MyOrdersResult>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new MyOrdersQuery(), ct)))
            .RequireAuthorization()
            .WithTags("Ordering")
            .WithName("MyOrders");
    }
}

public sealed class MyOrdersHandler(IOrderReader orders, IPrincipalAccessor principals)
    : IQueryHandler<MyOrdersQuery, MyOrdersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<MyOrdersResult> HandleAsync(
        MyOrdersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.my_orders");

        var customer = principals.Current.Customer;

        // Authenticated but not yet linked: the token is valid and no customer
        // exists behind it. An empty list rather than a refusal, because it is
        // the truthful answer — somebody who has never placed an order has no
        // orders, and telling them to call another endpoint first is telling
        // them about our wiring.
        if (customer is not { } id)
        {
            activity?.SetTag("ordering.unlinked", true);
            return new MyOrdersResult([]);
        }

        activity?.SetTag("ordering.customer_id", id.Value);

        var mine = await orders.ForCustomerAsync(id, cancellationToken);

        activity?.SetTag("ordering.orders", mine.Count);

        return new MyOrdersResult([.. mine.Select(OrderSummary.Of)]);
    }
}
