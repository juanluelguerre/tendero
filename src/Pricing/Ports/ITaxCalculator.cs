using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>
/// An amount as the shopper pays it — <b>VAT included</b> — and the tax class
/// that governs it (ADR 0028). Named <c>Inclusive</c> rather than <c>Net</c> so
/// that nobody reads it as a base to add tax to, which is exactly how a total
/// once came to charge 21 % more than every price the shop displayed.
/// </summary>
public sealed record TaxableAmount(string TaxClass, Money Inclusive)
{
    /// <summary>What applies when a variant declares no tax class. It exists
    /// as a constant so that "no class" is a decision rather than a <c>null</c>
    /// each calculator interprets its own way.</summary>
    public const string Standard = "standard";
}

/// <summary>Shipping is taxed too, and not always at the same rate as the
/// goods. It travels as one more base rather than as a special case.</summary>
public sealed record TaxRequest(
    string Currency,
    IReadOnlyList<TaxableAmount> Amounts,
    string? DestinationCountry = null);

/// <summary>One line of the breakdown: the rate, the base it applies to, and the
/// tax contained in what was paid. <c>Base + Amount</c> is what the class paid,
/// to the cent — the contract suite holds every adapter to it.</summary>
public sealed record TaxLine(string TaxClass, decimal Rate, Money Base, Money Amount);

public sealed record TaxAssessment(IReadOnlyList<TaxLine> Lines, Money Total);

/// <summary>
/// Says how much of what is paid is tax. It lives in Pricing — not in Ordering —
/// because tax is a function of price, tax class and destination, and the
/// storefront has to show tax-inclusive prices long before a checkout exists.
///
/// It extracts and never adds: catalogue prices already include VAT (ADR 0028),
/// so the answer is a breakdown of the total, not an increment to it.
///
/// Keyed adapters (ADR 0003): <c>flat-vat</c> and <c>zero</c> today. Each
/// inherits the same contract suite, which is what makes adding a third one a
/// class and a line of registration rather than a review.
/// </summary>
public interface ITaxCalculator
{
    /// <summary>The key it registers under. The adapter exposes it so the
    /// registry can be listed from the container rather than from a constant
    /// somebody will forget to update.</summary>
    string Key { get; }

    TaxAssessment Assess(TaxRequest request);
}

public sealed class UnknownTaxCalculatorException(string key, IReadOnlyCollection<string> known)
    : InvalidOperationException($"Unknown tax calculator '{key}'. Known: {string.Join(", ", known)}.")
{
    public string Key { get; } = key;
    public IReadOnlyCollection<string> Known { get; } = known;
}

/// <summary>The only place in Pricing allowed to talk to the container. Same
/// pattern as <c>ICatalogSourceRegistry</c>, for the same reason: the
/// <c>IServiceProvider</c> never travels as far as a handler.</summary>
public interface ITaxCalculatorRegistry
{
    IReadOnlyCollection<string> Keys { get; }
    ITaxCalculator Get(string key);
}
