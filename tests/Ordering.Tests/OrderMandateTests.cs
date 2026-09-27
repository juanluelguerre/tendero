using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The mandate an order was placed under (P11-5): frozen as values, and held by the
/// aggregate itself — no path places an order for other money than it approved.
/// </summary>
public sealed class OrderMandateTests
{
    private static readonly TestClock Clock = new();
    private static readonly Address Madrid = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private static Order Place(OrderMandate? mandate) => Order.Place(
        Clock, CustomerId.New(), "k-1", OrderBuilder.Default().Build().Lines, Madrid, Madrid,
        new OrderShipping("standard", "Envío estándar", new Money(0m, "EUR"), 3),
        new OrderQuote("q", "0123456789abcdef0123456789abcdef", Clock.GetUtcNow()),
        new OrderTotals(new Money(79.95m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"), Money.Zero("EUR"), new Money(79.95m, "EUR")),
        mandate: mandate);

    private static OrderMandate Approving(decimal total, string currency = "EUR") => OrderMandate.From(
        new MandateScope("mandate.checkout.1", "dev-platform-claude", "hash-1", new Money(total, currency), Clock.GetUtcNow().AddMinutes(15)));

    [Fact]
    public void An_order_keeps_the_mandate_it_was_placed_under_and_a_persons_has_none()
    {
        var order = Place(Approving(79.95m));

        Assert.Equal(("mandate.checkout.1", "dev-platform-claude", "hash-1", 79.95m, "EUR"),
            (order.Mandate!.Kind, order.Mandate.Issuer, order.Mandate.Reference, order.Mandate.ApprovedTotal, order.Mandate.Currency));
        Assert.Null(Place(null).Mandate);
    }

    [Theory]
    [InlineData("80.00", "EUR")]
    [InlineData("79.94", "EUR")]
    [InlineData("79.95", "USD")]
    public void No_order_is_placed_for_other_money_than_its_mandate_approved(string approved, string currency) =>
        Assert.Contains("mandate approves", Assert.Throws<InvalidOperationException>(
            () => Place(Approving(decimal.Parse(approved, System.Globalization.CultureInfo.InvariantCulture), currency))).Message, StringComparison.Ordinal);
}
