using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>A taxable base and the tax class that governs it.</summary>
public sealed record TaxableAmount(string TaxClass, Money Net)
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

/// <summary>One line of the breakdown: the rate applied, on what, for how
/// much.</summary>
public sealed record TaxLine(string TaxClass, decimal Rate, Money Base, Money Amount);

public sealed record TaxAssessment(IReadOnlyList<TaxLine> Lines, Money Total);

/// <summary>
/// Calculates tax. It lives in Pricing — not in Ordering — because tax is a
/// function of price, tax class and destination, and the storefront has to show
/// tax-inclusive prices long before a checkout exists.
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
