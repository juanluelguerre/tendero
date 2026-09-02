using System.Globalization;
using CsCheck;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

public sealed class MoneyTests
{
    private static Money Eur(decimal amount) => new(amount, "EUR");

    [Fact]
    public void Mixing_currencies_is_always_a_rejection()
    {
        Assert.Throws<InvalidOperationException>(() => Eur(10m) + new Money(10m, "USD"));
        Assert.Throws<InvalidOperationException>(() => Eur(10m) - new Money(10m, "USD"));
        Assert.Throws<InvalidOperationException>(() => Eur(10m) > new Money(10m, "USD"));
    }

    /// <summary>
    /// A percentage does NOT round. That is deliberate: chaining a discount, tax
    /// and a proportional split while rounding at every step is how cents go
    /// missing in a way nobody can explain. Rounding happens once, at the end.
    /// </summary>
    [Fact]
    public void A_percentage_keeps_its_precision_until_asked_to_round()
    {
        var vat = Eur(19.99m).Percent(21m);

        Assert.Equal(4.1979m, vat.Amount);
        Assert.Equal(4.20m, vat.Round().Amount);
    }

    /// <summary>
    /// The amounts travel as STRINGS and not as decimals. `[InlineData(2.005)]`
    /// goes through a double literal, so 2.00499999… arrives and the test fails
    /// measuring floating-point arithmetic instead of rounding. The reason Money
    /// uses decimal is exactly the one that breaks its own test when written the
    /// obvious way.
    /// </summary>
    [Theory]
    [InlineData("2.005", Rounding.ToEven, "2.00")]
    [InlineData("2.015", Rounding.ToEven, "2.02")]
    [InlineData("2.005", Rounding.AwayFromZero, "2.01")]
    [InlineData("-2.005", Rounding.AwayFromZero, "-2.01")]
    public void Rounding_is_a_decision_with_a_name(string amount, Rounding rounding, string expected)
    {
        var rounded = Eur(decimal.Parse(amount, CultureInfo.InvariantCulture)).Round(rounding);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), rounded.Amount);
    }

    [Fact]
    public void Dividing_by_zero_says_so_instead_of_producing_infinity()
    {
        Assert.Throws<DivideByZeroException>(() => Eur(10m) / 0m);
    }

    /// <summary>
    /// The case that springs a leak in every system that improvises it: 10.00 €
    /// across three equal parts is 3.33 + 3.33 + 3.33 = 9.99, and the missing
    /// cent ends up as an invoice that does not balance.
    /// </summary>
    [Fact]
    public void The_lost_cent_goes_somewhere_instead_of_disappearing()
    {
        var parts = Eur(10m).Allocate([1m, 1m, 1m]);

        Assert.Equal([3.34m, 3.33m, 3.33m], parts.Select(part => part.Amount));
        Assert.Equal(10.00m, parts.Sum(part => part.Amount));
    }

    /// <summary>
    /// Prorating an order discount across lines of different amounts: each line
    /// gets its share in proportion to its weight, and the remainder goes to the
    /// one with the largest discarded fraction.
    /// </summary>
    [Fact]
    public void Allocation_follows_the_weights()
    {
        var parts = Eur(100m).Allocate([70m, 20m, 10m]);

        Assert.Equal([70.00m, 20.00m, 10.00m], parts.Select(part => part.Amount));
    }

    [Fact]
    public void A_negative_amount_is_allocated_without_losing_a_cent_either()
    {
        var parts = Eur(-10m).Allocate([1m, 1m, 1m]);

        Assert.Equal(-10.00m, parts.Sum(part => part.Amount));
    }

    /// <summary>
    /// Deterministic: two runs give the same answer. It is what lets a split be
    /// frozen onto an order and recomputed without changing history.
    /// </summary>
    [Fact]
    public void The_same_allocation_twice_gives_the_same_answer()
    {
        var weights = new[] { 3m, 5m, 7m, 11m };

        Assert.Equal(
            Eur(37.13m).Allocate(weights).Select(part => part.Amount),
            Eur(37.13m).Allocate(weights).Select(part => part.Amount));
    }

    [Fact]
    public void Allocation_rejects_what_it_cannot_answer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Eur(10m).Allocate([]));
        Assert.Throws<InvalidOperationException>(() => Eur(10m).Allocate([0m, 0m]));
        Assert.Throws<InvalidOperationException>(() => Eur(10m).Allocate([1m, -1m]));
    }

    /// <summary>
    /// The property that matters, and the reason CsCheck earns its place: what
    /// is allocated sums back EXACTLY to what was allocated — for any amount and
    /// any weights, not for the handful of cases somebody thought of.
    ///
    /// This started life as a loop over two thousand amounts with one fixed set
    /// of weights, written that way because there was no property library yet.
    /// The loop only ever varied one of the two inputs; the generator varies
    /// both, and it shrinks a failure down to the smallest cart that shows it.
    /// </summary>
    [Fact]
    public void Whatever_is_allocated_always_sums_back_to_the_total()
    {
        Gen.Select(Gen.Int[-500_000, 500_000], Gen.Int[1, 5_000].Array[1, 8])
            .Sample(pair =>
            {
                var (cents, weights) = pair;

                var total = Eur(cents / 100m);
                var parts = total.Allocate([.. weights.Select(weight => (decimal)weight)]);

                Assert.Equal(total.Amount, parts.Sum(part => part.Amount));
                Assert.Equal(weights.Length, parts.Count);
            }, iter: 5_000);
    }

    /// <summary>
    /// Allocation is deterministic. It is what lets a discount be frozen onto an
    /// order and recomputed later without rewriting history — and it is only
    /// true because ties in the largest-remainder step break on the lowest
    /// index rather than on whatever order the sort happened to produce.
    /// </summary>
    [Fact]
    public void The_same_inputs_always_allocate_the_same_way()
    {
        Gen.Select(Gen.Int[1, 100_000], Gen.Int[1, 100].Array[2, 6])
            .Sample(pair =>
            {
                var (cents, weights) = pair;

                var total = Eur(cents / 100m);
                var asDecimals = weights.Select(weight => (decimal)weight).ToArray();

                Assert.Equal(
                    total.Allocate(asDecimals).Select(part => part.Amount),
                    total.Allocate(asDecimals).Select(part => part.Amount));
            }, iter: 2_000);
    }
}
