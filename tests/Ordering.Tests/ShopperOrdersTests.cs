using System.Text.Json;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ShopperOrders;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The orders a shopper is answered about (phase 12b, the Tino spec, *After-sale*):
/// theirs and nobody else's, found by who is asking and by the order on their
/// screen — never by a number anybody typed.
/// </summary>
public sealed class ShopperOrdersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>1 January 2026, 10:00 in Madrid.</summary>
    private static readonly DateTimeOffset NewYear = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly CustomerId Ana = CustomerId.New();
    private static readonly CustomerId Luis = CustomerId.New();

    private static CommercePrincipal SignedIn(CustomerId customer) => new(customer, null, "sub", "A shopper", [TenderoRoles.Shopper]);

    private static OrderLine Boots(int quantity = 1) =>
        new(ProductId.New(), VariantId.New(), "BOOTS-42", "Botines de piel", "42", ImageId: null, new Money(89.90m, "EUR"), quantity);

    private static OrderLine Socks(int quantity = 2) =>
        new(ProductId.New(), VariantId.New(), "SOCKS-M", "Calcetines de lana", null, ImageId: null, new Money(9.50m, "EUR"), quantity);

    private static Order AnOrder(CustomerId customer, TestClock clock, OrderStatus status, params OrderLine[] lines) =>
        OrderBuilder.Default().ForCustomer(customer).WithLines(lines.Length > 0 ? lines : [Boots()]).At(clock).InStatus(status);

    // ---------- the shop ----------

    private sealed class Orders(params Order[] all) : IOrderRepository, IOrderReader
    {
        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(all.FirstOrDefault(order => order.Id == id));

        public Task<IReadOnlyList<Order>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([.. all.Where(order => order.CustomerId == customer)]);

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Add(Order order) => throw new NotSupportedException();

        public Task<OrderList> ListAsync(OrderListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Returns(params ReturnRequest[] all) : IReturnRequestRepository
    {
        public Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(OrderId order, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReturnRequest>>([.. all.Where(request => request.OrderId == order)]);

        public Task<ReturnRequest?> FindByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReturnRequest>> OpenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Add(ReturnRequest request) => throw new NotSupportedException();
    }

    /// <summary>Who has an account: an order of theirs is theirs alone; anybody else's is a guest's.</summary>
    private sealed class Accounts(params CustomerId[] registered) : IRegisteredCustomers
    {
        public Task<bool> HasAccountAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
            Task.FromResult(registered.Contains(customer));
    }

    private sealed class Asking(CommercePrincipal principal) : IPrincipalAccessor
    {
        public CommercePrincipal Current => principal;
    }

    private sealed class Dispatcher(ShopperOrdersHandler handler) : IQueryDispatcher
    {
        public ShopperOrdersQuery? Asked { get; private set; }

        public async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (ShopperOrdersQuery)(object)query;
            return (TResult)(object)await handler.HandleAsync(Asked, cancellationToken);
        }
    }

    private static ShopperOrdersHandler Handler(
        CommercePrincipal asker, Order[] orders, TestClock clock, ReturnRequest[]? returns = null, CustomerId[]? accounts = null)
    {
        var all = new Orders(orders);
        var principals = new Asking(asker);
        return new ShopperOrdersHandler(all, all, new Returns(returns ?? []), new OrderAccess(new Accounts(accounts ?? [Ana, Luis]), principals),
            principals, clock);
    }

    private static Task<ShopperOrdersResult> Ask(
        CommercePrincipal asker, Order[] orders, TestClock clock, OrderId? onScreen = null, ReturnRequest[]? returns = null,
        CustomerId[]? accounts = null) =>
        Handler(asker, orders, clock, returns, accounts).HandleAsync(new ShopperOrdersQuery(onScreen), Ct);

    // ---------- whose orders ----------

    [Fact]
    public async Task A_signed_in_shopper_is_answered_about_their_own_most_recent_orders_and_no_one_elses()
    {
        var clock = new TestClock(NewYear);
        var first = AnOrder(Ana, clock, OrderStatus.Delivered);
        clock.Advance(TimeSpan.FromDays(1));
        var second = AnOrder(Ana, clock, OrderStatus.Shipped);
        clock.Advance(TimeSpan.FromDays(1));
        var third = AnOrder(Ana, clock, OrderStatus.Confirmed);
        clock.Advance(TimeSpan.FromDays(1));
        var fourth = AnOrder(Ana, clock, OrderStatus.Pending);
        var someoneElses = AnOrder(Luis, clock, OrderStatus.Delivered);

        var result = await Ask(SignedIn(Ana), [first, someoneElses, second, third, fourth], clock);

        Assert.True(result.SignedIn);
        // The three most recent, newest first; the oldest is not a question's subject.
        Assert.Equal([fourth.Id.ToString(), third.Id.ToString(), second.Id.ToString()], result.Orders.Select(order => order.OrderId));
        Assert.All(result.Orders, order => Assert.False(order.OnScreen));
    }

    /// <summary>The isolation the spec names: signed in, with another shopper's order id on the screen.</summary>
    [Fact]
    public async Task Another_shoppers_order_on_the_screen_is_not_there_and_the_askers_own_still_are()
    {
        var clock = new TestClock(NewYear);
        var mine = AnOrder(Ana, clock, OrderStatus.Shipped);
        var theirs = AnOrder(Luis, clock, OrderStatus.Delivered);

        var result = await Ask(SignedIn(Ana), [mine, theirs], clock, onScreen: theirs.Id);

        Assert.Equal([mine.Id.ToString()], result.Orders.Select(order => order.OrderId));
        Assert.DoesNotContain(result.Orders, order => order.OnScreen);
    }

    [Fact]
    public async Task Nobody_signed_in_reads_an_account_holders_order_by_its_id()
    {
        var clock = new TestClock(NewYear);
        var theirs = AnOrder(Ana, clock, OrderStatus.Delivered);

        var result = await Ask(CommercePrincipal.Anonymous, [theirs], clock, onScreen: theirs.Id);

        Assert.False(result.SignedIn);
        Assert.Empty(result.Orders);
    }

    /// <summary>A guest's order has no account to scope it to: its id is the credential, and here only the browser holds it.</summary>
    [Fact]
    public async Task A_guest_is_answered_about_the_order_on_their_screen_and_no_other()
    {
        var clock = new TestClock(NewYear);
        var guest = CustomerId.New();
        var theirOrder = AnOrder(guest, clock, OrderStatus.Shipped);
        var anotherOfTheirs = AnOrder(guest, clock, OrderStatus.Delivered);

        var result = await Ask(CommercePrincipal.Anonymous, [theirOrder, anotherOfTheirs], clock, onScreen: theirOrder.Id, accounts: []);

        var order = Assert.Single(result.Orders);
        Assert.Equal((theirOrder.Id.ToString(), true, OrderStatus.Shipped), (order.OrderId, order.OnScreen, order.Status));
        Assert.False(result.SignedIn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_id_that_is_no_order_reads_nothing(bool signedIn)
    {
        var clock = new TestClock(NewYear);

        var result = await Ask(signedIn ? SignedIn(Ana) : CommercePrincipal.Anonymous, [AnOrder(Luis, clock, OrderStatus.Delivered)], clock,
            onScreen: OrderId.New());

        Assert.Empty(result.Orders);
    }

    [Fact]
    public async Task The_order_on_the_screen_comes_first_and_is_not_listed_twice()
    {
        var clock = new TestClock(NewYear);
        var old = AnOrder(Ana, clock, OrderStatus.Delivered);
        clock.Advance(TimeSpan.FromDays(30));
        var recent = Enumerable.Range(0, 3).Select(_ => { clock.Advance(TimeSpan.FromHours(1)); return AnOrder(Ana, clock, OrderStatus.Confirmed); }).ToArray();

        var result = await Ask(SignedIn(Ana), [old, .. recent], clock, onScreen: old.Id);

        Assert.Equal(ShopperOrdersHandler.Recent, result.Orders.Count);
        Assert.Equal((old.Id.ToString(), true), (result.Orders[0].OrderId, result.Orders[0].OnScreen));
        Assert.Equal([recent[2].Id.ToString(), recent[1].Id.ToString()], result.Orders.Skip(1).Select(order => order.OrderId));
    }

    // ---------- what can be returned, and until when ----------

    [Fact]
    public async Task A_delivered_order_can_be_returned_until_the_window_from_its_delivery_ends_line_by_line()
    {
        var clock = new TestClock(NewYear);
        var order = AnOrder(Ana, clock, OrderStatus.Delivered, Boots(), Socks(2));
        var returned = ReturnRequest.Open(clock, order, [new(order.Lines[1].VariantId, "SOCKS-M", 1, ReturnReason.WrongSize, null)], earlier: []);
        clock.Advance(TimeSpan.FromDays(3));

        var answered = Assert.Single((await Ask(SignedIn(Ana), [order], clock, returns: [returned])).Orders);

        Assert.Equal((NewYear, NewYear + ReturnRequest.Window, true), (answered.DeliveredAt, answered.ReturnBy, answered.CanReturnNow));
        // What the return command would accept: one pair of socks is already on its way back.
        Assert.Equal([("Botines de piel", "42", 1, 1), ("Calcetines de lana", null, 2, 1)],
            answered.Lines.Select(line => (line.Name, line.Variant, line.Quantity, line.Returnable)));
        Assert.Equal((order.Totals.Total.Amount, "EUR"), (answered.Total, answered.Currency));
    }

    [Fact]
    public async Task Past_the_window_nothing_can_be_returned_and_the_day_it_ended_is_known()
    {
        var clock = new TestClock(NewYear);
        var order = AnOrder(Ana, clock, OrderStatus.Delivered);
        clock.Advance(ReturnRequest.Window + TimeSpan.FromSeconds(1));

        var answered = Assert.Single((await Ask(SignedIn(Ana), [order], clock)).Orders);

        Assert.Equal((false, NewYear + ReturnRequest.Window, 0), (answered.CanReturnNow, answered.ReturnBy, answered.Lines.Single().Returnable));
    }

    [Fact]
    public async Task At_the_windows_last_moment_it_can_still_be_returned()
    {
        var clock = new TestClock(NewYear);
        var order = AnOrder(Ana, clock, OrderStatus.Delivered);
        clock.Advance(ReturnRequest.Window);

        Assert.True(Assert.Single((await Ask(SignedIn(Ana), [order], clock)).Orders).CanReturnNow);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task An_order_that_was_not_delivered_has_no_day_to_return_it_by(OrderStatus status)
    {
        var clock = new TestClock(NewYear);

        var answered = Assert.Single((await Ask(SignedIn(Ana), [AnOrder(Ana, clock, status)], clock)).Orders);

        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null, false, 0),
            (answered.DeliveredAt, answered.ReturnBy, answered.CanReturnNow, answered.Lines.Single().Returnable));
    }

    // ---------- the read ----------

    private static (MyOrdersTool Tool, Dispatcher Dispatcher) Tool(
        CommercePrincipal asker, Order[] orders, TestClock clock, string? onScreen = null, ReturnRequest[]? returns = null,
        CustomerId[]? accounts = null)
    {
        var dispatcher = new Dispatcher(Handler(asker, orders, clock, returns, accounts));
        return (new MyOrdersTool(dispatcher, new ShopperScreen { OrderId = onScreen }, Options.Create(new ShopOptions())), dispatcher);
    }

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A number typed into a chat reads nothing here, because nothing here reads one.</summary>
    [Fact]
    public void It_is_a_shoppers_read_that_takes_the_language_and_nothing_that_names_an_order()
    {
        var (tool, _) = Tool(SignedIn(Ana), [], new TestClock(NewYear));

        Assert.Equal((ToolAudience.Shopper, "my_orders"), (tool.Audience, tool.Name));
        Assert.Equal(["culture"], tool.ArgumentsSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task An_order_is_sentences_under_a_reference_of_this_answer_with_its_days_as_the_shop_counts_them()
    {
        // Delivered at 23:30 UTC on 31 December: in Madrid it is already 1 January.
        var clock = new TestClock(new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero));
        var order = AnOrder(Ana, clock, OrderStatus.Delivered, Boots(), Socks(2));
        var (tool, _) = Tool(SignedIn(Ana), [order], clock);

        var answer = await tool.ReadAsync(Arguments("""{"culture":"es"}"""), Ct);

        var said = Assert.Single(((OrdersForShopper)answer.Data).Orders);
        Assert.Equal(("order1", false, "Pedido del 1 de enero de 2026"), (said.Reference, said.OnScreen, said.Title));
        Assert.Equal(
            [
                "El pedido se hizo el 1 de enero de 2026.",
                "Su estado es: entregado.",
                "Se entregó el 1 de enero de 2026.",
                "Incluye 1 × Botines de piel (42).",
                "Incluye 2 × Calcetines de lana.",
                FormattableString.Invariant($"Total: {order.Totals.Total.Amount:0.00} EUR."),
                "Se puede devolver 1 × Botines de piel (42) hasta el 15 de enero de 2026.",
                "Se puede devolver 2 × Calcetines de lana hasta el 15 de enero de 2026.",
            ],
            said.Says);

        // Each sentence is a fact of that order; the order is a subject whose id the model is never shown.
        Assert.All(answer.Facts!, fact => Assert.Equal(("order1", FactSource.Order), (fact.Subject, fact.Source)));
        Assert.Equal(said.Says, answer.Facts!.Select(fact => fact.Text));
        Assert.Equal(new Subject("order1", "Pedido del 1 de enero de 2026", SubjectKind.Order, order.Id.ToString()), Assert.Single(answer.Subjects!));
        Assert.DoesNotContain(order.Id.ToString(), JsonSerializer.Serialize(answer.Data));
        Assert.Equal((1, (int?)null), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task In_english_and_on_its_way_it_says_so_and_that_it_cannot_be_returned_yet()
    {
        var clock = new TestClock(NewYear);
        var (tool, _) = Tool(SignedIn(Ana), [AnOrder(Ana, clock, OrderStatus.Shipped)], clock);

        var said = Assert.Single(((OrdersForShopper)(await tool.ReadAsync(Arguments("""{"culture":"en"}"""), Ct)).Data).Orders);

        Assert.Equal("Order of 1 January 2026", said.Title);
        Assert.Contains("Its status is: shipped, on its way.", said.Says);
        Assert.Contains("It holds 1 × Botines de piel (42).", said.Says);
        Assert.Equal("It cannot be returned yet: the 14 days to return it start when it is delivered.", said.Says[^1]);
        Assert.DoesNotContain(said.Says, sentence => sentence.StartsWith("It was delivered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Past_the_window_it_says_the_day_the_time_ended_and_a_cancelled_order_says_nothing_of_returning()
    {
        var clock = new TestClock(NewYear);
        var delivered = AnOrder(Ana, clock, OrderStatus.Delivered);
        clock.Advance(TimeSpan.FromDays(1));
        var cancelled = AnOrder(Ana, clock, OrderStatus.Cancelled);
        clock.Advance(TimeSpan.FromDays(20));
        var (tool, _) = Tool(SignedIn(Ana), [delivered, cancelled], clock);

        var orders = ((OrdersForShopper)(await tool.ReadAsync(Arguments("""{"culture":"es"}"""), Ct)).Data).Orders;

        // Newest first: the cancelled one is order1 — and with more than one order,
        // which is the latest is said, not left to be worked out from two dates.
        Assert.Equal("Este es el pedido más reciente del comprador.", orders[0].Says[0]);
        Assert.DoesNotContain("Este es el pedido más reciente del comprador.", orders[1].Says);
        Assert.Contains("Su estado es: cancelado.", orders[0].Says);
        Assert.DoesNotContain(orders[0].Says, sentence => sentence.Contains("devolver", StringComparison.Ordinal));
        Assert.Equal("El plazo para devolverlo terminó el 15 de enero de 2026.", orders[1].Says[^1]);
    }

    /// <summary>
    /// An order's state is the shop's word for it, in the shopper's language — what Tino
    /// cites when it is asked where an order is. Every state has one, in both.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Pending, "pendiente de pago", "waiting for payment")]
    [InlineData(OrderStatus.PaymentAuthorized, "pago autorizado, confirmándose", "payment authorised, being confirmed")]
    [InlineData(OrderStatus.PaymentFailed, "el pago falló", "the payment failed")]
    [InlineData(OrderStatus.Confirmed, "confirmado, en preparación", "confirmed, being prepared")]
    [InlineData(OrderStatus.Shipped, "enviado, en camino", "shipped, on its way")]
    [InlineData(OrderStatus.Delivered, "entregado", "delivered")]
    [InlineData(OrderStatus.Cancelled, "cancelado", "cancelled")]
    public async Task Every_state_of_an_order_has_the_shops_word_for_it_in_both_languages(OrderStatus status, string es, string en)
    {
        var clock = new TestClock(NewYear);
        var (tool, _) = Tool(SignedIn(Ana), [AnOrder(Ana, clock, status)], clock);

        async Task<IReadOnlyList<string>> Said(string culture) =>
            Assert.Single(((OrdersForShopper)(await tool.ReadAsync(Arguments($$"""{"culture":"{{culture}}"}"""), Ct)).Data).Orders).Says;

        Assert.Contains($"Su estado es: {es}.", await Said("es"));
        Assert.Contains($"Its status is: {en}.", await Said("en"));
    }

    /// <summary>The same order, the same sentences, in English: none is left in the other language.</summary>
    [Fact]
    public async Task A_delivered_order_is_said_in_english_sentence_by_sentence()
    {
        var clock = new TestClock(new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero));
        var older = AnOrder(Ana, clock, OrderStatus.Delivered, Boots(), Socks(2));
        clock.Advance(TimeSpan.FromHours(1));
        var newer = AnOrder(Ana, clock, OrderStatus.Delivered, Boots());
        var back = ReturnRequest.Open(clock, newer, [new(newer.Lines[0].VariantId, "BOOTS-42", 1, ReturnReason.WrongSize, null)], earlier: []);
        var (tool, _) = Tool(SignedIn(Ana), [older, newer], clock, onScreen: older.Id.ToString(), returns: [back]);

        var orders = ((OrdersForShopper)(await tool.ReadAsync(Arguments("""{"culture":"en"}"""), Ct)).Data).Orders;

        // The one on the screen comes first.
        Assert.Equal(
            [
                "This is the order the shopper is looking at.",
                "The order was placed on 1 January 2026.",
                "Its status is: delivered.",
                "It was delivered on 1 January 2026.",
                "It holds 1 × Botines de piel (42).",
                "It holds 2 × Calcetines de lana.",
                FormattableString.Invariant($"Total: {older.Totals.Total.Amount:0.00} EUR."),
                "1 × Botines de piel (42) can be returned until 15 January 2026.",
                "2 × Calcetines de lana can be returned until 15 January 2026.",
            ],
            orders[0].Says);
        Assert.Equal("This is the shopper's most recent order.", orders[1].Says[0]);
        Assert.Equal("Nothing of it is left to return.", orders[1].Says[^1]);

        clock.Advance(TimeSpan.FromDays(30));
        var later = ((OrdersForShopper)(await tool.ReadAsync(Arguments("""{"culture":"en"}"""), Ct)).Data).Orders;
        Assert.Equal("The time to return it ended on 15 January 2026.", later[0].Says[^1]);
    }

    [Fact]
    public async Task What_is_already_on_its_way_back_is_not_offered_again()
    {
        var clock = new TestClock(NewYear);
        var order = AnOrder(Ana, clock, OrderStatus.Delivered, Boots());
        var all = ReturnRequest.Open(clock, order, [new(order.Lines[0].VariantId, "BOOTS-42", 1, ReturnReason.WrongSize, null)], earlier: []);
        var (tool, _) = Tool(SignedIn(Ana), [order], clock, returns: [all]);

        var said = Assert.Single(((OrdersForShopper)(await tool.ReadAsync(Arguments("{}"), Ct)).Data).Orders);

        Assert.Equal("No queda nada de él por devolver.", said.Says[^1]);
    }

    /// <summary>The id comes from the screen, which the turn filled from what the browser sent: the model passed nothing.</summary>
    [Fact]
    public async Task The_order_on_the_screen_is_asked_for_by_the_read_and_marked_in_its_answer()
    {
        var clock = new TestClock(NewYear);
        var guest = CustomerId.New();
        var order = AnOrder(guest, clock, OrderStatus.Shipped);
        var (tool, dispatcher) = Tool(CommercePrincipal.Anonymous, [order], clock, onScreen: order.Id.ToString(), accounts: []);

        var answer = await tool.ReadAsync(Arguments("""{"culture":"es"}"""), Ct);

        Assert.Equal(order.Id, dispatcher.Asked!.OnScreen);
        var said = Assert.Single(((OrdersForShopper)answer.Data).Orders);
        Assert.True(said.OnScreen);
        // Which order "this order" means is one of its sentences: the call that
        // writes the answer reads the sentences and nothing else.
        Assert.Equal("Este es el pedido que el comprador está viendo.", said.Says[0]);
        Assert.Null(((OrdersForShopper)answer.Data).Notice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("not-an-id")]
    public async Task What_is_not_an_orders_id_on_the_screen_is_no_order_on_the_screen(string? onScreen)
    {
        var clock = new TestClock(NewYear);
        var (tool, dispatcher) = Tool(SignedIn(Ana), [AnOrder(Ana, clock, OrderStatus.Shipped)], clock, onScreen);

        await tool.ReadAsync(Arguments("{}"), Ct);

        Assert.Null(dispatcher.Asked!.OnScreen);
    }

    [Theory]
    [InlineData("es", "Para preguntar por un pedido, inicia sesión o abre el pedido desde su correo de confirmación.")]
    [InlineData("en", "To ask about an order, sign in, or open the order from its confirmation email.")]
    public async Task Nobody_to_answer_about_is_said_as_a_notice_of_the_shops(string culture, string notice)
    {
        var (tool, _) = Tool(CommercePrincipal.Anonymous, [], new TestClock(NewYear));

        var answer = await tool.ReadAsync(Arguments($$"""{"culture":"{{culture}}"}"""), Ct);

        Assert.Equal(new OrdersForShopper([], notice), answer.Data, OrdersComparer.Instance);
        Assert.Equal([new Fact(Fact.TheShop, FactSource.Notice, notice)], answer.Facts);
        Assert.Empty(answer.Subjects!);
    }

    /// <summary>Signed in with no orders is the truth, not a notice: there is somebody, and they have bought nothing.</summary>
    [Fact]
    public async Task A_signed_in_shopper_with_no_orders_has_none_and_is_told_nothing_about_signing_in()
    {
        var (tool, _) = Tool(SignedIn(Ana), [], new TestClock(NewYear));

        var answer = await tool.ReadAsync(Arguments("{}"), Ct);

        Assert.Null(((OrdersForShopper)answer.Data).Notice);
        Assert.Empty(answer.Facts!);
        Assert.Equal(0, answer.Rows);
    }

    private sealed class OrdersComparer : IEqualityComparer<object>
    {
        public static readonly OrdersComparer Instance = new();

        public new bool Equals(object? x, object? y) =>
            x is OrdersForShopper a && y is OrdersForShopper b && a.Notice == b.Notice && a.Orders.SequenceEqual(b.Orders);

        public int GetHashCode(object value) => 0;
    }
}
