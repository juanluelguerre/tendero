using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Builder explícito (docs/testing.md): cada valor por defecto declara que es
/// irrelevante para la conducta bajo prueba. Nada de datos anónimos.
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

    public static OrderBuilder Default() => new();

    public OrderBuilder WithLines(params OrderLine[] lines)
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

    public Order Build() => Order.Place(Clock, CustomerId.New(), _idempotencyKey, _lines, _culture);

    /// <summary>
    /// Un pedido en el estado pedido, alcanzado por transiciones legales. No hay
    /// atajo por reflexión a propósito: si un estado deja de ser alcanzable, este
    /// método deja de compilar o de pasar, que es justo lo que se quiere saber.
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
        new(ProductId.New(), VariantId.New(), $"TEST-{Guid.NewGuid():N}", "Línea de prueba",
            null, new Money(amount, currency), quantity);
}
