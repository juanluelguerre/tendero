using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering;

/// <summary>
/// What an order another system holds becomes here, however it arrives: imported as
/// history (ADR 0066) or received from a sales channel to be shipped (ADR 0070). Below
/// both slices, which may not reference each other, so the two are read one way.
/// </summary>
internal static class SourceOrders
{
    /// <summary>
    /// The order's lines by the catalogue's ids and category, with the names as the buyer
    /// saw them then; null, and the line the catalogue does not sell, when one is missing.
    /// </summary>
    public static async Task<(List<OrderLine>? Lines, SourceOrderLine? Missing)> LinesAsync(
        SourceOrder placed, ICatalogueVariants catalogue, CancellationToken cancellationToken)
    {
        var skus = placed.Lines.Select(line => line.Sku).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = await catalogue.FindBySkusAsync(skus, cancellationToken);

        var missing = placed.Lines.FirstOrDefault(line => line.Sku is null || !known.ContainsKey(line.Sku));
        if (missing is not null)
            return (null, missing);

        return (
        [
            .. placed.Lines.Select(line =>
            {
                var variant = known[line.Sku!];
                return new OrderLine(variant.ProductId, variant.VariantId, variant.Sku, line.ProductName, line.VariantLabel, variant.ImageId,
                    line.UnitPrice, line.Quantity, variant.CategoryCode, variant.CategoryPath);
            }),
        ], null);
    }

    /// <summary>
    /// A tax line as the source named it. Prices include the tax (ADR 0028), so the
    /// base the rate was taken on is the amount divided by the rate.
    /// </summary>
    public static OrderTax Tax(SourceTax tax) =>
        new(tax.Title, tax.Rate,
            tax.Rate > 0 ? new Money(decimal.Round(tax.Amount.Amount / tax.Rate, 2), tax.Amount.Currency) : Money.Zero(tax.Amount.Currency),
            tax.Amount);
}
