using System.Globalization;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>What a promotion does, as a closed set on the wire (ADR 0043).</summary>
public enum DealKind
{
    /// <summary>A percentage off each line it reaches.</summary>
    PercentOffLine,

    /// <summary>An amount off the whole order.</summary>
    AmountOffOrder,

    /// <summary>Take <c>Buy</c> plus <c>Free</c>, pay for <c>Buy</c>.</summary>
    BuyXGetY,

    /// <summary>Shipping costs nothing.</summary>
    FreeShipping,
}

/// <summary>
/// What a promotion takes off and what it needs, as data (ADR 0059: a front end
/// composes its own sentence from these; an agent reads <see cref="DealText"/>'s).
/// Each figure is set only for the kind it belongs to.
/// </summary>
/// <param name="ReachesSome">It names a category or an item: an order discount
/// then needs one of them in the order.</param>
public sealed record DealTerms(
    DealKind Kind,
    decimal? Percent = null,
    Money? AmountOff = null,
    int? Buy = null,
    int? Free = null,
    Money? MinimumSubtotal = null,
    int? MinimumQuantity = null,
    bool ReachesSome = false);

/// <summary>
/// A promotion a product is in, as data (ADR 0059): a card composes its own words
/// from <see cref="Terms"/>, an agent reads the sentence the server writes from
/// the same terms (<see cref="DealText"/>). The search carries it on every card it
/// draws, and Pricing answers it for one SKU, for the product's page (phase 13c).
/// </summary>
/// <param name="Name">The promotion's name in the reader's language: "5 € en menaje".</param>
public sealed record ProductDeal(string Code, string Name, DealTerms Terms);

/// <summary>
/// A promotion's terms as a sentence the server writes, es and en — what the
/// shop's campaigns say (<c>list_campaigns</c>) and what a product found in one
/// says (<c>search_products</c>), phase 13c. Read the way the till reads them
/// (<c>CombiningPromotionEngine</c>): a minimum is the whole order's, and an
/// order discount that names a category needs one of its items in the order.
/// Money as each language writes it (<see cref="MoneyText"/>).
/// </summary>
public static class DealText
{
    /// <summary>
    /// "un 10 % menos en cada artículo de la promoción", "5,00 € menos en el pedido
    /// si lleva algún artículo de la promoción y suma al menos 40,00 €".
    /// </summary>
    public static string OfThePromotion(DealTerms terms, string culture) => Terms(terms, culture, ofOneProduct: false);

    /// <summary>
    /// The same, said of one product the promotion reaches: "un 10 % menos en su
    /// precio", "5,00 € menos en el pedido si suma al menos 40,00 €" — the product
    /// is the item the order needs.
    /// </summary>
    public static string OfAProductInIt(DealTerms terms, string culture) => Terms(terms, culture, ofOneProduct: true);

    private static string Terms(DealTerms terms, string culture, bool ofOneProduct)
    {
        var english = culture == "en";
        var minimum = terms.MinimumSubtotal is { } money ? MoneyText.In(money, culture) : null;
        var needsOne = terms.ReachesSome && !ofOneProduct;

        return terms.Kind switch
        {
            DealKind.PercentOffLine => (english, ofOneProduct) switch
            {
                (true, true) => $"{Percent(terms.Percent, culture)} off its price",
                (true, false) => $"{Percent(terms.Percent, culture)} off each item in it",
                (false, true) => $"un {Percent(terms.Percent, culture)} menos en su precio",
                (false, false) => $"un {Percent(terms.Percent, culture)} menos en cada artículo de la promoción",
            } + Units(terms.MinimumQuantity, english) + OrderAtLeast(minimum, english),
            DealKind.BuyXGetY => (english
                ? $"take {terms.Buy + terms.Free} and pay for {terms.Buy}"
                : $"llévate {terms.Buy + terms.Free} y paga {terms.Buy}") + OrderAtLeast(minimum, english),
            DealKind.AmountOffOrder => (english
                ? $"{Amount(terms.AmountOff, culture)} off the order"
                : $"{Amount(terms.AmountOff, culture)} menos en el pedido") + Needs(needsOne, minimum, english, orderNamed: true),
            _ => (english ? "free shipping" : "envío gratis") + Needs(needsOne, minimum, english),
        };
    }

    /// <summary>"si lleva algún artículo de la promoción y suma al menos 40,00 €": both, either or nothing.</summary>
    private static string Needs(bool needsOne, string? minimum, bool english, bool orderNamed = false) => (needsOne, minimum) switch
    {
        (true, { } least) => english
            ? $" when it has an item in the promotion and comes to at least {least}"
            : $" si lleva algún artículo de la promoción y suma al menos {least}",
        (true, null) => english ? " when it has an item in the promotion" : " si lleva algún artículo de la promoción",
        // "menos en el pedido si suma": the order was just named.
        (false, { } least) when orderNamed => english ? $" when it comes to at least {least}" : $" si suma al menos {least}",
        (false, { } least) => english ? $" when the order comes to at least {least}" : $" si el pedido suma al menos {least}",
        _ => "",
    };

    /// <summary>A minimum on the order, for an effect on the lines: ", si el pedido suma al menos 40,00 €".</summary>
    private static string OrderAtLeast(string? minimum, bool english) => minimum is null
        ? ""
        : english ? $", when the order comes to at least {minimum}" : $", si el pedido suma al menos {minimum}";

    /// <summary>A minimum of units for a percentage; a deal says its own (3 for 2 needs 3).</summary>
    private static string Units(int? units, bool english) => units is { } count
        ? english ? $", from {count} of them" : $", a partir de {count} unidades"
        : "";

    private static string Amount(Money? amount, string culture) => amount is { } money ? MoneyText.In(money, culture) : "";

    /// <summary>"10 %" in Spanish, "10%" in English.</summary>
    private static string Percent(decimal? percent, string culture)
    {
        var number = (percent ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);
        return culture == "en" ? $"{number}%" : $"{number.Replace('.', ',')} %";
    }
}
