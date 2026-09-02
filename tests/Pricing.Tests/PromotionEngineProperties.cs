using CsCheck;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Engine;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// Combination rules are the canonical property-based case, and the reason
/// CsCheck enters the repository here rather than earlier.
///
/// The examples in <see cref="PromotionEngineTests"/> say what happens for six
/// carts somebody thought of. These say what is true for every cart — and the
/// difference matters because the interesting failures of a promotions engine
/// live in the combinations nobody thought of: three stackable discounts that
/// together exceed the line, an exclusive one reached after the cart is already
/// empty, two promotions of one group at equal priority.
/// </summary>
public sealed class PromotionEngineProperties
{
    private static readonly CombiningPromotionEngine Engine = new();

    private const int Iterations = 2_000;

    // ---------- Generators ----------

    private static readonly Gen<PricedLine> GenLine =
        Gen.Select(
            Gen.Int[1, 40],                 // unit price in whole euros plus cents below
            Gen.Int[0, 99],
            Gen.Int[1, 4],
            Gen.Int[0, 2],
            (whole, cents, quantity, category) => Build.Line(
                $"SKU{whole}-{cents}-{quantity}-{category}",
                whole + cents / 100m,
                quantity,
                categoryPath: category switch
                {
                    0 => "APPAREL/SPORTSWEAR/SHIRT",
                    1 => "HOME/KITCHEN/COOKWARE",
                    _ => null
                }));

    private static readonly Gen<PromotionEffect> GenEffect =
        Gen.Select(
            Gen.Int[0, 3],
            Gen.Int[1, 60],
            Gen.Int[1, 30],
            Gen.Int[1, 3],
            (kind, percent, amount, buy) => (PromotionEffect)(kind switch
            {
                0 => new PercentOffLine(percent),
                1 => new AmountOffOrder(Build.Money(amount)),
                2 => new BuyXGetY(buy, 1),
                _ => new FreeShipping()
            }));

    private static readonly Gen<Promotion> GenPromotion =
        Gen.Select(
            Gen.Int[0, 99],
            GenEffect,
            Gen.Int[0, 2],
            Gen.Int[0, 2],
            Gen.Int[0, 2],
            (index, effect, policy, group, scope) => Build.Promotion(
                $"P{index:00}",
                effect,
                (CombinationPolicy)policy,
                priority: index % 5,
                condition: scope switch
                {
                    0 => PromotionCondition.Always,
                    1 => new PromotionCondition(CategoryCode: "APPAREL"),
                    _ => new PromotionCondition(MinimumSubtotal: Build.Money(20m))
                },
                group: group switch { 0 => null, 1 => "seasonal", _ => "welcome" }));

    private static readonly Gen<(PricedCart Cart, Promotion[] Promotions)> GenScenario =
        Gen.Select(
            GenLine.Array[1, 4],
            GenPromotion.Array[0, 4],
            Gen.Int[0, 900],
            (lines, promotions, shipping) =>
                (Build.Cart(shipping / 100m, "retail", lines), Distinct(promotions)));

    /// <summary>Two promotions with the same code are one promotion applied
    /// twice, which is a generator artefact rather than a case worth testing.</summary>
    private static Promotion[] Distinct(Promotion[] promotions) =>
        [.. promotions.GroupBy(promotion => promotion.Code).Select(group => group.First())];

    // ---------- Properties ----------

