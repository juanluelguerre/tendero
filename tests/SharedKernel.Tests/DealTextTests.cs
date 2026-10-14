using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// A promotion's terms said of one product it reaches (phase 13c): the sentence the
/// shopper's assistant cites beside a card. The product is the item the order
/// needs, so an order discount says only its minimum; a percentage is off its own
/// price. The campaign's wording, said of the promotion, is held by
/// <c>ListCampaignsToolTests</c>.
/// </summary>
public sealed class DealTextTests
{
    private static readonly DealTerms KitchenFive = new(
        DealKind.AmountOffOrder, AmountOff: new Money(5, "EUR"), MinimumSubtotal: new Money(40, "EUR"), ReachesSome: true);

    [Theory]
    [InlineData("es", "5,00 € menos en el pedido si suma al menos 40,00 €")]
    [InlineData("en", "€5.00 off the order when it comes to at least €40.00")]
    public void An_order_discount_said_of_a_product_in_it_needs_only_its_minimum(string culture, string said)
    {
        Assert.Equal(said, DealText.OfAProductInIt(KitchenFive, culture));
    }

    [Fact]
    public void Said_of_the_promotion_it_needs_one_of_its_items_too()
    {
        Assert.Equal(
            "5,00 € menos en el pedido si lleva algún artículo de la promoción y suma al menos 40,00 €",
            DealText.OfThePromotion(KitchenFive, "es"));
    }

    [Theory]
    [InlineData("es", "un 10 % menos en su precio")]
    [InlineData("en", "10% off its price")]
    public void A_percentage_said_of_a_product_is_off_its_own_price(string culture, string said)
    {
        Assert.Equal(said, DealText.OfAProductInIt(new DealTerms(DealKind.PercentOffLine, Percent: 10, ReachesSome: true), culture));
    }

    [Fact]
    public void A_deal_says_its_own_units_and_a_half_percent_keeps_its_decimal()
    {
        Assert.Equal("llévate 3 y paga 2", DealText.OfAProductInIt(new DealTerms(DealKind.BuyXGetY, Buy: 2, Free: 1), "es"));
        Assert.Equal("un 12,5 % menos en su precio", DealText.OfAProductInIt(new DealTerms(DealKind.PercentOffLine, Percent: 12.5m), "es"));
    }
}
