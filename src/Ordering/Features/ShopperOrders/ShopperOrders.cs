using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ElGuerre.Tendero.Ordering.Contracts;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Ordering.Features.ShopperOrders;

/// <summary>
/// The orders the asking shopper may be answered about (phase 12b, the Tino spec,
/// *After-sale*): the one on their screen, when there is one and they may read it,
/// and — signed in — their most recent ones.
///
/// **Nothing here is found by a number somebody typed.** A signed-in shopper's
/// orders are found through the principal. The order on the screen is found by its
/// id, which the browser sent beside the question and no model ever held, and is
/// read by the rule the order's own page reads it by (<see cref="OrderAccess"/>):
/// an account holder's order is theirs, and a guest's is read with its id.
/// </summary>
/// <param name="OnScreen">The order the shopper is looking at, if any.</param>
public sealed record ShopperOrdersQuery(OrderId? OnScreen) : IQuery<ShopperOrdersResult>;

/// <param name="SignedIn">Whether there is a customer behind the asker: with neither that nor an order, there is nobody to answer about.</param>
public sealed record ShopperOrdersResult(bool SignedIn, IReadOnlyList<ShopperOrder> Orders);

/// <param name="DeliveredAt">When it was delivered, which is when the days to return it start.</param>
/// <param name="ReturnBy">The last moment a return can be opened; null until it is delivered.</param>
/// <param name="CanReturnNow">Whether that moment has not passed.</param>
public sealed record ShopperOrder(
    string OrderId, bool OnScreen, OrderStatus Status, DateTimeOffset PlacedAt, DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReturnBy, bool CanReturnNow, IReadOnlyList<ShopperOrderLine> Lines, decimal Total, string Currency);

/// <param name="Returnable">How many of the line can still be sent back: what the return command would accept.</param>
public sealed record ShopperOrderLine(string Name, string? Variant, int Quantity, int Returnable);

public sealed class ShopperOrdersHandler(
    IOrderRepository orders, IOrderReader reader, IReturnRequestRepository returns, OrderAccess access,
    IPrincipalAccessor principals, TimeProvider clock)
    : IQueryHandler<ShopperOrdersQuery, ShopperOrdersResult>
{
    /// <summary>A signed-in shopper's most recent orders, at most: a question is about the last one, or the one before.</summary>
    public const int Recent = 3;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ShopperOrdersResult> HandleAsync(ShopperOrdersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.shopper_orders");
        var found = new List<(Order Order, bool OnScreen)>();

        // The order on the screen, by the rule its own page reads it by. One the
        // asker may not read is not there, and nothing says that it exists.
        if (query.OnScreen is { } id && await orders.FindByIdAsync(id, cancellationToken) is { } shown
            && await access.CanReadAsync(shown, cancellationToken))
            found.Add((shown, true));

        var customer = principals.Current.Customer;
        if (customer is { } mine)
        {
            var recent = (await reader.ForCustomerAsync(mine, cancellationToken))
                .OrderByDescending(order => order.CreatedAt)
                .Where(order => found.All(known => known.Order.Id != order.Id))
                .Take(Recent - found.Count);
            found.AddRange(recent.Select(order => (order, false)));
        }

        var now = clock.GetUtcNow();
        var answer = new List<ShopperOrder>(found.Count);
        foreach (var (order, onScreen) in found)
        {
            var opened = await returns.ForOrderAsync(order.Id, cancellationToken);

            // The same two questions the return command asks before it accepts
            // anything: how many of each line are left, and whether the window is open.
            var left = ReturnRequest.Returnable(order, opened);
            var canReturn = ReturnRequest.WindowIsOpen(order, now);
            DateTimeOffset? delivered = order.Status == OrderStatus.Delivered ? order.UpdatedAt : null;
            var view = OrderView.From(order);

            answer.Add(new ShopperOrder(
                view.OrderId, onScreen, order.Status, order.CreatedAt, delivered, delivered + ReturnRequest.Window, canReturn,
                [
                    .. view.Lines.Select(line => new ShopperOrderLine(
                        line.ProductName, line.VariantLabel, line.Quantity,
                        canReturn ? Math.Max(0, left.GetValueOrDefault(line.Sku)) : 0)),
                ],
                view.Total, view.Currency));
        }

        activity?.SetTag("ordering.orders", answer.Count);
        return new ShopperOrdersResult(customer is not null, answer);
    }
}

