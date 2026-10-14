using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Kits;

/// <summary>
/// The kit solver on kits somebody thought of (phase 13c): the ones the spec
/// tells as stories. What is true of every kit is in
/// <see cref="KitSolverProperties"/>.
/// </summary>
public sealed class KitSolverTests
{
    private static readonly BranchAndBoundKitSolver Solver = new();

    private static KitCandidate Product(string code, decimal price, int relevance) => new(code, new Money(price, "EUR"), relevance);

    private static KitProblem Kit(decimal budget, params KitSlot[] slots) => new(new Money(budget, "EUR"), slots);

    private static string?[] Chosen(KitSolution solution) => [.. solution.Slots.Select(slot => slot.Chosen?.Code)];

    /// <summary>
    /// The spec's story. The set of knives is the better ranked and does not fit
    /// beside the pan; the chef's knife does. The board is optional and the money
    /// left does not reach the cheapest one — and each of those is said.
    /// </summary>
    [Fact]
    public void A_kitchen_kit_under_a_budget_takes_what_fits_and_says_what_did_not()
    {
        var solution = Solver.Solve(Kit(120m,
            new KitSlot("pan", true, [Product("PAN-IRON", 49.90m, 1000), Product("PAN-SET", 44.95m, 500)]),
            new KitSlot("knife", true, [Product("KNIVES-3", 119m, 1000), Product("KNIFE-CHEF", 64m, 500)]),
            new KitSlot("board", false, [Product("BOARD", 24.90m, 1000)])));

        Assert.Equal(["PAN-IRON", "KNIFE-CHEF", null], Chosen(solution));
        Assert.Equal((113.90m, 6.10m), (solution.Total.Amount, solution.Left.Amount));
        Assert.Equal(KitBinding.Budget, solution.Binding);

        // The knives it would have taken, and what was missing: 119 − 64 − 6.10.
        var knife = solution.Slots[1];
        Assert.Equal(("KNIVES-3", 48.90m), (knife.Better!.Candidate.Code, knife.Better.Short.Amount));

        // The board that did not fit: 24.90 − 6.10.
        var board = solution.Slots[2];
        Assert.Equal((KitGapReason.OverBudget, "BOARD", 18.80m), (board.Gap!.Reason, board.Gap.Cheapest!.Code, board.Gap.Short!.Value.Amount));

        // Nothing better than the pan it took.
        Assert.Null(solution.Slots[0].Better);
    }

    /// <summary>
    /// **Coverage before relevance** (the spec's second decision, against the
    /// roadmap): a kit that drops its knife to afford a better pan is not a kit.
    /// </summary>
    [Fact]
    public void A_required_slot_filled_is_worth_more_than_a_better_product_in_another()
    {
        var solution = Solver.Solve(Kit(100m,
            new KitSlot("pan", true, [Product("PAN-BEST", 90m, 1000), Product("PAN-PLAIN", 30m, 100)]),
            new KitSlot("knife", true, [Product("KNIFE", 60m, 100)])));

        Assert.Equal(["PAN-PLAIN", "KNIFE"], Chosen(solution));
    }

    /// <summary>And a required slot before any number of optional ones.</summary>
    [Fact]
    public void A_required_slot_is_filled_before_two_optional_ones()
    {
        var solution = Solver.Solve(Kit(50m,
            new KitSlot("board", false, [Product("BOARD", 25m, 1000)]),
            new KitSlot("utensils", false, [Product("UTENSILS", 25m, 1000)]),
            new KitSlot("pan", true, [Product("PAN", 50m, 100)])));

        Assert.Equal([null, null, "PAN"], Chosen(solution));
    }

    /// <summary>Of two kits as relevant as each other, the cheaper: money left is the shopper's.</summary>
    [Fact]
    public void Of_two_kits_as_good_the_cheaper_is_taken()
    {
        var solution = Solver.Solve(Kit(100m,
            new KitSlot("pan", true, [Product("PAN-DEAR", 60m, 700), Product("PAN-CHEAP", 40m, 700)])));

        Assert.Equal(["PAN-CHEAP"], Chosen(solution));
    }

    /// <summary>And of two as good and as cheap, always the same one: by code, so a kit does not change between two visits.</summary>
    [Fact]
    public void Of_two_kits_as_good_and_as_cheap_the_one_whose_codes_come_first()
    {
        var solution = Solver.Solve(Kit(100m,
            new KitSlot("pan", true, [Product("PAN-B", 40m, 700), Product("PAN-A", 40m, 700)])));

        Assert.Equal(["PAN-A"], Chosen(solution));
    }

