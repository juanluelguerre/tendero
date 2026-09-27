using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.TestOrders;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Test orders (ADR 0038, P11-0): a rehearsal goes all the way through and ends
/// before anything leaves the warehouse — decided by the credential, never by the
/// request, and refused at the one transition that would make it real.
/// </summary>
public sealed class TestOrderTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void The_mode_is_the_credentials_and_nothing_else()
    {
        Assert.Equal(OrderMode.Live, OrderModes.Of(CommercePrincipal.Anonymous));
        Assert.Equal(OrderMode.Live, OrderModes.Of(Agent(test: false)));
        Assert.Equal(OrderMode.Test, OrderModes.Of(Agent(test: true)));
    }

    [Fact]
    public void A_test_order_is_placed_as_one_under_the_id_its_payment_was_authorised_with()
    {
        var order = OrderBuilder.Default().AsTest().Build();

        Assert.Equal(OrderMode.Test, order.Mode);
        Assert.Equal(OrderMode.Live, OrderBuilder.Default().Build().Mode);

        var id = OrderId.New();
        var placed = Order.Place(
            Clock, CustomerId.New(), "k-1", OrderBuilder.Default().Build().Lines,
            Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES"),
            Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES"),
            new OrderShipping("standard", "Envío estándar", new Money(0m, "EUR"), 3),
            new OrderQuote("q", "0123456789abcdef0123456789abcdef", Clock.GetUtcNow()),
            new OrderTotals(new Money(79.95m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"), Money.Zero("EUR"), new Money(79.95m, "EUR")),
            id: id);
        Assert.Equal(id, placed.Id);
    }

    [Fact]
    public void A_test_order_is_never_shipped_and_a_live_one_does_not_end_by_itself()
    {
        var test = OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed);
        var live = OrderBuilder.Default().InStatus(OrderStatus.Confirmed);

        Assert.Contains("never shipped", Assert.Throws<InvalidOperationException>(() => test.Ship(Clock)).Message, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Confirmed, test.Status);
        Assert.Throws<InvalidOperationException>(() => live.EndTest(Clock));
        Assert.Equal(OrderStatus.Confirmed, live.Status);
    }

    [Fact]
    public async Task A_confirmed_test_order_ends_cancelled_so_its_stock_and_hold_are_given_back()
    {
        var order = OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed);
        order.ClearDomainEvents();
        var unitOfWork = new CountingUnitOfWork();
        using var metrics = new InstrumentRecorder("tendero.orders.test_ended");

        await new EndTestOrderWhenConfirmed(new OneOrder(order), unitOfWork, Clock)
            .HandleAsync(new OrderConfirmed(order.Id, Clock.GetUtcNow()), TestContext.Current.CancellationToken);

        Assert.Equal((OrderStatus.Cancelled, OrderStop.TestOrderEnded), (order.Status, order.Stop?.Code));
        // The cancellation is what the stock saga's compensation hangs from.
        Assert.Contains(order.DomainEvents, e => e is OrderCancelled);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Single(metrics.Of("tendero.orders.test_ended"));
    }

    [Fact]
    public async Task A_live_order_and_a_redelivery_are_left_alone()
    {
        var live = OrderBuilder.Default().InStatus(OrderStatus.Confirmed);
        var ended = OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed);
        ended.EndTest(Clock);
        var unitOfWork = new CountingUnitOfWork();

        foreach (var order in new[] { live, ended })
            await new EndTestOrderWhenConfirmed(new OneOrder(order), unitOfWork, Clock)
                .HandleAsync(new OrderConfirmed(order.Id, Clock.GetUtcNow()), TestContext.Current.CancellationToken);

        Assert.Equal((OrderStatus.Confirmed, OrderStatus.Cancelled), (live.Status, ended.Status));
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public void A_test_order_pays_through_the_test_provider_whatever_the_shop_takes_money_with()
    {
        var options = new CheckoutOptions { PaymentProvider = "stripe", TestPaymentProvider = "fake" };

        Assert.Equal(("stripe", "fake"), (options.ProviderFor(OrderMode.Live), options.ProviderFor(OrderMode.Test)));
    }

    private static CommercePrincipal Agent(bool test) =>
        new(null, new AgentId("claude-desktop"), "claude-desktop", "Claude Desktop", ["agent"]) { IsTest = test };
}