// ---------- the read ----------

public sealed record MyOrdersArguments(
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

/// <param name="Orders">The shopper's orders, each under a reference of this answer: order1, order2.</param>
/// <param name="Notice">What the shop says when there is nobody to answer about.</param>
public sealed record OrdersForShopper(IReadOnlyList<OrderForShopper> Orders, string? Notice);

/// <summary>
/// An order as the model reads it: what it says, under a reference that means
/// nothing outside this answer. No id, and nothing that says who bought it or
/// where it went.
/// </summary>
/// <param name="Reference">order1, order2: how an answer names it.</param>
/// <param name="OnScreen">Whether it is the one the shopper is looking at.</param>
/// <param name="Says">Everything the shop holds of it that an answer may cite, a sentence each.</param>
public sealed record OrderForShopper(string Reference, bool OnScreen, string Title, IReadOnlyList<string> Says);

/// <summary>
/// <c>my_orders</c> (phase 12b): the asking shopper's orders, as sentences an answer
/// may cite — when each was placed and delivered, what it holds, and what of it can
/// still be returned and until which day. **It takes no order number**: a number
/// typed into a chat reads nothing here, because nothing here reads one. The order
/// the shopper is looking at comes from <see cref="ShopperScreen"/>, which the turn
/// fills from what the browser sent and the model never saw.
/// </summary>
public sealed class MyOrdersTool(IQueryDispatcher queries, ShopperScreen screen, IOptions<ShopOptions> shop)
    : ReadTool<MyOrdersArguments, OrdersForShopper>(queries)
{
    /// <summary>The real ids, by reference, for the read's subjects: never for its data.</summary>
    private readonly Dictionary<string, string> ids = [];

    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "my_orders";

    public override string Description =>
        "The orders of the shopper who is asking: the one they are looking at, and their most recent ones when they are signed in. For each: when it was placed and delivered, its status, what it holds, and what can still be returned and until which day. It takes no order number — never ask the shopper for one.";

    protected override async Task<(OrdersForShopper Data, int Rows, int? Total)> ReadAsync(
        MyOrdersArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var culture = CultureNegotiation.Resolve(arguments.Culture, default);
        var onScreen = Guid.TryParse(screen.OrderId, out var id) ? new OrderId(id) : (OrderId?)null;
        var result = await queries.SendAsync(new ShopperOrdersQuery(onScreen), cancellationToken);

        var words = new OrderWords(culture, DateTimeZoneProviders.Tzdb[shop.Value.TimeZone]);
        this.ids.Clear();
        var said = result.Orders.Select((order, index) =>
        {
            var reference = $"order{index + 1}";
            this.ids[reference] = order.OrderId;
            // The composing call reads the sentences and nothing else, so which order
            // the shopper is looking at is one of them.
            IReadOnlyList<string> says = order.OnScreen ? [words.OnTheScreen, .. words.Of(order)] : words.Of(order);
            return new OrderForShopper(reference, order.OnScreen, words.Title(order), says);
        }).ToList();

        // Nobody to answer about is said, not left as an empty list: a guest who is
        // not on their order's page is told how to ask.
        var notice = said.Count == 0 && !result.SignedIn ? words.SignInOrOpenTheOrder : null;
        return (new OrdersForShopper(said, notice), said.Count, null);
    }

    protected override IReadOnlyList<Fact> Facts(OrdersForShopper data) =>
    [
        .. data.Orders.SelectMany(order => order.Says.Select(sentence => new Fact(order.Reference, FactSource.Order, sentence))),
        .. data.Notice is { } notice ? [new Fact(Fact.TheShop, FactSource.Notice, notice)] : Array.Empty<Fact>(),
    ];

    protected override IReadOnlyList<Subject> Subjects(OrdersForShopper data) =>
        [.. data.Orders.Select(order => new Subject(order.Reference, order.Title, SubjectKind.Order, this.ids.GetValueOrDefault(order.Reference)))];
}

/// <summary>
/// An order in the shopper's words, es and en (invariant 6), with its days as the
/// shop counts them (ADR 0046). The months are written out here because the host
/// runs with invariant globalization: a culture's month names are not there to ask.
/// </summary>
internal sealed class OrderWords(string culture, DateTimeZone zone)
{
    private static readonly string[] Meses =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static readonly string[] Months =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private bool English => culture == "en";

    public string SignInOrOpenTheOrder => English
        ? "To ask about an order, sign in, or open the order from its confirmation email."
        : "Para preguntar por un pedido, inicia sesión o abre el pedido desde su correo de confirmación.";

    public string OnTheScreen => English
        ? "This is the order the shopper is looking at."
        : "Este es el pedido que el comprador está viendo.";

    public string Title(ShopperOrder order) => English ? $"Order of {Day(order.PlacedAt)}" : $"Pedido del {Day(order.PlacedAt)}";

    public IReadOnlyList<string> Of(ShopperOrder order)
    {
        var says = new List<string>
        {
            English ? $"The order was placed on {Day(order.PlacedAt)}." : $"El pedido se hizo el {Day(order.PlacedAt)}.",
            English ? $"Its status is: {Status(order.Status)}." : $"Su estado es: {Status(order.Status)}.",
        };
        if (order.DeliveredAt is { } delivered)
            says.Add(English ? $"It was delivered on {Day(delivered)}." : $"Se entregó el {Day(delivered)}.");

        says.AddRange(order.Lines.Select(line => English ? $"It holds {Item(line, line.Quantity)}." : $"Incluye {Item(line, line.Quantity)}."));
        says.Add(string.Create(CultureInfo.InvariantCulture, $"Total: {order.Total:0.00} {order.Currency}."));

        switch (order)
        {
            case { CanReturnNow: true, ReturnBy: { } until }:
                var returnable = order.Lines.Where(line => line.Returnable > 0).ToList();
                says.AddRange(returnable.Select(line => English
                    ? $"{Item(line, line.Returnable)} can be returned until {Day(until)}."
                    : $"Se puede devolver {Item(line, line.Returnable)} hasta el {Day(until)}."));
                if (returnable.Count == 0)
                    says.Add(English ? "Nothing of it is left to return." : "No queda nada de él por devolver.");
                break;
            case { ReturnBy: { } ended }:
                says.Add(English ? $"The time to return it ended on {Day(ended)}." : $"El plazo para devolverlo terminó el {Day(ended)}.");
                break;
            case { Status: OrderStatus.Cancelled or OrderStatus.PaymentFailed }:
                break;
            default:
                var days = (int)ReturnRequest.Window.TotalDays;
                says.Add(English
                    ? $"It cannot be returned yet: the {days} days to return it start when it is delivered."
                    : $"Todavía no se puede devolver: los {days} días para devolverlo empiezan cuando se entrega.");
                break;
        }

        return says;
    }

    private static string Item(ShopperOrderLine line, int quantity) =>
        $"{quantity} × {line.Name}" + (line.Variant is { Length: > 0 } variant ? $" ({variant})" : "");

    private string Day(DateTimeOffset moment)
    {
        var day = Instant.FromDateTimeOffset(moment).InZone(zone).Date;
        return English ? $"{day.Day} {Months[day.Month - 1]} {day.Year}" : $"{day.Day} de {Meses[day.Month - 1]} de {day.Year}";
    }

    private string Status(OrderStatus status) => (status, English) switch
    {
        (OrderStatus.Pending, true) => "waiting for payment",
        (OrderStatus.Pending, false) => "pendiente de pago",
        (OrderStatus.PaymentAuthorized, true) => "payment authorised, being confirmed",
        (OrderStatus.PaymentAuthorized, false) => "pago autorizado, confirmándose",
        (OrderStatus.PaymentFailed, true) => "the payment failed",
        (OrderStatus.PaymentFailed, false) => "el pago falló",
        (OrderStatus.Confirmed, true) => "confirmed, being prepared",
        (OrderStatus.Confirmed, false) => "confirmado, en preparación",
        (OrderStatus.Shipped, true) => "shipped, on its way",
        (OrderStatus.Shipped, false) => "enviado, en camino",
        (OrderStatus.Delivered, true) => "delivered",
        (OrderStatus.Delivered, false) => "entregado",
        (OrderStatus.Cancelled, true) => "cancelled",
        _ => "cancelado",
    };
}
