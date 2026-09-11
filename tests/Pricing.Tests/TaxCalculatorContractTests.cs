using CsCheck;
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

    private static TaxRequest Request(params (string TaxClass, decimal Paid)[] amounts) =>
        new(Build.Eur, [.. amounts.Select(a => new TaxableAmount(a.TaxClass, Build.Money(a.Paid)))]);

    [Fact]
    public void The_key_is_a_stable_lowercase_identifier()
    {
        Assert.False(String.IsNullOrWhiteSpace(Calculator.Key));
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

    /// <summary>
    /// The amounts a calculator receives are what the shopper pays, VAT included
    /// (ADR 0028), and what it answers is the tax INSIDE them. So each line of the
    /// breakdown splits what its class paid into a base and a tax that add up to
    /// it exactly — a cent lost to rounding would be a receipt whose lines do not
    /// add up to its total.
    /// </summary>
    [Fact]
    public void Each_line_splits_what_its_class_paid_into_a_base_and_a_tax()
    {
        var request = Request(("standard", 19.99m), ("reduced", 7.35m), ("standard", 4.95m));

        var assessment = Calculator.Assess(request);

        Assert.All(assessment.Lines, line =>
        {
            var paid = request.Amounts
                .Where(amount => string.Equals(amount.TaxClass, line.TaxClass, StringComparison.OrdinalIgnoreCase))
                .Aggregate(Money.Zero(Build.Eur), (sum, amount) => sum + amount.Inclusive);

            Assert.Equal(paid, line.Base + line.Amount);
        });
    }

    /// <summary>The tax is part of what was paid, so it can never be more than
    /// it. An adapter that added the tax on top would fail here first.</summary>
    [Fact]
    public void The_tax_is_never_more_than_what_was_paid()
    {
        var assessment = Calculator.Assess(Request(("standard", 100m), ("reduced", 50m)));

        Assert.True(assessment.Total <= Build.Money(150m));
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
    private readonly FlatVatTaxCalculator calculator = new(Options.Create(new FlatVatOptions()));

    /// <summary>
    /// 121 paid at the standard rate is 100 of base and 21 of VAT. Not 25,41
    /// added on top — which is what this calculator answered until ADR 0028,
    /// while the storefront printed "VAT included" under the total.
    /// </summary>
    [Fact]
    public void The_standard_rate_is_twenty_one_per_cent_of_the_base_inside_the_price()
    {
        var assessment = this.calculator.Assess(
            new TaxRequest(Build.Eur, [new TaxableAmount("standard", Build.Money(121m))]));

        Assert.Equal(21m, assessment.Total.Amount);
        Assert.Equal(100m, assessment.Lines.Single().Base.Amount);
    }

    /// <summary>
    /// A receipt carries one line per rate, not one per article. Grouping before
    /// the arithmetic also keeps the total honest: rounding each article and
    /// adding up gives a different number than rounding the aggregated base.
    /// </summary>
    [Fact]
    public void The_breakdown_carries_one_line_per_rate()
    {
        var assessment = this.calculator.Assess(
            new TaxRequest(
                Build.Eur,
                [
                    new TaxableAmount("standard", Build.Money(12.10m)),
                    new TaxableAmount("standard", Build.Money(12.10m)),
                    new TaxableAmount("reduced", Build.Money(11m))
                ]));

        Assert.Equal(2, assessment.Lines.Count);
        Assert.Equal(20m, assessment.Lines.Single(line => line.TaxClass == "standard").Base.Amount);
        Assert.Equal(4.20m, assessment.Lines.Single(line => line.TaxClass == "standard").Amount.Amount);
        Assert.Equal(1.00m, assessment.Lines.Single(line => line.TaxClass == "reduced").Amount.Amount);
    }

    /// <summary>
    /// Base and tax add up to what was paid over any amounts at all, not only the
    /// ones above: extracting VAT is where a cent goes missing, and the only
    /// honest way to lose none is to round the base once and let the tax be the
    /// remainder.
    /// </summary>
    [Fact]
    public void Base_and_tax_add_up_to_what_was_paid_for_any_amounts()
    {
        Gen.Select(Gen.Int[0, 50_000], Gen.Int[0, 3])
            .Array[1, 6]
            .Sample(pairs =>
            {
                string[] classes = ["standard", "reduced", "super-reduced", "exempt"];
                var amounts = pairs
                    .Select(pair => new TaxableAmount(classes[pair.Item2], Build.Money(pair.Item1 / 100m)))
                    .ToArray();

                var assessment = this.calculator.Assess(new TaxRequest(Build.Eur, amounts));

                var paid = amounts.Aggregate(Money.Zero(Build.Eur), (sum, amount) => sum + amount.Inclusive);
                var split = assessment.Lines.Aggregate(
                    Money.Zero(Build.Eur), (sum, line) => sum + line.Base + line.Amount);

                Assert.Equal(paid, split);
                Assert.All(assessment.Lines, line => Assert.False(line.Amount.IsNegative));
            }, iter: 2_000);
    }

    [Fact]
    public void An_unknown_tax_class_is_charged_nothing_rather_than_guessed_at()
    {
        var assessment = this.calculator.Assess(
            new TaxRequest(Build.Eur, [new TaxableAmount("made-up", Build.Money(100m))]));

        Assert.True(assessment.Total.IsZero);
    }
}
