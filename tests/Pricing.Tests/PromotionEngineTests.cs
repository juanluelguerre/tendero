using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Engine;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

public sealed class PromotionEngineTests
{
    private readonly CombiningPromotionEngine _engine = new();

    // ---------- The four effects ----------

    [Fact]
    public void A_percentage_comes_off_the_lines_it_reaches_and_no_others()
    {
        var cart = Build.Cart(0m, "retail",
            Build.Line("SHIRT", 24.50m, quantity: 2, categoryPath: "APPAREL/SPORTSWEAR/SHIRT"),
            Build.Line("PAN", 44.95m, categoryPath: "HOME/KITCHEN/COOKWARE"));

        var outcome = _engine.Apply(
            [Build.Promotion("APPAREL10", new PercentOffLine(10m),
                condition: new PromotionCondition(CategoryCode: "APPAREL"))],
            cart, Build.At());

        // 49,00 × 10% = 4,90 on the shirts; the pan is untouched.
        Assert.Equal(4.90m, Applied(outcome, "APPAREL10").Amount.Amount);
        Assert.Equal(44.95m, outcome.Cart.Lines.Single(line => line.Sku == "PAN").Net.Amount);
    }

    /// <summary>
    /// A promotion on KITCHEN has to reach a pan filed under COOKWARE. Matching
    /// the leaf alone would make every category promotion a promotion on exactly
    /// one shelf — the same reason the search index carries the whole branch.
    /// </summary>
    [Fact]
    public void A_category_promotion_reaches_the_whole_branch_below_it()
    {
        var cart = Build.Cart(0m, "retail",
            Build.Line("PAN", 40m, categoryPath: "HOME/KITCHEN/COOKWARE"));

        var outcome = _engine.Apply(
            [Build.Promotion("K", new PercentOffLine(50m),
                condition: new PromotionCondition(CategoryCode: "KITCHEN"))],
            cart, Build.At());

        Assert.Equal(20m, Applied(outcome, "K").Amount.Amount);
    }

    [Fact]
    public void An_order_amount_is_spread_across_lines_without_losing_a_cent()
    {
        var cart = Build.Cart(0m, "retail",
            Build.Line("A", 10m), Build.Line("B", 10m), Build.Line("C", 10m));

        var outcome = _engine.Apply(
            [Build.Promotion("TEN", new AmountOffOrder(Build.Money(10m)))], cart, Build.At());

        Assert.Equal(10m, Applied(outcome, "TEN").Amount.Amount);
        Assert.Equal([3.34m, 3.33m, 3.33m], outcome.Cart.Lines.Select(line => line.Discount.Amount));
        Assert.Equal(20m, outcome.Cart.Net.Amount);
    }

