using ElGuerre.Tendero.Ordering.Domain;

namespace ElGuerre.Tendero.Ordering.Contracts;

/// <summary>
/// What an order and a return look like on the wire.
///
/// They live here and not in a slice because **three slices return them** —
/// checkout, the order page and the backoffice list — and a slice that reached
/// into another slice to borrow a record would break the isolation rule that
/// makes a slice deletable. That rule is an executable test, and it is what sent
/// these types here rather than a review comment.
///
/// It is the same shape `Search/Contracts.cs` already has: a namespace beside
/// `Features/`, holding the records the slices agree on. Behaviour still goes
/// down to the SharedKernel or out to a port; this is neither, it is the
/// vocabulary of the context's own API.
///
/// Every id is a flat string. `OrderId` is a record struct and serialises as
/// <c>{"value":"…"}</c> — the mistake CLAUDE.md warns no unit test will catch.
/// </summary>
public sealed record OrderLineView(
    string Sku, string ProductName, string? VariantLabel, decimal UnitPrice, int Quantity);

public sealed record OrderDiscountView(string PromotionCode, string Label, decimal Amount);

public sealed record OrderTaxView(string TaxClass, decimal Rate, decimal Base, decimal Amount);

public sealed record OrderView(
    string OrderId,
    string Status,
    string Culture,
    string Currency,
    DateTimeOffset CreatedAt,
    string ShippingAddress,
    string ShippingLabel,
    IReadOnlyList<OrderLineView> Lines,
    IReadOnlyList<OrderDiscountView> Discounts,
    IReadOnlyList<OrderTaxView> Taxes,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal Shipping,
    decimal TaxTotal,
    decimal Total)
{
    public static OrderView From(Order order) => new(
        order.Id.ToString(),
        order.Status.ToString(),
        order.Culture,
        order.Currency,
        order.CreatedAt,
        order.ShippingAddress.SingleLine(),
        order.Shipping.Label,
        [
            .. order.Lines.Select(line => new OrderLineView(
                line.Sku, line.ProductName, line.VariantLabel, line.UnitPrice.Amount, line.Quantity))
        ],
        [
            .. order.Discounts.Select(discount => new OrderDiscountView(
                discount.PromotionCode, discount.Label, discount.Amount.Amount))
        ],
        [
            .. order.Taxes.Select(tax => new OrderTaxView(
                tax.TaxClass, tax.Rate, tax.Base.Amount, tax.Amount.Amount))
        ],
        order.Totals.Subtotal.Amount,
        order.Totals.DiscountTotal.Amount,
        order.Totals.Shipping.Amount,
        order.Totals.TaxTotal.Amount,
        order.Totals.Total.Amount);
}

public sealed record ReturnLineView(string Sku, int Quantity, string Reason, string? Comment);

public sealed record ReturnView(
    string ReturnId,
    string OrderId,
    string Status,
    string? Resolution,
    decimal? RefundAmount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReturnLineView> Lines)
{
    public static ReturnView From(ReturnRequest request) => new(
        request.Id.ToString(),
        request.OrderId.ToString(),
        request.Status.ToString(),
        request.Resolution,
        request.RefundAmount?.Amount,
        request.CreatedAt,
        [
            .. request.Lines.Select(line => new ReturnLineView(
                line.Sku, line.Quantity, line.Reason.ToString(), line.Comment))
        ]);
}
