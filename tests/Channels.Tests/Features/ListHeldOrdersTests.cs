using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.ListHeldOrders;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Features;

/// <summary>
/// The held orders as the orders screen shows them (P16-29): oldest first, each with why
/// as a code, what it concerns, and the shop's sentence in the language asked.
/// </summary>
public sealed class ListHeldOrdersTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InMemoryChannelState TwoHeld()
    {
        var state = new InMemoryChannelState();
        state.Add(HeldChannelOrder.Hold("shopify", "7002", "#1014", HeldOrderReason.NoAddress, null, null, At.AddHours(1)));
        state.Add(HeldChannelOrder.Hold("shopify", "7001", "#1013", HeldOrderReason.NotInCatalogue, "B13BAG0711-OLIVE",
            "https://tendero-dev.myshopify.com/admin/orders/7001", At));
        return state;
    }

    [Fact]
    public async Task Held_orders_are_listed_oldest_first_with_why_as_a_code()
    {
        var view = await new ListHeldOrdersHandler(TwoHeld()).HandleAsync(new ListHeldOrdersQuery("en"), Ct);

        Assert.Equal(["#1013", "#1014"], view.Orders.Select(order => order.Number));
        var first = view.Orders[0];
        Assert.Equal(
            ("shopify", "7001", HeldOrderReason.NotInCatalogue, "B13BAG0711-OLIVE", "https://tendero-dev.myshopify.com/admin/orders/7001", At, At),
            (first.Channel, first.ExternalId, first.Reason.Code, first.Reason.About, first.Page, first.HeldSince, first.LastTriedAt));
    }

    /// <summary>The sentence is for a reader with no front end, in the language asked (invariant 6).</summary>
    [Theory]
    [InlineData("es", "El pedido #1013 vende B13BAG0711-OLIVE, que el catálogo no tiene.", "El pedido #1014 no tiene adónde enviarse.")]
    [InlineData("en", "Order #1013 sells B13BAG0711-OLIVE, which the catalogue does not.", "Order #1014 has nowhere to be sent.")]
    public async Task Each_reason_is_said_in_the_language_asked(string culture, string notInCatalogue, string noAddress)
    {
        var view = await new ListHeldOrdersHandler(TwoHeld()).HandleAsync(new ListHeldOrdersQuery(culture), Ct);

        Assert.Equal([notInCatalogue, noAddress], view.Orders.Select(order => order.Reason.Text));
    }

    [Theory]
    [InlineData("es", "Los importes del pedido #1015 no cuadran con los de la tienda.")]
    [InlineData("en", "Order #1015's figures do not add up by the shop's arithmetic.")]
    public async Task Figures_that_do_not_add_up_are_said_in_the_language_asked(string culture, string said)
    {
        var state = new InMemoryChannelState();
        state.Add(HeldChannelOrder.Hold("shopify", "7003", "#1015", HeldOrderReason.TotalsDoNotAddUp, null, null, At));

        var view = await new ListHeldOrdersHandler(state).HandleAsync(new ListHeldOrdersQuery(culture), Ct);

        Assert.Equal(said, Assert.Single(view.Orders).Reason.Text);
    }

    [Fact]
    public async Task None_held_is_an_empty_list() =>
        Assert.Empty((await new ListHeldOrdersHandler(new InMemoryChannelState()).HandleAsync(new ListHeldOrdersQuery("es"), Ct)).Orders);
}
