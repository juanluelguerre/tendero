using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ListOffers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;
using static ElGuerre.Tendero.Pricing.Tests.Build;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// An offer's headline figure, **each kind under its own name** (phase 13b). It
/// was one number called `amount` that meant a percentage, an amount of money or
/// a count according to `kind`, so the money among them reached a reader as a
/// bare `10` with no currency — and a reader that did not look at `kind` first
/// read a 20 % as twenty euros.
/// </summary>
public sealed class ListOffersHandlerTests
{
    [Fact]
    public async Task Each_kind_of_offer_says_its_own_figure_and_no_other()
    {
        var handler = new ListOffersHandler(
            new GivenPromotions(
                Promotion("A-PERCENT", new PercentOffLine(20m), priority: 1),
                Promotion("B-AMOUNT", new AmountOffOrder(new Money(10m, "EUR")), priority: 2),
                Promotion("C-BUY", new BuyXGetY(3, 1), priority: 3),
                Promotion("D-SHIPPING", new FreeShipping(), priority: 4)),
            new TestClock());

        var offers = (await handler.HandleAsync(new ListOffersQuery("es"), TestContext.Current.CancellationToken)).Items;

        Assert.Equal(
            [
                ("percent-off-line", 20m, null, null, null),
                ("amount-off-order", null, new Money(10m, "EUR"), null, null),
                ("buy-x-get-y", null, null, 3, 1),
                ("free-shipping", null, null, null, null),
            ],
            offers.Select(offer => (offer.Kind, offer.Percent, offer.AmountOff, offer.Buy, offer.Free)));
    }
}
