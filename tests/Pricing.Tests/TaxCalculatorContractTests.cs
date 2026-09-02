using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// What every tax calculator must honour, whoever writes it.
///
/// The same arrangement as <c>CatalogSourceConnectorContractTests</c>: an
/// abstract suite the adapter inherits in three lines. A new adapter without its
/// subclass does not merge — which is the difference between a port and an
/// interface somebody happened to extract.
///
/// Two implementations that genuinely differ is the point. A contract suite with
/// one adapter proves that the adapter agrees with itself.
/// </summary>
public abstract class TaxCalculatorContractTests
{
    protected abstract ITaxCalculator Calculator { get; }

    private static TaxRequest Request(params (string TaxClass, decimal Net)[] amounts) =>
        new(Build.Eur, [.. amounts.Select(a => new TaxableAmount(a.TaxClass, Build.Money(a.Net)))]);

    [Fact]
    public void The_key_is_a_stable_lowercase_identifier()
    {
        Assert.False(string.IsNullOrWhiteSpace(Calculator.Key));
        Assert.Equal(Calculator.Key.ToLowerInvariant(), Calculator.Key);
        Assert.DoesNotContain(' ', Calculator.Key);
    }

    [Fact]
    public void Tax_is_never_negative_on_a_non_negative_base()
    {
        var assessment = Calculator.Assess(Request(("standard", 100m), ("reduced", 50m)));

        Assert.False(assessment.Total.IsNegative);
        Assert.All(assessment.Lines, line => Assert.False(line.Amount.IsNegative));
    }

    [Fact]
    public void The_currency_of_the_request_is_the_currency_of_the_answer()
    {
        var assessment = Calculator.Assess(Request(("standard", 100m)));

        Assert.Equal(Build.Eur, assessment.Total.Currency);
        Assert.All(assessment.Lines, line => Assert.Equal(Build.Eur, line.Amount.Currency));
    }

    [Fact]
    public void The_breakdown_adds_up_to_the_total()
    {
        var assessment = Calculator.Assess(Request(("standard", 19.99m), ("reduced", 7.35m), ("standard", 4.95m)));

        var sum = assessment.Lines.Aggregate(
            Money.Zero(Build.Eur), (total, line) => total + line.Amount);

        Assert.Equal(assessment.Total.Amount, sum.Amount);
    }

    [Fact]
    public void A_zero_base_produces_zero_tax()
    {
        var assessment = Calculator.Assess(Request(("standard", 0m)));

        Assert.True(assessment.Total.IsZero);
    }

    [Fact]
    public void Nothing_to_tax_is_answered_with_zero_and_not_with_an_exception()
    {
        var assessment = Calculator.Assess(new TaxRequest(Build.Eur, []));

        Assert.True(assessment.Total.IsZero);
        Assert.Empty(assessment.Lines);
    }

    /// <summary>
    /// Assessing twice gives the same answer. It sounds trivial until a
    /// calculator caches a rate or reads a clock: a quote is frozen and
    /// revalidated later, and a calculator that drifts breaks the revalidation
    /// rather than the calculation.
    /// </summary>
    [Fact]
    public void Assessing_the_same_request_twice_gives_the_same_answer()
    {
        var request = Request(("standard", 33.33m), ("reduced", 12.10m));

        Assert.Equal(
            Calculator.Assess(request).Total.Amount,
            Calculator.Assess(request).Total.Amount);
    }
}

public sealed class FlatVatTaxCalculatorContractTests : TaxCalculatorContractTests
{
    protected override ITaxCalculator Calculator { get; } =
        new FlatVatTaxCalculator(Options.Create(new FlatVatOptions()));
}

public sealed class ZeroTaxCalculatorContractTests : TaxCalculatorContractTests
{
    protected override ITaxCalculator Calculator { get; } = new ZeroTaxCalculator();
}

/// <summary>What is specific to Spanish VAT, and therefore not in the contract.</summary>
public sealed class FlatVatTaxCalculatorTests
{
    private readonly FlatVatTaxCalculator _calculator = new(Options.Create(new FlatVatOptions()));

    [Fact]
    public void The_standard_rate_is_twenty_one_per_cent()
    {
        var assessment = _calculator.Assess(
            new TaxRequest(Build.Eur, [new TaxableAmount("standard", Build.Money(100m))]));

        Assert.Equal(21m, assessment.Total.Amount);
    }

    /// <summary>
    /// A receipt carries one line per rate, not one per article. Grouping before
    /// the arithmetic also keeps the total honest: rounding each article and
    /// adding up gives a different number than rounding the aggregated base.
    /// </summary>
    [Fact]
    public void The_breakdown_carries_one_line_per_rate()
    {
        var assessment = _calculator.Assess(new TaxRequest(Build.Eur,
        [
            new TaxableAmount("standard", Build.Money(10m)),
            new TaxableAmount("standard", Build.Money(10m)),
            new TaxableAmount("reduced", Build.Money(10m))
        ]));

        Assert.Equal(2, assessment.Lines.Count);
        Assert.Equal(20m, assessment.Lines.Single(line => line.TaxClass == "standard").Base.Amount);
        Assert.Equal(4.20m, assessment.Lines.Single(line => line.TaxClass == "standard").Amount.Amount);
        Assert.Equal(1.00m, assessment.Lines.Single(line => line.TaxClass == "reduced").Amount.Amount);
    }

    [Fact]
    public void An_unknown_tax_class_is_charged_nothing_rather_than_guessed_at()
    {
        var assessment = _calculator.Assess(
            new TaxRequest(Build.Eur, [new TaxableAmount("made-up", Build.Money(100m))]));

        Assert.True(assessment.Total.IsZero);
    }
}
