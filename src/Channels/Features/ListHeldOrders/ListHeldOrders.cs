using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Channels.Features.ListHeldOrders;

/// <summary>
/// The orders a channel took and charged for that Tendero could not receive (P16-29):
/// what the orders screen shows above its list, only when there are any, so the person
/// who ships knows an order is paid and waiting, and why.
/// </summary>
public sealed record ListHeldOrdersQuery(string Culture) : IQuery<HeldOrdersView>;

/// <param name="ExternalId">The channel's id for the order.</param>
/// <param name="Number">What the channel's customer and shopkeeper call it (<c>#1013</c>).</param>
/// <param name="Reason">Why it is held: a code to compose a sentence from, what it concerns, and the shop's sentence in the language asked.</param>
/// <param name="Page">The order in the channel's own admin, where it can be mended or closed; null when the channel has none.</param>
public sealed record HeldOrderView(
    string Channel,
    string ExternalId,
    string Number,
    Message<HeldOrderReason> Reason,
    string? Page,
    DateTimeOffset HeldSince,
    DateTimeOffset LastTriedAt);

/// <param name="Orders">Oldest held first: the one waiting longest is the one to act on.</param>
public sealed record HeldOrdersView(IReadOnlyList<HeldOrderView> Orders);

public sealed class ListHeldOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/channels/held-orders",
            async Task<Ok<HeldOrdersView>> (string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                var view = await dispatcher.SendAsync(new ListHeldOrdersQuery(resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";
                return TypedResults.Ok(view);
            })
            // Whoever sees the orders: a held order is an order to ship that has not arrived.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Channels")
            .WithName("ListHeldOrders");
    }
}

public sealed class ListHeldOrdersHandler(IHeldChannelOrders held) : IQueryHandler<ListHeldOrdersQuery, HeldOrdersView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Channels);

    public async Task<HeldOrdersView> HandleAsync(ListHeldOrdersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("channels.list_held_orders");

        var orders = await held.AllAsync(cancellationToken);
        activity?.SetTag("channels.held_orders", orders.Count);

        return new HeldOrdersView(
        [
            .. orders.OrderBy(order => order.HeldSince).Select(order => new HeldOrderView(
                order.Channel,
                order.ExternalId,
                order.Number,
                new Message<HeldOrderReason>(order.Reason, order.About, Sentence(order, query.Culture)),
                order.Page,
                order.HeldSince,
                order.LastTriedAt)),
        ]);
    }

    /// <summary>The shop's sentence, for an agent with no front end; a front end composes its own from the code.</summary>
    private static string Sentence(HeldChannelOrder order, string culture)
    {
        var spanish = culture.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        return order.Reason switch
        {
            HeldOrderReason.NotInCatalogue => spanish
                ? $"El pedido {order.Number} vende {order.About}, que el catálogo no tiene."
                : $"Order {order.Number} sells {order.About}, which the catalogue does not.",
            HeldOrderReason.NoAddress => spanish
                ? $"El pedido {order.Number} no tiene adónde enviarse."
                : $"Order {order.Number} has nowhere to be sent.",
            HeldOrderReason.TotalsDoNotAddUp => spanish
                ? $"Los importes del pedido {order.Number} no cuadran con los de la tienda."
                : $"Order {order.Number}'s figures do not add up by the shop's arithmetic.",
        };
    }
}