    /// <summary>
    /// The cart never becomes an income. This is the property that a
    /// hand-written example set reliably misses: it takes three stackable
    /// discounts landing on one line to break it.
    /// </summary>
    [Fact]
    public void No_combination_of_promotions_ever_makes_a_total_negative()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            Assert.False(outcome.Cart.Net.IsNegative);
            Assert.False(outcome.Cart.Shipping.IsNegative);
            Assert.All(outcome.Cart.Lines, line => Assert.False(line.Net.IsNegative));
        }, iter: Iterations);
    }

    [Fact]
    public void What_is_discounted_never_exceeds_what_the_cart_was_worth()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            Assert.True(outcome.Cart.DiscountTotal <= scenario.Cart.Gross);
        }, iter: Iterations);
    }

    /// <summary>
    /// The sum of the applied discounts is exactly what came off the lines, plus
    /// whatever came off the shipping. If those two ever drift apart, the
    /// receipt shows discounts that do not explain the total — the single most
    /// common way a promotions engine loses a shopkeeper's trust.
    /// </summary>
    [Fact]
    public void The_discounts_reported_are_exactly_the_discounts_taken()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            var reported = outcome.Discounts
                .Where(discount => discount.Outcome == DiscountOutcome.Applied)
                .Aggregate(Money.Zero(Build.Eur), (total, discount) => total + discount.Amount);

            var taken = outcome.Cart.DiscountTotal + (scenario.Cart.Shipping - outcome.Cart.Shipping);

            Assert.Equal(taken.Amount, reported.Amount);
        }, iter: Iterations);
    }

    /// <summary>
    /// Evaluation order is (Priority, Code), so the order the promotions arrive
    /// in cannot matter. Without the code tiebreak this fails immediately — which
    /// is the point of asserting it rather than trusting the OrderBy.
    /// </summary>
    [Fact]
    public void The_order_promotions_arrive_in_never_changes_the_total()
    {
        Gen.Select(GenScenario, Gen.Int[0, 5]).Sample(pair =>
        {
            var (scenario, rotation) = pair;

            var rotated = scenario.Promotions
                .Skip(rotation % Math.Max(scenario.Promotions.Length, 1))
                .Concat(scenario.Promotions.Take(rotation % Math.Max(scenario.Promotions.Length, 1)))
                .ToArray();

            var first = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());
            var second = Engine.Apply(rotated, scenario.Cart, Build.At());

            Assert.Equal(first.Cart.Net.Amount, second.Cart.Net.Amount);
            Assert.Equal(first.Cart.Shipping.Amount, second.Cart.Shipping.Amount);
        }, iter: Iterations);
    }

    [Fact]
    public void Two_promotions_of_one_exclusivity_group_are_never_both_applied()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            var byGroup = outcome.Discounts
                .Where(discount => discount.Outcome == DiscountOutcome.Applied)
                .Join(scenario.Promotions,
                      discount => discount.PromotionCode,
                      promotion => promotion.Code,
                      (_, promotion) => promotion)
                .Where(promotion => promotion.Combination == CombinationPolicy.ExclusiveInGroup
                                 && promotion.ExclusivityGroup is not null)
                .GroupBy(promotion => promotion.ExclusivityGroup!);

            Assert.All(byGroup, group => Assert.Single(group));
        }, iter: Iterations);
    }

    /// <summary>
    /// Once a globally exclusive promotion applies, nothing after it does. The
    /// ones before it keep their discounts, which is what "wins over everything
    /// below" means and what the example test pins down.
    /// </summary>
    [Fact]
    public void Nothing_applies_after_a_globally_exclusive_promotion_has()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            var applied = outcome.Discounts
                .Select((discount, index) => (discount, index))
                .Where(pair => pair.discount.Outcome == DiscountOutcome.Applied)
                .ToArray();

            var exclusive = applied
                .Where(pair => pair.discount.Combination == CombinationPolicy.ExclusiveGlobal)
                .Select(pair => pair.index)
                .ToArray();

            if (exclusive.Length == 0)
                return;

            Assert.Single(exclusive);
            Assert.All(applied, pair => Assert.True(pair.index <= exclusive[0]));
        }, iter: Iterations);
    }

    /// <summary>
    /// Every promotion that was eligible comes back with a verdict, and every
    /// suppression carries a reason. A silent disappearance is the failure this
    /// whole phase exists to make impossible.
    /// </summary>
    [Fact]
    public void Every_suppressed_promotion_carries_a_reason_in_both_cultures()
    {
        GenScenario.Sample(scenario =>
        {
            var outcome = Engine.Apply(scenario.Promotions, scenario.Cart, Build.At());

            Assert.All(
                outcome.Discounts.Where(discount => discount.Outcome == DiscountOutcome.Suppressed),
                discount =>
                {
                    Assert.NotNull(discount.Reason);
                    Assert.False(string.IsNullOrWhiteSpace(discount.Reason!.Explanation.In("es")));
                    Assert.False(string.IsNullOrWhiteSpace(discount.Reason.Explanation.In("en")));
                    Assert.True(discount.Amount.IsZero);
                });
        }, iter: Iterations);
    }
}