    /// <summary>
    /// Two products that could each go in either of two slots make two kits
    /// worth the same. Which one is given is settled by the slots' and the
    /// products' codes — the same kit whichever slot the template lists first.
    /// </summary>
    [Theory]
    [InlineData("board", "tray")]
    [InlineData("tray", "board")]
    public void A_tie_between_two_slots_is_settled_by_their_codes_and_not_by_the_order_they_came_in(string first, string second)
    {
        KitCandidate[] either = [Product("OAK", 20m, 500), Product("BAMBOO", 20m, 500)];

        var solution = Solver.Solve(Kit(100m, new KitSlot(first, true, either), new KitSlot(second, true, either)));

        var held = solution.Slots.ToDictionary(slot => slot.Slot, slot => slot.Chosen!.Code);
        Assert.Equal(("BAMBOO", "OAK"), (held["board"], held["tray"]));
    }

    /// <summary>A product that is a candidate of two slots goes where it makes the better kit, and nowhere twice.</summary>
    [Fact]
    public void A_product_two_slots_want_fills_one_of_them()
    {
        var solution = Solver.Solve(Kit(200m,
            new KitSlot("pan", true, [Product("WOK", 44m, 1000)]),
            new KitSlot("pot", true, [Product("WOK", 44m, 1000), Product("POT", 69m, 400)])));

        Assert.Equal(["WOK", "POT"], Chosen(solution));
    }

    /// <summary>Nothing to choose from is a reason of its own, and not the budget's fault.</summary>
    [Fact]
    public void A_slot_with_no_candidate_says_so()
    {
        var solution = Solver.Solve(Kit(200m,
            new KitSlot("pan", true, [Product("PAN", 44m, 1000)]),
            new KitSlot("grinder", true, [])));

        Assert.Equal(KitGapReason.NoCandidate, solution.Slots[1].Gap!.Reason);
        Assert.Equal(KitBinding.Candidates, solution.Binding);
    }

    [Fact]
    public void A_kit_that_takes_the_best_of_everything_was_held_back_by_nothing()
    {
        var solution = Solver.Solve(Kit(200m, new KitSlot("pan", true, [Product("PAN", 44m, 1000), Product("PAN-2", 30m, 500)])));

        Assert.Equal((KitBinding.None, false), (solution.Binding, solution.CeilingReached));
        Assert.Equal(156m, solution.Left.Amount);
    }

    /// <summary>A template with no slots is an empty kit, not a fault: the budget is all left.</summary>
    [Fact]
    public void A_kit_of_no_slots_is_empty()
    {
        var solution = Solver.Solve(Kit(80m));

        Assert.Empty(solution.Slots);
        Assert.Equal((0m, 80m, KitBinding.None), (solution.Total.Amount, solution.Left.Amount, solution.Binding));
    }

    /// <summary>
    /// The size the spec promises, and its hard case: five slots of twenty
    /// candidates where the better ranked is the dearer and the budget reaches
    /// the best of nothing. Four million kits, solved whole in 77,693 nodes —
    /// and in 98,202 when the bound lets each slot spend all that is left
    /// instead of what the others' cheapest leave it. The number below is
    /// between the two on purpose: a bound that got looser fails here, where no
    /// answer would show it.
    /// </summary>
    [Fact]
    public void Five_slots_of_twenty_candidates_are_solved_exactly_within_the_ceiling()
    {
        var slots = Enumerable.Range(0, 5).Select(slot => new KitSlot($"slot{slot}", slot < 3,
            [.. Enumerable.Range(0, 20).Select(rank => Product($"S{slot}-P{rank:00}", 60m + slot - (rank * 2) + ((rank * 7) % 5), 1000 - (rank * 50)))])).ToArray();

        var solution = Solver.Solve(Kit(190m, slots));

        Assert.False(solution.CeilingReached);
        Assert.True(solution.Nodes < 90_000, $"{solution.Nodes} nodes");
        Assert.All(solution.Slots, slot => Assert.NotNull(slot.Chosen));
    }

    // ---------- what is not a problem at all ----------

    [Fact]
    public void A_budget_below_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(-1m)));
    }

    [Fact]
    public void A_price_in_another_currency_than_the_budgets_is_refused()
    {
        var problem = Kit(100m, new KitSlot("pan", true, [new KitCandidate("PAN", new Money(40m, "USD"), 100)]));

        Assert.Throws<ArgumentException>(() => Solver.Solve(problem));
    }

    [Fact]
    public void A_price_or_a_relevance_below_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(100m, new KitSlot("pan", true, [Product("PAN", -1m, 100)]))));
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(100m, new KitSlot("pan", true, [Product("PAN", 1m, -1)]))));
    }

    /// <summary>Two slots of one code, or a candidate twice in a slot, is a template written wrong: which of the two was meant is nobody's to guess.</summary>
    [Fact]
    public void A_slot_or_a_candidate_named_twice_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(100m, new KitSlot("pan", true, []), new KitSlot("pan", false, []))));
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(100m, new KitSlot("pan", true, [Product("PAN", 40m, 100), Product("PAN", 30m, 900)]))));
    }

    [Fact]
    public void No_problem_at_all_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => Solver.Solve(null!));

    [Fact]
    public void A_ceiling_of_no_nodes_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Solver.Solve(Kit(100m) with { NodeCeiling = 0 }));
    }
}