    /// <summary>
    /// The cart cannot become an income. An order-level discount larger than
    /// what is left is capped, and the reported amount is what was actually
    /// taken — not what was asked for.
    /// </summary>
    [Fact]
    public void An_order_amount_larger_than_the_cart_takes_only_what_is_there()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 8m));

        var outcome = _engine.Apply(
            [Build.Promotion("HUGE", new AmountOffOrder(Build.Money(50m)))], cart, Build.At());

        Assert.Equal(8m, Applied(outcome, "HUGE").Amount.Amount);
        Assert.Equal(0m, outcome.Cart.Net.Amount);
    }

    [Fact]
    public void Three_for_two_makes_one_of_every_three_free()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("SHIRT", 24.50m, quantity: 7));

        var outcome = _engine.Apply(
            [Build.Promotion("3X2", new BuyXGetY(2, 1))], cart, Build.At());

        // Seven units are two complete groups of three plus one loose: two free.
        Assert.Equal(49.00m, Applied(outcome, "3X2").Amount.Amount);
    }

    [Fact]
    public void Free_shipping_zeroes_the_shipping_and_nothing_else()
    {
        var cart = Build.Cart(4.95m, "retail", Build.Line("A", 60m));

        var outcome = _engine.Apply(
            [Build.Promotion("SHIP0", new FreeShipping())], cart, Build.At());

        Assert.Equal(4.95m, Applied(outcome, "SHIP0").Amount.Amount);
        Assert.True(outcome.Cart.Shipping.IsZero);
        Assert.Equal(60m, outcome.Cart.Net.Amount);
    }

    [Fact]
    public void Free_shipping_on_an_order_that_already_ships_free_is_suppressed_and_says_so()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 60m));

        var outcome = _engine.Apply(
            [Build.Promotion("SHIP0", new FreeShipping())], cart, Build.At());

        Assert.Equal(RuleReasons.NothingToDiscountCode, Suppressed(outcome, "SHIP0").Reason!.Code);
    }

    // ---------- The combination table, which is the point of the phase ----------

    /// <summary>
    /// The demo of phase 3: two discounts applied and a third suppressed, with
    /// the reason printed — "no acumulable con Rebajas de verano".
    /// </summary>
    [Fact]
    public void Two_promotions_of_one_exclusivity_group_never_both_apply()
    {
        var cart = Build.Cart(0m, "retail",
            Build.Line("SHIRT", 100m, categoryPath: "APPAREL/SPORTSWEAR/SHIRT"));

        var outcome = _engine.Apply(
        [
            Build.Promotion("SUMMER25", new PercentOffLine(25m),
                CombinationPolicy.ExclusiveInGroup, priority: 10, group: "seasonal"),
            Build.Promotion("APPAREL10", new PercentOffLine(10m),
                CombinationPolicy.ExclusiveInGroup, priority: 20, group: "seasonal")
        ], cart, Build.At());

        Assert.Equal(25m, Applied(outcome, "SUMMER25").Amount.Amount);

        var loser = Suppressed(outcome, "APPAREL10");
        Assert.Equal(RuleReasons.NotCombinableWithCode, loser.Reason!.Code);
        Assert.Equal("No acumulable con SUMMER25.", loser.Reason.Explanation.In("es"));
        Assert.Equal("Not combinable with SUMMER25.", loser.Reason.Explanation.In("en"));
    }

    [Fact]
    public void A_stackable_promotion_outside_the_group_still_applies()
    {
        var cart = Build.Cart(0m, "retail",
            Build.Line("SHIRT", 100m, categoryPath: "APPAREL/SPORTSWEAR/SHIRT"));

        var outcome = _engine.Apply(
        [
            Build.Promotion("SUMMER25", new PercentOffLine(25m),
                CombinationPolicy.ExclusiveInGroup, priority: 10, group: "seasonal"),
            Build.Promotion("APPAREL10", new PercentOffLine(10m),
                CombinationPolicy.ExclusiveInGroup, priority: 20, group: "seasonal"),
            Build.Promotion("EXTRA5", new AmountOffOrder(Build.Money(5m)),
                CombinationPolicy.Stackable, priority: 30)
        ], cart, Build.At());

        Assert.Equal(
            ["SUMMER25", "EXTRA5"],
            outcome.Discounts.Where(discount => discount.Outcome == DiscountOutcome.Applied)
                             .Select(discount => discount.PromotionCode));

        Assert.Equal(70m, outcome.Cart.Net.Amount);
    }

    [Fact]
    public void An_exclusive_global_promotion_stops_everything_below_it()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("BF", new PercentOffLine(30m), CombinationPolicy.ExclusiveGlobal, priority: 5),
            Build.Promotion("TEN", new AmountOffOrder(Build.Money(10m)),
                CombinationPolicy.Stackable, priority: 10)
        ], cart, Build.At());

        Assert.Equal(30m, Applied(outcome, "BF").Amount.Amount);
        Assert.Equal(RuleReasons.SupersededByExclusiveCode, Suppressed(outcome, "TEN").Reason!.Code);
        Assert.Equal(70m, outcome.Cart.Net.Amount);
    }

    /// <summary>
    /// "Wins over everything BELOW it" is the whole wording. A promotion with
    /// higher priority already applied when the exclusive one is reached, and it
    /// is not taken back — the alternative would make the result depend on how
    /// far the engine had got.
    /// </summary>
    [Fact]
    public void An_exclusive_global_promotion_does_not_undo_what_already_applied()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("FIRST", new AmountOffOrder(Build.Money(10m)),
                CombinationPolicy.Stackable, priority: 1),
            Build.Promotion("BF", new PercentOffLine(30m), CombinationPolicy.ExclusiveGlobal, priority: 5)
        ], cart, Build.At());

        Assert.Equal(10m, Applied(outcome, "FIRST").Amount.Amount);
        Assert.Equal(27m, Applied(outcome, "BF").Amount.Amount);   // 30% of the remaining 90,00
    }

    // ---------- Eligibility ----------

    [Fact]
    public void An_unmet_condition_is_reported_with_what_was_missing()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 10m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("BIG", new PercentOffLine(10m),
                condition: new PromotionCondition(MinimumSubtotal: Build.Money(50m)))
        ], cart, Build.At());

        var suppressed = Suppressed(outcome, "BIG");
        Assert.Equal(RuleReasons.ConditionNotMetCode, suppressed.Reason!.Code);
        Assert.Contains("50.00 EUR", suppressed.Reason.Explanation.In("en"));
    }

    /// <summary>
    /// Conditions are read against the ALREADY discounted cart. A minimum of
    /// 60 € stops being met once an earlier promotion took the cart below it,
    /// which is what anyone who has read a shop's terms expects.
    /// </summary>
    [Fact]
    public void A_minimum_is_measured_against_what_the_cart_is_worth_now()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 65m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("FIRST", new AmountOffOrder(Build.Money(10m)), priority: 1),
            Build.Promotion("SECOND", new PercentOffLine(10m), priority: 2,
                condition: new PromotionCondition(MinimumSubtotal: Build.Money(60m)))
        ], cart, Build.At());

        Assert.Equal(DiscountOutcome.Applied, Find(outcome, "FIRST").Outcome);
        Assert.Equal(RuleReasons.ConditionNotMetCode, Suppressed(outcome, "SECOND").Reason!.Code);
    }

    [Theory]
    [InlineData("2026-11-26T23:59:59Z", false)]
    [InlineData("2026-11-27T00:00:00Z", true)]
    [InlineData("2026-11-30T00:00:00Z", false)]
    public void A_promotion_outside_its_window_is_not_even_evaluated(string instant, bool expected)
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("BF", new PercentOffLine(30m), priority: 5,
                validFrom: DateTimeOffset.Parse("2026-11-27T00:00:00Z"),
                validTo: DateTimeOffset.Parse("2026-11-30T00:00:00Z"))
        ], cart, Build.At(DateTimeOffset.Parse(instant)));

        Assert.Equal(expected, outcome.Discounts.Count == 1);
    }

    /// <summary>
    /// Not merely "not applied" — absent. Showing "not applied: you are not VIP"
    /// leaks the existence of the VIP tariff to whoever does not have it, and
    /// listing every coupon promotion turns the cart into a coupon finder.
    /// </summary>
    [Fact]
    public void A_promotion_for_another_segment_or_needing_a_coupon_does_not_appear_at_all()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        var outcome = _engine.Apply(
        [
            Build.Promotion("VIPONLY", new PercentOffLine(10m), segment: "vip"),
            Build.Promotion("SECRET", new PercentOffLine(10m), coupon: "BF2026")
        ], cart, Build.At());

        Assert.Empty(outcome.Discounts);
    }

    [Fact]
    public void The_coupon_that_is_brought_unlocks_its_promotion()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        var outcome = _engine.Apply(
            [Build.Promotion("SECRET", new PercentOffLine(10m), coupon: "BF2026")],
            cart, Build.At(Build.Now, "bf2026"));

        Assert.Equal(10m, Applied(outcome, "SECRET").Amount.Amount);
    }

    // ---------- Determinism ----------

    /// <summary>
    /// Evaluation order is (Priority, Code) and nothing else. Without the code
    /// tiebreak, two promotions of equal priority would apply in whatever order
    /// the store returned them, and the same cart would total two different
    /// amounts on two requests.
    /// </summary>
    [Fact]
    public void Equal_priorities_are_broken_by_code_and_not_by_input_order()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("A", 100m));

        Promotion[] promotions =
        [
            Build.Promotion("BBB", new PercentOffLine(10m), CombinationPolicy.ExclusiveInGroup,
                priority: 10, group: "g"),
            Build.Promotion("AAA", new PercentOffLine(50m), CombinationPolicy.ExclusiveInGroup,
                priority: 10, group: "g")
        ];

        var forwards = _engine.Apply(promotions, cart, Build.At());
        var backwards = _engine.Apply([.. promotions.Reverse()], cart, Build.At());

        Assert.Equal(50m, Applied(forwards, "AAA").Amount.Amount);
        Assert.Equal(forwards.Cart.Net.Amount, backwards.Cart.Net.Amount);
    }

    private static AppliedDiscount Find(PromotionOutcome outcome, string code) =>
        outcome.Discounts.Single(discount => discount.PromotionCode == code);

    private static AppliedDiscount Applied(PromotionOutcome outcome, string code)
    {
        var discount = Find(outcome, code);
        Assert.Equal(DiscountOutcome.Applied, discount.Outcome);
        return discount;
    }

    private static AppliedDiscount Suppressed(PromotionOutcome outcome, string code)
    {
        var discount = Find(outcome, code);
        Assert.Equal(DiscountOutcome.Suppressed, discount.Outcome);
        Assert.NotNull(discount.Reason);
        return discount;
    }
}

