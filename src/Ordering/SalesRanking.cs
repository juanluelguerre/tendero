using ElGuerre.Tendero.Ordering.Domain;

namespace ElGuerre.Tendero.Ordering;

/// <summary>
/// What sold, ranked — one rule for every screen that asks.
///
/// The shopkeeper's dashboard ranked its best sellers inline (P11-7), and the
/// shop's home asks the same question in public (R-8: *Lo más vendido*,
/// *Tendencias*). Two copies of "what counts as a sale and how a tie is broken"
/// would agree until one of them was edited, and the home would then show a
/// best seller the dashboard does not. So the rule lives here, beside the other
/// rules more than one slice reads (<see cref="OrderAccess"/>), and each screen
/// chooses only what it groups by: the dashboard by SKU — a variant is what a
/// shopkeeper restocks — and the home by product, which is what a card shows.
/// </summary>
public static class SalesRanking
{
    /// <summary>
    /// A sale: a live order whose money was authorised and that was not cancelled —
    /// read off the state machine (invariant 5), which is where "paid" is decided.
    /// A rehearsal (ADR 0038) is never a sale, so it never ranks.
    /// </summary>
    public static bool IsSale(Order order) =>
        order.Mode == OrderMode.Live
        && order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered;

    /// <summary>
    /// What sold in <paramref name="orders"/>, grouped by <paramref name="key"/>,
    /// most units first; a tie goes to the one that brought more money, and then
    /// to the key, so the same orders always rank the same way. Orders that are
    /// not sales are left out here, whoever passed them; <paramref name="lines"/>
    /// narrows the lines that count (a category, for Steward's ranking).
    /// </summary>
    public static IReadOnlyList<Sold> Rank(IEnumerable<Order> orders, Func<OrderLine, string> key, Func<OrderLine, bool>? lines = null) =>
    [
        .. orders.Where(IsSale)
            .SelectMany(order => order.Lines)
            .Where(lines ?? (_ => true))
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Sold(group.Key, group.First().ProductName, group.Sum(line => line.Quantity), group.Sum(line => line.Total.Amount)))
            .OrderByDescending(sold => sold.Units)
            .ThenByDescending(sold => sold.Revenue)
            .ThenBy(sold => sold.Key, StringComparer.Ordinal)
    ];

    /// <summary>A product, as the home ranks it: the product a line was for, not the variant.</summary>
    public static string ByProduct(OrderLine line) => line.ProductId.Value.ToString();

    /// <summary>A variant, as the dashboard ranks it: the SKU a shopkeeper restocks.</summary>
    public static string BySku(OrderLine line) => line.Sku;
}

/// <summary>
/// One thing that sold, with what it sold. The figures stay inside Ordering's
/// own screens: the public rows answer a rank and never these numbers.
/// </summary>
/// <param name="Name">The name as the first line that sold it snapshotted it.</param>
public sealed record Sold(string Key, string Name, int Units, decimal Revenue);
