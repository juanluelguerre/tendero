namespace ElGuerre.Tendero.SharedKernel;

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

/// <summary>
/// Identidad de una variante — la unidad COMPRABLE. El producto es la unidad
/// encontrable (ADR 0015): la búsqueda casa y filtra por variante y devuelve
/// productos colapsados, así que un carrito y una línea de pedido hablan de
/// esto, y un resultado de búsqueda habla de <see cref="ProductId"/>.
/// </summary>
public readonly record struct VariantId(Guid Value)
{
    public static VariantId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identidad de una imagen: el hash de su contenido, no un GUID. Dos productos
/// con la misma foto comparten id, así que la deduplicación sale gratis, y una
/// clave nunca cambia de contenido, así que puede servirse como inmutable.
/// </summary>
public readonly record struct ImageId(string Value)
{
    public override string ToString() => Value;
}

// ---------- Dinero como value object ----------
/// <summary>
/// Cómo se redondea al partir dinero. Existe como decisión con nombre y no como
/// una llamada suelta a <c>Math.Round</c> porque el redondeo de dinero es una
/// regla de negocio: dos sistemas que redondean distinto no discrepan en un
/// céntimo, discrepan en la factura.
/// </summary>
public enum Rounding
{
    /// <summary>Bancario: 0,5 va al par más cercano. El que no sesga al alza
    /// sobre muchas operaciones, y el que usa .NET por defecto.</summary>
    ToEven,

    /// <summary>0,5 se aleja del cero. Es lo que la mayoría de la gente entiende
    /// por "redondear", y lo que muchas haciendas exigen.</summary>
    AwayFromZero
}

public readonly record struct Money(decimal Amount, string Currency)
{
    /// <summary>
    /// Dos decimales. Es una simplificación deliberada de laboratorio: el euro,
    /// el dólar y la libra los tienen, pero el yen tiene cero y el dinar
    /// kuwaití tres. El día que entre una segunda divisa, esto sale de aquí y
    /// pasa a ser un dato de la divisa — y multi-divisa está aplazado a
    /// propósito (initial-plan §7).
    /// </summary>
    public const int Decimals = 2;

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a with { Amount = a.Amount + b.Amount };
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a with { Amount = a.Amount - b.Amount };
    }

    public static Money operator *(Money m, int factor) =>
        m with { Amount = m.Amount * factor };

    /// <summary>
    /// Multiplicación por un factor fraccionario, SIN redondear. Devuelve el
    /// importe exacto para que quien encadene operaciones no redondee a cada
    /// paso: redondear tres veces seguidas es cómo se pierden céntimos que nadie
    /// sabe explicar. El redondeo se pide una vez, al final, con
    /// <see cref="Round"/>.
    /// </summary>
    public static Money operator *(Money m, decimal factor) =>
        m with { Amount = m.Amount * factor };

    public static Money operator /(Money m, decimal divisor) =>
        divisor == 0m
            ? throw new DivideByZeroException($"Cannot divide {m} by zero.")
            : m with { Amount = m.Amount / divisor };

    public static bool operator >(Money a, Money b) => Compare(a, b) > 0;
    public static bool operator <(Money a, Money b) => Compare(a, b) < 0;
    public static bool operator >=(Money a, Money b) => Compare(a, b) >= 0;
    public static bool operator <=(Money a, Money b) => Compare(a, b) <= 0;

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    /// <summary>
    /// Un porcentaje de este importe, sin redondear. <c>Percent(21)</c> es el
    /// IVA español; <c>Percent(10)</c> un descuento del diez por ciento.
    /// </summary>
    public Money Percent(decimal percent) => this * (percent / 100m);

    /// <summary>Al número de decimales de la divisa, con la política pedida.</summary>
    public Money Round(Rounding rounding = Rounding.ToEven) =>
        this with
        {
            Amount = Math.Round(
                Amount,
                Decimals,
                rounding == Rounding.ToEven ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero)
        };

    /// <summary>
    /// Reparte este importe entre varios pesos, sin perder ni inventar un
    /// céntimo.
    ///
    /// Es la operación por la que <see cref="Money"/> tenía que crecer, y la que
    /// hace agua en todo sistema de comercio que la improvisa: repartir 10,00 €
    /// entre tres líneas iguales da 3,33 + 3,33 + 3,33 = 9,99, y el céntimo que
    /// falta acaba apareciendo como un descuadre en la factura.
    ///
    /// El método es el del RESTO MAYOR: se redondea cada parte hacia abajo y los
    /// céntimos sobrantes se dan de uno en uno a las partes cuya fracción
    /// descartada era mayor. Con empate gana el índice más bajo, para que el
    /// reparto sea determinista y dos ejecuciones den lo mismo — que es lo que
    /// permite congelarlo en un pedido.
    ///
    /// La suma de lo devuelto es EXACTAMENTE este importe. Esa es la propiedad,
    /// y es la que merece un test de propiedades en vez de tres ejemplos.
    /// </summary>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<decimal> weights)
    {
        ArgumentOutOfRangeException.ThrowIfZero(weights.Count);

        if (weights.Any(weight => weight < 0m))
            throw new InvalidOperationException("Allocation weights cannot be negative.");

        var total = weights.Sum();
        if (total == 0m)
            throw new InvalidOperationException("Allocation weights cannot all be zero.");

        var unit = Smallest();
        var target = Round();

        // En unidades mínimas —céntimos— porque repartir en decimales y redondear
        // al final es exactamente el error que esto existe para evitar.
        var totalUnits = (long)decimal.Round(target.Amount / unit, 0, MidpointRounding.AwayFromZero);

        var shares = new long[weights.Count];
        var remainders = new decimal[weights.Count];
        var assigned = 0L;

        for (var index = 0; index < weights.Count; index++)
        {
            var exact = totalUnits * weights[index] / total;
            var whole = decimal.Truncate(exact);

            shares[index] = (long)whole;
            remainders[index] = exact - whole;
            assigned += shares[index];
        }

        // Los céntimos que quedan, al mayor resto. Puede ser negativo si el
        // importe lo era, y entonces se quitan en el mismo orden.
        var leftover = totalUnits - assigned;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => remainders[index])
            .ThenBy(index => index)
            .ToArray();

        for (var step = 0; step < Math.Abs(leftover); step++)
            shares[order[step % order.Length]] += Math.Sign(leftover);

        // Copia local: una lambda dentro de un struct no puede capturar `this`.
        var currency = Currency;
        return [.. shares.Select(share => new Money(share * unit, currency))];
    }

    /// <summary>La unidad mínima de la divisa: 0,01 con dos decimales.</summary>
    private static decimal Smallest() => 1m / (decimal)Math.Pow(10, Decimals);

    private static int Compare(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount.CompareTo(b.Amount);
    }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}

// ---------- Culturas ----------
/// <summary>
/// Normalización de códigos de cultura, en un solo sitio. La regla —quedarse con
/// la subetiqueta primaria en minúsculas, "es-ES" → "es"— la aplicaban por su
/// cuenta <see cref="LocalizedText"/> y <c>Order.Place</c>, con el mismo
/// <c>Split</c> escrito dos veces. Son la misma decisión: qué significa "la
/// cultura de esto".
/// </summary>
public static class Culture
{
    public static string Normalize(string culture) =>
        culture.Split('-', '_')[0].ToLowerInvariant();
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

    private static string Normalize(string culture) => Culture.Normalize(culture);
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
