namespace ElGuerre.Tendero.SharedKernel;

// ---------- Strongly-typed ids ----------
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
/// A variant's identity — the PURCHASABLE unit. The product is the findable one
/// (ADR 0015): search matches and filters per variant and returns collapsed
/// products, so a cart and an order line talk about this, and a search result
/// talks about <see cref="ProductId"/>.
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
/// A cart's identity. Distinct from the token a guest's browser holds: the id is
/// how the system names the cart, the token is how an anonymous visitor proves
/// it is theirs, and conflating them would put a bearer credential in every URL
/// and every log line.
/// </summary>
public readonly record struct CartId(Guid Value)
{
    public static CartId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>A return request's identity. Returns are their own aggregate because
/// they are per LINE, which an order-level state machine cannot express without
/// a combinatorial explosion.</summary>
public readonly record struct ReturnRequestId(Guid Value)
{
    public static ReturnRequestId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// An image's identity: the hash of its content, not a GUID. Two products with
/// the same photo share an id, so deduplication comes free, and a key never
/// changes content, so it can be served as immutable.
/// </summary>
public readonly record struct ImageId(string Value)
{
    public override string ToString() => Value;
}

// ---------- Money as a value object ----------
/// <summary>
/// How money is rounded when it is split. It exists as a named decision and not
/// as a loose call to <c>Math.Round</c> because rounding money is a business
/// rule: two systems that round differently do not disagree by a cent, they
/// disagree by an invoice.
/// </summary>
public enum Rounding
{
    /// <summary>Banker's: 0.5 goes to the nearest even. The one that does not
    /// bias upwards over many operations, and .NET's default.</summary>
    ToEven,

    /// <summary>0.5 moves away from zero. What most people mean by "rounding",
    /// and what many tax authorities require.</summary>
    AwayFromZero
}

public readonly record struct Money(decimal Amount, string Currency)
{
    /// <summary>
    /// Two decimals. A deliberate laboratory simplification: the euro, the
    /// dollar and the pound have them, but the yen has none and the Kuwaiti
    /// dinar has three. The day a second currency arrives this leaves here and
    /// becomes a fact about the currency — and multi-currency is deferred on
    /// purpose (initial-plan §7).
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
    /// Multiplication by a fractional factor, WITHOUT rounding. It returns the
    /// exact amount so that whoever chains operations does not round at every
    /// step: rounding three times in a row is how cents go missing in a way
    /// nobody can explain. Rounding is asked for once, at the end, with
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
    /// A percentage of this amount, unrounded. <c>Percent(21)</c> is Spanish
    /// VAT; <c>Percent(10)</c> is a ten per cent discount.
    /// </summary>
    public Money Percent(decimal percent) => this * (percent / 100m);

    /// <summary>To the currency's number of decimals, with the policy asked for.</summary>
    public Money Round(Rounding rounding = Rounding.ToEven) =>
        this with
        {
            Amount = Math.Round(
                Amount,
                Decimals,
                rounding == Rounding.ToEven ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero)
        };

    /// <summary>
    /// Splits this amount across weights, without losing or inventing a cent.
    ///
    /// This is the operation <see cref="Money"/> had to grow for, and the one
    /// that springs a leak in every commerce system that improvises it:
    /// splitting 10.00 € across three equal lines gives 3.33 + 3.33 + 3.33 =
    /// 9.99, and the missing cent turns up later as an invoice that does not
    /// balance.
    ///
    /// The method is LARGEST REMAINDER: every share is floored, and the leftover
    /// cents are handed out one at a time to the shares whose discarded fraction
    /// was biggest. Ties go to the lowest index, so the split is deterministic
    /// and two runs give the same answer — which is what lets it be frozen onto
    /// an order.
    ///
    /// The sum of what comes back is EXACTLY this amount. That is the property,
    /// and it is the one that deserves a property test rather than three
    /// examples.
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

        // In minimal units — cents — because splitting in decimals and rounding
        // at the end is exactly the mistake this exists to avoid.
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

        // The cents left over go to the largest remainders. It can be negative
        // if the amount was, and then they are taken away in the same order.
        var leftover = totalUnits - assigned;
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => remainders[index])
            .ThenBy(index => index)
            .ToArray();

        for (var step = 0; step < Math.Abs(leftover); step++)
            shares[order[step % order.Length]] += Math.Sign(leftover);

        // A local copy: a lambda inside a struct cannot capture `this`.
        var currency = Currency;
        return [.. shares.Select(share => new Money(share * unit, currency))];
    }

    /// <summary>The currency's smallest unit: 0.01 with two decimals.</summary>
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

// ---------- Cultures ----------
/// <summary>
/// Normalisation of culture codes, in one place. The rule — keep the primary
/// subtag, lowercased, "es-ES" becomes "es" — was applied independently by
/// <see cref="LocalizedText"/> and by <c>Order.Place</c>, with the same
/// <c>Split</c> written twice. They are the same decision: what "the culture of
/// this" means.
/// </summary>
public static class Culture
{
    public static string Normalize(string culture) =>
        culture.Split('-', '_')[0].ToLowerInvariant();
}

// ---------- Localized text ----------
/// <summary>
/// A value object for multilingual text. ISO 639-1 keys, lowercase ("es", "en").
/// The fallback resolution lives here, not scattered through the application.
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

    /// <summary>The resolution chain: requested culture -> fallback -> first available.</summary>
    public string In(string culture, string fallback = "en") =>
        _values.TryGetValue(Normalize(culture), out var value) ? value
        : _values.TryGetValue(Normalize(fallback), out var fb) ? fb
        : _values.Values.First();

    /// <summary>Returns a copy with the translation added or replaced (the AI
    /// enrichment slice will use it).</summary>
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

    // The persistence pipeline drains them into the Outbox table and clears them.
    public void ClearDomainEvents() => _events.Clear();
}