/// <summary>
/// The combination table has to answer for every policy that exists, the same
/// way the order transition matrix is walked in full. A fourth policy added to
/// the enum without a row fails here instead of silently behaving like the
/// dictionary's miss.
/// </summary>
public sealed class CombinationTableTests
{
    [Fact]
    public void Every_combination_policy_has_exactly_one_rule()
    {
        var policies = Enum.GetValues<CombinationPolicy>();

        Assert.Equal(policies.Length, CombiningPromotionEngine.Rules.Count);
        Assert.All(policies, policy => Assert.True(CombiningPromotionEngine.Rules.ContainsKey(policy)));
    }

    /// <summary>
    /// Exactly one policy stops evaluation and exactly one closes a group. If a
    /// second one ever does either, that is a design decision and it should
    /// break a test, not slip in.
    /// </summary>
    [Fact]
    public void The_table_says_what_the_names_promise()
    {
        Assert.Single(CombiningPromotionEngine.Rules, rule => rule.Value.StopsEvaluation);
        Assert.Single(CombiningPromotionEngine.Rules, rule => rule.Value.SuppressesGroup);

        Assert.True(CombiningPromotionEngine.Rules[CombinationPolicy.ExclusiveGlobal].StopsEvaluation);
        Assert.True(CombiningPromotionEngine.Rules[CombinationPolicy.ExclusiveInGroup].SuppressesGroup);

        var stackable = CombiningPromotionEngine.Rules[CombinationPolicy.Stackable];
        Assert.False(stackable.StopsEvaluation);
        Assert.False(stackable.SuppressesGroup);
    }
}

