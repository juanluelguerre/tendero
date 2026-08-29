namespace Tendero.SharedKernel;

// ---------- Ids fuertemente tipados ----------
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

// ---------- Dinero como value object ----------
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a with { Amount = a.Amount + b.Amount };
    }

    public static Money operator *(Money m, int factor) =>
        m with { Amount = m.Amount * factor };

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}

// ---------- Texto localizado ----------
/// <summary>
/// Value object para textos multilenguaje. Claves ISO 639-1 en minúsculas ("es", "en").
/// La resolución con fallback vive aquí, no repartida por la aplicación.
/// </summary>
public sealed class LocalizedText
{
    private readonly Dictionary<string, string> _values;

    public LocalizedText(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("At least one translation is required.");
        _values = values.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value);
    }

    public static LocalizedText From(string culture, string value) =>
        new(new Dictionary<string, string> { [culture] = value });

    public IReadOnlyDictionary<string, string> Values => _values;
    public IReadOnlyCollection<string> Cultures => _values.Keys;

    /// <summary>Cadena de resolución: cultura pedida -> fallback -> primera disponible.</summary>
    public string In(string culture, string fallback = "en") =>
        _values.TryGetValue(Normalize(culture), out var value) ? value
        : _values.TryGetValue(Normalize(fallback), out var fb) ? fb
        : _values.Values.First();

    /// <summary>Devuelve una copia con la traducción añadida o reemplazada
    /// (lo usará el slice de enriquecimiento con IA).</summary>
    public LocalizedText With(string culture, string value)
    {
        var copy = new Dictionary<string, string>(_values) { [Normalize(culture)] = value };
        return new LocalizedText(copy);
    }

    private static string Normalize(string culture) =>
        culture.Split('-', '_')[0].ToLowerInvariant();
}

// ---------- Eventos de dominio ----------
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);

    // El pipeline de persistencia los vuelca a la tabla Outbox y los limpia.
    public void ClearDomainEvents() => _events.Clear();
}
