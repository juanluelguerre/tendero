using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>
/// Ordering's answer to <see cref="IOrderExport"/>: the shop's days turned into the
/// instants they span on its clock — a day the clocks change on is 23 or 25 hours
/// long, which only the zone knows (ADR 0046) — and each sale said as its invoice
/// says it. A sale is the dashboard's (<see cref="SalesRanking.IsSale"/>), so the
/// accountant and the dashboard count the same orders.
/// </summary>
public sealed class OrderExportReader(IShopHistory history, IOptions<ShopOptions> shop) : IOrderExport
{
    public async Task<IReadOnlyList<ExportedOrder>> SalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var zone = DateTimeZoneProviders.Tzdb[shop.Value.TimeZone];
        var start = zone.AtStartOfDay(LocalDate.FromDateOnly(from)).ToDateTimeOffset();
        var end = zone.AtStartOfDay(LocalDate.FromDateOnly(to.AddDays(1))).ToDateTimeOffset();

        return
        [
            .. (await history.OrdersPlacedAsync(start, end, cancellationToken))
                .Where(SalesRanking.IsSale)
                .OrderBy(order => order.CreatedAt)
                .Select(order => Export(order, zone)),
        ];
    }

    private static ExportedOrder Export(Order order, DateTimeZone zone) => new(
        order.Id.ToString(),
        Instant.FromDateTimeOffset(order.CreatedAt).InZone(zone).Date.ToDateOnly(),
        order.Status.ToString(),
        order.BillingAddress.RecipientName,
        order.BillingAddress.CountryCode,
        [
            .. order.Lines.Select(line => new ExportedOrderLine(
                line.Sku,
                line.VariantLabel is { Length: > 0 } variant ? $"{line.ProductName} ({variant})" : line.ProductName,
                line.Quantity,
                line.UnitPrice,
                line.Total)),
        ],
        [.. order.Discounts.Select(discount => new ExportedOrderDiscount(discount.PromotionCode, discount.Label, discount.Amount))],
        new ExportedOrderShipping(order.Shipping.Label, order.Shipping.Amount),
        [.. order.Taxes.Select(tax => new ExportedOrderTax(tax.TaxClass, tax.Rate, tax.Base, tax.Amount))],
        order.Totals.Total);
}
