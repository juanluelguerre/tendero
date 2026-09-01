using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

public enum OrderStatus
{
    Pending,            // creado, esperando autorización de pago
    PaymentAuthorized,  // el proveedor de pago autorizó el cargo
    PaymentFailed,      // rechazado; se puede reintentar o cancelar
    Confirmed,          // pago capturado y stock comprometido
    Shipped,
    Delivered,
    Cancelled
}

// Snapshot: el pedido guarda nombre y precio del momento de compra,
// nunca una FK "viva" al producto (que puede cambiar o archivarse).
public sealed record OrderLine(ProductId ProductId, string ProductName, Money UnitPrice, int Quantity)
{
    public Money Total => UnitPrice * Quantity;
}

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentAuthorized(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentFailed(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderConfirmed(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderCancelled(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderShipped(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// La entrega también es un hecho. Era la única transición que no emitía nada, y
/// resulta ser justo la que abre la ventana de devolución: el bucle de motivos de
/// devolución de la fase 4 no tiene otro sitio del que colgarse.
/// </summary>
public sealed record OrderDelivered(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : AggregateRoot
{
    // Máquina de estados declarativa: una transición fuera de esta tabla es un bug.
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending]           = [OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentAuthorized] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed]     = [OrderStatus.Pending, OrderStatus.Cancelled],
        [OrderStatus.Confirmed]         = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped]           = [OrderStatus.Delivered],
        [OrderStatus.Delivered]         = [],
        [OrderStatus.Cancelled]         = []
    };

    private readonly List<OrderLine> _lines = [];

    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }

    // El storefront (o el agente vía UCP) manda esta clave: reintentar el
    // checkout con la misma clave NO crea un segundo pedido.
    public string IdempotencyKey { get; private set; } = default!;

    // Idioma del comprador al comprar. Las líneas guardan el nombre YA resuelto
    // en esta cultura: el histórico del pedido no cambia si el catálogo se retraduce.
    public string Culture { get; private set; } = "es";

    // La divisa es del pedido, no de cada línea: se fija al comprar y no cambia.
    // Tenerla aquí es lo que permite que Total exista aunque no queden líneas.
    public string Currency { get; private set; } = default!;

    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public Money Total => _lines.Aggregate(
        Money.Zero(Currency),
        (sum, line) => sum + line.Total);

    private Order() { } // EF Core

    public static Order Place(
        CustomerId customerId, string idempotencyKey, IReadOnlyList<OrderLine> lines, string culture = "es")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (lines.Count == 0)
            throw new InvalidOperationException("An order requires at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOperationException("Line quantity must be positive.");

        // Un pedido tiene UNA divisa. Detectarlo aquí y no al sumar convierte un
        // error de datos en un rechazo con nombre, en el único sitio que puede
        // decidirlo. Multi-divisa está aplazado a propósito (initial-plan §7).
        var currency = lines[0].UnitPrice.Currency;
        if (lines.Any(l => !string.Equals(l.UnitPrice.Currency, currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An order cannot mix currencies.");

        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = OrderId.New(),
            CustomerId = customerId,
            IdempotencyKey = idempotencyKey,
            Culture = SharedKernel.Culture.Normalize(culture),
            Currency = currency,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        order._lines.AddRange(lines);
        order.Raise(new OrderPlaced(order.Id, now));
        return order;
    }

    public void AuthorizePayment() =>
        TransitionTo(OrderStatus.PaymentAuthorized, now => new OrderPaymentAuthorized(Id, now));

    public void FailPayment(string reason) =>
        TransitionTo(OrderStatus.PaymentFailed, now => new OrderPaymentFailed(Id, reason, now));

    public void Confirm() =>
        TransitionTo(OrderStatus.Confirmed, now => new OrderConfirmed(Id, now));

    public void Ship() =>
        TransitionTo(OrderStatus.Shipped, now => new OrderShipped(Id, now));

    public void Deliver() =>
        TransitionTo(OrderStatus.Delivered, now => new OrderDelivered(Id, now));

    // La saga de compensación llama aquí cuando algo falla a mitad
    // (p. ej. pago autorizado pero sin stock): cancela y libera.
    public void Cancel(string reason) =>
        TransitionTo(OrderStatus.Cancelled, now => new OrderCancelled(Id, reason, now));

    // La fábrica dejó de ser nullable cuando Deliver() empezó a emitir su evento:
    // el `IDomainEvent?` existía por una sola transición muda.
    private void TransitionTo(OrderStatus target, Func<DateTimeOffset, IDomainEvent> eventFactory)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"Illegal transition {Status} -> {target} for order {Id}.");

        Status = target;
        UpdatedAt = DateTimeOffset.UtcNow;

        Raise(eventFactory(UpdatedAt));
    }
}
