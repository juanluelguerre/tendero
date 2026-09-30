using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Every figure is in its orders' currency, and a period with no orders in the
/// shop's default. The laboratory sells in euros only, so a test in another
/// currency is the one that can tell the two apart (R-9's close: the four copies
/// this replaced each survived a mutant that always answered euros).
/// </summary>
public sealed class DashboardCurrencyTests
{
    [Fact]
    public void A_figure_is_in_its_orders_currency()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(20m, "USD"))
            .WithShipping(new OrderShipping("standard", "Standard", new Money(0m, "USD"), 3))
            .At(new TestClock())
            .InStatus(OrderStatus.Confirmed);

        Assert.Equal("USD", DashboardCurrency.Of([order]));
    }

    [Fact]
    public void A_period_with_no_orders_is_in_the_default()
    {
        Assert.Equal("EUR", DashboardCurrency.Of([]));
    }
}