/// <summary>
/// The bug a property test found and no example would have.
///
/// The engine used to match lines by SKU. A cart carrying the same SKU on two
/// lines — which nothing in the domain forbids — then either discounted both
/// when one was meant, or threw outright while allocating an order-level amount
/// into a dictionary keyed on SKU. It matches by position now.
///
/// The quoting slice happens to reject duplicate SKUs, so this was unreachable
/// through the API. That is exactly what makes it worth pinning: an engine that
/// is only correct because of a validator somewhere else is not correct, and the
/// validator is one refactor away from moving.
/// </summary>
public sealed class DuplicateSkuTests
{
    private readonly CombiningPromotionEngine _engine = new();

    [Fact]
    public void Two_lines_carrying_one_sku_are_two_lines()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("SAME", 10m), Build.Line("SAME", 10m));

        var outcome = _engine.Apply(
            [Build.Promotion("TEN", new AmountOffOrder(Build.Money(10m)))], cart, Build.At());

        Assert.Equal(10m, outcome.Discounts.Single().Amount.Amount);
        Assert.Equal([5m, 5m], outcome.Cart.Lines.Select(line => line.Discount.Amount));
        Assert.Equal(10m, outcome.Cart.Net.Amount);
    }

    [Fact]
    public void A_percentage_on_a_repeated_sku_is_taken_once_per_line()
    {
        var cart = Build.Cart(0m, "retail", Build.Line("SAME", 10m), Build.Line("SAME", 30m));

        var outcome = _engine.Apply(
            [Build.Promotion("TEN", new PercentOffLine(10m))], cart, Build.At());

        Assert.Equal(4m, outcome.Discounts.Single().Amount.Amount);
        Assert.Equal([1m, 3m], outcome.Cart.Lines.Select(line => line.Discount.Amount));
    }
}
