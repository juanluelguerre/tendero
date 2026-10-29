using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The shop's sales as they leave it for the accountant (phase 16, E-4): the shop's
/// days, counted on its clock (ADR 0046); the dashboard's idea of a sale; and an
/// order said in an invoice's terms, as values.
/// </summary>
public sealed class OrderExportReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IOptions<ShopOptions> Madrid = Options.Create(new ShopOptions { TimeZone = "Europe/Madrid" });

    private static Money Eur(decimal amount) => new(amount, "EUR");

    /// <summary>October in Madrid is from midnight at +02:00 to midnight at +01:00: the clocks went back on the 25th.</summary>
    [Fact]
    public async Task The_shops_days_are_asked_for_on_its_clock()
    {
        var history = new RecordingHistory([]);

        await new OrderExportReader(history, Madrid).SalesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);

        Assert.Equal(
            (new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1))),
            history.Asked);
    }

    [Fact]
    public async Task Only_a_sale_leaves_neither_a_rehearsal_nor_an_order_never_paid_for()
    {
        var sale = OrderBuilder.Default().InStatus(OrderStatus.Confirmed);
        var rehearsal = OrderBuilder.Default().AsTest().InStatus(OrderStatus.Confirmed);
        var cancelled = OrderBuilder.Default().InStatus(OrderStatus.Cancelled);

        var exported = await new OrderExportReader(new RecordingHistory([sale, rehearsal, cancelled]), Madrid)
            .SalesAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Ct);

        Assert.Equal([sale.Id.ToString()], exported.Select(order => order.OrderId));
    }

    /// <summary>The day is the shop's — half past ten at night in UTC is already the next day in Madrid — and the order is said as its invoice says it.</summary>
    [Fact]
    public async Task An_order_leaves_as_its_invoice_says_it()
    {
        var placed = new TestClock(new DateTimeOffset(2026, 10, 1, 22, 30, 0, TimeSpan.Zero));
        var order = OrderBuilder.Default()
            .At(placed)
            .ShippedTo(Address.Create("Lucía Martín", "Calle de Hortaleza 48", null, "Madrid", "M", "28004", "ES", "+34600111201"))
            .WithDiscounts(new OrderDiscount("WELCOME5", "Bienvenida", Eur(5m)))
            .WithTaxes(new OrderTax("standard", 0.21m, Eur(65.08m), Eur(13.67m)))
            .WithTotals(new OrderTotals(Eur(79.95m), Eur(5m), Eur(4.95m), Eur(13.67m), Eur(79.90m)))
            .InStatus(OrderStatus.Delivered);

        var exported = Assert.Single(await new OrderExportReader(new RecordingHistory([order]), Madrid)
            .SalesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct));

        Assert.Equal((new DateOnly(2026, 10, 2), "Delivered", "Lucía Martín", "ES", Eur(79.90m)),
            (exported.Day, exported.Status, exported.Customer, exported.Country, exported.Total));
        Assert.Equal([new ExportedOrderLine("B073WXYZ01-38", "Zapatillas de running (azul marino · 38)", 1, Eur(79.95m), Eur(79.95m))], exported.Lines);
        Assert.Equal([new ExportedOrderDiscount("WELCOME5", "Bienvenida", Eur(5m))], exported.Discounts);
        Assert.Equal(new ExportedOrderShipping("Envio estandar", Eur(4.95m)), exported.Shipping);
        Assert.Equal([new ExportedOrderTax("standard", 0.21m, Eur(65.08m), Eur(13.67m))], exported.Taxes);
    }

    /// <summary>
    /// Oldest first, whatever order the history answers in: the accountant reads a
    /// span as it happened. Found by the exports' Stryker close.
    /// </summary>
    [Fact]
    public async Task Sales_leave_oldest_first()
    {
        Order At(int day) => OrderBuilder.Default().At(new TestClock(new DateTimeOffset(2026, 10, day, 10, 0, 0, TimeSpan.Zero))).InStatus(OrderStatus.Confirmed);
        var (third, first, second) = (At(20), At(2), At(9));

        var exported = await new OrderExportReader(new RecordingHistory([third, first, second]), Madrid)
            .SalesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);

        Assert.Equal([2, 9, 20], exported.Select(order => order.Day.Day));
    }

    private sealed class RecordingHistory(IReadOnlyList<Order> orders) : IShopHistory
    {
        public (DateTimeOffset From, DateTimeOffset To)? Asked { get; private set; }

        public Task<IReadOnlyList<Order>> OrdersPlacedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Asked = (from, to);
            return Task.FromResult(orders);
        }

        public Task<IReadOnlyList<Order>> OrdersInAsync(IReadOnlyCollection<OrderStatus> statuses, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Cart>> CartsStartedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReturnRequest>> ReturnsOpenOrChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset?> FirstLiveOrderAtAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<CustomerId>> CustomersWhoOrderedBeforeAsync(IReadOnlyCollection<CustomerId> customers, DateTimeOffset before, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
