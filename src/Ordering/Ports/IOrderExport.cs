using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// The shop's sales as they leave it for the accountant (phase 16, E-4; spec
/// <c>docs/specs/exports.md</c>): as values, never an <c>Order</c>, for <c>Exports</c>,
/// an adapter project that may not reach a domain. What an invoice keeps and nothing
/// else — the billing party, the lines, the discounts, the shipping, the tax by rate
/// — and never the buyer's email or phone: an erased customer's orders are kept for
/// the six years the Commercial Code asks, blocked, for exactly this use (P15-9).
/// </summary>
public interface IOrderExport
{
    /// <summary>
    /// Every sale placed on the shop's days <paramref name="from"/> to <paramref name="to"/>,
    /// both included, counted in the shop's time zone (ADR 0046), oldest first. A sale
    /// is what the dashboard calls one: live, and paid for or past it.
    /// </summary>
    Task<IReadOnlyList<ExportedOrder>> SalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <param name="Day">The shop's day it was placed on.</param>
/// <param name="Customer">Who it was billed to, as the order froze it.</param>
/// <param name="Country">The billing country, ISO 3166-1 alpha-2: what decides the tax.</param>
public sealed record ExportedOrder(
    string OrderId,
    DateOnly Day,
    string Status,
    string Customer,
    string Country,
    IReadOnlyList<ExportedOrderLine> Lines,
    IReadOnlyList<ExportedOrderDiscount> Discounts,
    ExportedOrderShipping Shipping,
    IReadOnlyList<ExportedOrderTax> Taxes,
    Money Total);

/// <param name="Name">The product and its variant, as the buyer saw them then.</param>
public sealed record ExportedOrderLine(string Sku, string Name, int Quantity, Money UnitPrice, Money Total);

public sealed record ExportedOrderDiscount(string Code, string Label, Money Amount);

public sealed record ExportedOrderShipping(string Label, Money Amount);

/// <param name="Base">What the rate was taken on, tax excluded.</param>
public sealed record ExportedOrderTax(string TaxClass, decimal Rate, Money Base, Money Amount);
