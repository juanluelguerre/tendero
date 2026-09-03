using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Tests;

/// <summary>
/// An explicit builder (docs/testing.md): every default value declares that it
/// is irrelevant to the behaviour under test. No anonymous data.
///
/// It is linked into three test projects rather than living in one, for the
/// reason `TestClock` already is: `Order.Place` takes everything checkout froze
/// — an address, a shipping option, a quote and its totals — and three suites
/// need a plausible order without any of them being what the test is about.
/// Copying the defaults into each would mean three orders that drift apart.
/// </summary>
internal sealed class OrderBuilder
{
    private static readonly TestClock Clock = new();

    private List<OrderLine> _lines =
    [
        new(ProductId.New(), VariantId.New(), "B073WXYZ01-38", "Zapatillas de running",
            "azul marino · 38", new Money(79.95m, "EUR"), 1)
    ];

    private string _culture = "es";
    private string _idempotencyKey = "checkout-1";

    /// <summary>
    /// Somewhere to send it. Every default here declares that it is irrelevant
    /// to the behaviour under test — an order needs an address the way it needs
    /// a currency, and no test in this file is about Madrid.
    /// </summary>
    private Address _shipTo = Address.Create(
        "Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private OrderShipping _shipping = new(
        "standard", "Envio estandar", new Money(4.95m, "EUR"), 3);

    private OrderTotals? _totals;
    private IReadOnlyList<OrderDiscount> _discounts = [];
    private IReadOnlyList<OrderTax> _taxes = [];

    public static OrderBuilder Default() => new();

    public OrderBuilder WithLines(params OrderLine[] lines) => WithLines((IEnumerable<OrderLine>)lines);

    public OrderBuilder WithLines(IEnumerable<OrderLine> lines)
    {
        _lines = [.. lines];
        return this;
    }

    public OrderBuilder InCulture(string culture)
    {
        _culture = culture;
        return this;
    }

    public OrderBuilder WithIdempotencyKey(string key)
    {
        _idempotencyKey = key;
        return this;
    }

    public OrderBuilder ShippedTo(Address address)
    {
        _shipTo = address;
        return this;
    }

    public OrderBuilder WithShipping(OrderShipping shipping)
    {
        _shipping = shipping;
        return this;
    }

    /// <summary>
    /// The frozen totals. Left unset they are DERIVED from the lines plus
    /// shipping, which is what a quote would have produced for a cart with no
    /// promotions and no tax — a plausible order rather than a zero one, so a
    /// test that never mentions money still gets numbers that add up.
    /// </summary>
    public OrderBuilder WithTotals(OrderTotals totals)
    {
        _totals = totals;
        return this;
    }

    public OrderBuilder WithDiscounts(params OrderDiscount[] discounts)
    {
        _discounts = [.. discounts];
        return this;
    }

    public OrderBuilder WithTaxes(params OrderTax[] taxes)
    {
        _taxes = [.. taxes];
        return this;
    }

    public Order Build()
    {
        // An order with no lines is a rejection the aggregate owns, so the
        // builder has to be able to ASK for one. Falling back to the currency of
        // the shipping keeps this method from throwing first and hiding it.
        var currency = _lines.Count > 0 ? _lines[0].UnitPrice.Currency : _shipping.Amount.Currency;
        // Only sum when the lines agree. Mixing currencies is a rejection the
        // AGGREGATE owns, and a builder that added them up first would throw
        // "Currency mismatch" from Money and hide the sentence under test.
        var mixed = _lines.Any(line =>
            !string.Equals(line.UnitPrice.Currency, currency, StringComparison.OrdinalIgnoreCase));

        var subtotal = mixed
            ? Money.Zero(currency)
            : _lines.Aggregate(Money.Zero(currency), (sum, line) => sum + line.Total);

        var totals = _totals ?? new OrderTotals(
            subtotal,
            Money.Zero(currency),
            _shipping.Amount,
            Money.Zero(currency),
            subtotal + _shipping.Amount);

        return Order.Place(
            Clock,
            CustomerId.New(),
            _idempotencyKey,
            _lines,
            _shipTo,
            _shipTo,
            _shipping,
            new OrderQuote("quote-1", "0123456789abcdef0123456789abcdef", Clock.GetUtcNow()),
            totals,
            _discounts,
            _taxes,
            _culture);
    }

    /// <summary>
    /// An order in the requested status, reached through legal transitions. There
    /// is no reflection shortcut on purpose: if a status stops being reachable,
    /// this method stops compiling or stops passing, which is exactly what one
    /// wants to know.
    /// </summary>
    public Order InStatus(OrderStatus status)
    {
        var order = Build();

        switch (status)
        {
            case OrderStatus.Pending:
                break;
            case OrderStatus.PaymentAuthorized:
                order.AuthorizePayment(Clock);
                break;
            case OrderStatus.PaymentFailed:
                order.FailPayment(Clock, "card declined");
                break;
            case OrderStatus.Confirmed:
                order.AuthorizePayment(Clock);
                order.Confirm(Clock);
                break;
            case OrderStatus.Shipped:
                order.AuthorizePayment(Clock);
                order.Confirm(Clock);
                order.Ship(Clock);
                break;
            case OrderStatus.Delivered:
                order.AuthorizePayment(Clock);
                order.Confirm(Clock);
                order.Ship(Clock);
                order.Deliver(Clock);
                break;
            case OrderStatus.Cancelled:
                order.Cancel(Clock, "customer changed their mind");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status.");
        }

        return order;
    }

    public static OrderLine Line(decimal amount, string currency, int quantity = 1) =>
        new(ProductId.New(), VariantId.New(), $"TEST-{Guid.NewGuid():N}", "A test line",
            null, new Money(amount, currency), quantity);
}
