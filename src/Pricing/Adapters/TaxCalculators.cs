using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Pricing.Adapters;

public sealed class FlatVatOptions
{
    public const string SectionName = "Pricing:Tax:FlatVat";

    /// <summary>
    /// Rate per tax class, as a percentage. The defaults are the Spanish 2026
    /// ones — standard 21, reduced 10, super-reduced 4 — because the laboratory
    /// shop sells in Spain, and a default of zero would have hidden everything
    /// interesting about the calculation.
    /// </summary>
    public Dictionary<string, decimal> Rates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["standard"] = 21m,
        ["reduced"] = 10m,
        ["super-reduced"] = 4m,
        ["exempt"] = 0m
    };

    public string DefaultTaxClass { get; set; } = TaxableAmount.Standard;
}

/// <summary>
/// VAT by tax class, with no destination: one country.
///
/// **It extracts the VAT a price already contains; it never adds any** (ADR
/// 0028). Catalogue prices are what a shopper in Spain is shown and pays, and
/// by law those include VAT. Until 2026-09-11 this calculator treated them as
/// net and the quote added 21 % on top, so a pair of shoes shown at 59,96 was
/// charged 72,55 under a total that said "VAT included".
///
/// The absence of destination rules is a declared simplification and not an
/// oversight — the destination already travels in <see cref="TaxRequest"/>, so
/// the adapter that uses it is a new class and a line of registration, and not
/// one <c>if</c> in the engine.
/// </summary>
internal sealed class FlatVatTaxCalculator(IOptions<FlatVatOptions> options) : ITaxCalculator
{
    public const string Key = "flat-vat";

    string ITaxCalculator.Key => Key;

    public TaxAssessment Assess(TaxRequest request)
    {
        var settings = options.Value;

        // Grouped by class before the arithmetic: a receipt's breakdown carries
        // one line per rate, not one per article, and adding up per-article
        // roundings afterwards would give a different total than rounding the
        // aggregated base once.
        var lines = request.Amounts
            .GroupBy(amount => Normalise(amount.TaxClass, settings), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var rate = settings.Rates.GetValueOrDefault(group.Key, 0m);
                var paid = group.Aggregate(
                    Money.Zero(request.Currency), (total, amount) => total + amount.Inclusive);

                // The base is rounded once and the tax is whatever is left, so
                // base plus tax is what was paid to the cent. Rounding both would
                // be two roundings of one amount, and a receipt a cent adrift.
                var basis = (paid / (1m + rate / 100m)).Round(Rounding.AwayFromZero);

                return new TaxLine(group.Key, rate, basis, paid - basis);
            })
            .ToArray();

        return new TaxAssessment(
            lines,
            lines.Aggregate(Money.Zero(request.Currency), (total, line) => total + line.Amount));
    }

    private static string Normalise(string? taxClass, FlatVatOptions settings) =>
        string.IsNullOrWhiteSpace(taxClass) ? settings.DefaultTaxClass : taxClass.Trim().ToLowerInvariant();
}

/// <summary>
/// No tax at all. Not a test stub: it is the right adapter for an exempt
/// market, and it is what gives the contract two implementations that genuinely
/// differ — a contract suite with a single adapter only proves the adapter
/// agrees with itself.
/// </summary>
internal sealed class ZeroTaxCalculator : ITaxCalculator
{
    public const string Key = "zero";

    string ITaxCalculator.Key => Key;

    public TaxAssessment Assess(TaxRequest request) =>
        new([], Money.Zero(request.Currency));
}

/// <summary>
/// The only place in Pricing that talks to the container. Same pattern as
/// <c>KeyedCatalogSourceRegistry</c>, for the same reason: the list of keys
/// comes from the registry and not from a constant, because a constant and a
/// registry are two truths that drift apart.
/// </summary>
internal sealed class KeyedTaxCalculatorRegistry(IServiceProvider services) : ITaxCalculatorRegistry
{
    public IReadOnlyCollection<string> Keys =>
        [.. services.GetKeyedServices<ITaxCalculator>(KeyedService.AnyKey)
            .Select(calculator => calculator.Key)
            .Order(StringComparer.Ordinal)];

    public ITaxCalculator Get(string key) =>
        services.GetKeyedService<ITaxCalculator>(key) ?? throw new UnknownTaxCalculatorException(key, Keys);
}
