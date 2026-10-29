using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;

/// <summary>
/// One row of the accountant's export (E-4): a line an order sold, a discount it
/// gave, the shipping it charged, or the tax it collected at one rate — each beside
/// its order's number, day, status and billing party, so a spreadsheet filters and
/// sums without a second sheet. Amounts include tax, as the shop's prices do
/// (ADR 0028); a discount's is negative, so the column sums to what the orders took.
/// A tax row carries no amount, only its rate, its base and the tax.
/// </summary>
/// <param name="Day">The shop's day the order was placed on (ADR 0046).</param>
/// <param name="Status">The order's status, by its code.</param>
/// <param name="Customer">Who it was billed to, as the order froze it.</param>
/// <param name="Description">The product and its variant, the discount's code and label, the shipping's label, or the tax's class.</param>
/// <param name="TaxRate">A fraction: 0.21 is 21 %.</param>
public sealed record OrderLineForTheBooks(
    string OrderId,
    DateOnly Day,
    string Status,
    string Customer,
    string Country,
    BookLineKind Kind,
    string? Sku,
    string Description,
    int? Quantity,
    Money? UnitPrice,
    Money? Amount,
    decimal? TaxRate,
    Money? TaxBase,
    Money? Tax);

/// <summary>What a row of the accountant's export is.</summary>
public enum BookLineKind
{
    Line,
    Discount,
    Shipping,
    Tax,
}
