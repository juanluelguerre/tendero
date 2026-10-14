using CsCheck;
using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Kits;

/// <summary>
/// What is true of every kit the solver builds (phase 13c, the kit solver's
/// spec, *Measures*). The examples in <see cref="KitSolverTests"/> say what
/// happens for a handful of kits somebody thought of; these say what a shopper
/// is promised whatever the catalogue holds — and none of them can be stated
/// about a model that picks products.
///
/// **The one that matters most is the oracle.** Branch and bound is an
/// optimisation only while it finds what trying everything finds; a bound that
/// prunes a branch it should not have is a heuristic that looks like a solver,
/// and no example would show it.
/// </summary>
public sealed class KitSolverProperties
{
    private static readonly BranchAndBoundKitSolver Solver = new();

    private const int Iterations = 2_000;

    private const string Euro = "EUR";

    // ---------- Generators ----------

    /// <summary>Few codes on purpose: the same product is then a candidate of two slots, which is where "never twice" is tested.</summary>
    private static readonly Gen<KitCandidate> GenCandidate =
        Gen.Select(Gen.Int[0, 9], Gen.Int[1, 60], Gen.Int[0, 3], Gen.Int[0, 10],
            (code, euros, quarters, rank) => new KitCandidate($"P{code}", new Money(euros + (quarters * 0.25m), Euro), rank * 100));

    private static readonly Gen<KitSlot> GenSlot =
        Gen.Select(Gen.Int[0, 999], Gen.Bool, GenCandidate.Array[0, 5],
            (code, required, candidates) => new KitSlot(
                $"slot{code}", required, [.. candidates.DistinctBy(candidate => candidate.Code)]));

    private static readonly Gen<KitProblem> GenProblem =
        Gen.Select(Gen.Int[0, 150], GenSlot.Array[0, 4],
            (budget, slots) => new KitProblem(new Money(budget, Euro), [.. slots.DistinctBy(slot => slot.Code)]));

    // ---------- What a solution is worth, written again here and not taken from the solver ----------

    private static (int Required, int Optional, int Relevance, decimal Total) Worth(KitProblem problem, IReadOnlyDictionary<string, KitCandidate?> chosen)
    {
        var filled = problem.Slots.Where(slot => chosen[slot.Code] is not null).ToList();
        return (
            filled.Count(slot => slot.Required),
            filled.Count(slot => !slot.Required),
            filled.Sum(slot => chosen[slot.Code]!.Relevance),
            filled.Sum(slot => chosen[slot.Code]!.Price.Amount));
    }

    /// <summary>Better is: more required slots, then more optional, then more relevance, then cheaper.</summary>
    private static int Compare((int Required, int Optional, int Relevance, decimal Total) one, (int Required, int Optional, int Relevance, decimal Total) other) =>
        (one.Required, one.Optional, one.Relevance).CompareTo((other.Required, other.Optional, other.Relevance)) is var first and not 0
            ? first
            : other.Total.CompareTo(one.Total);

    private static Dictionary<string, KitCandidate?> ChosenBySlot(KitSolution solution) =>
        solution.Slots.ToDictionary(slot => slot.Slot, slot => slot.Chosen);

    /// <summary>Every kit there is — each slot empty or holding one of its candidates — that fits the budget and names no product twice.</summary>
    private static IEnumerable<Dictionary<string, KitCandidate?>> Everything(KitProblem problem)
    {
        IEnumerable<List<KitCandidate?>> kits = [[]];
        foreach (var slot in problem.Slots)
        {
            var options = slot.Candidates.Cast<KitCandidate?>().Append(null).ToList();
            kits = kits.SelectMany(kit => options.Select(option => (List<KitCandidate?>)[.. kit, option])).ToList();
        }

        foreach (var kit in kits)
        {
            var held = kit.OfType<KitCandidate>().ToList();
            if (held.Sum(candidate => candidate.Price.Amount) > problem.Budget.Amount)
                continue;
            if (held.Select(candidate => candidate.Code).Distinct().Count() != held.Count)
                continue;

            yield return problem.Slots.Select((slot, index) => (slot.Code, Candidate: kit[index]))
                .ToDictionary(pair => pair.Code, pair => pair.Candidate);
        }
    }

    // ---------- Properties ----------

    [Fact]
    public void A_kit_never_costs_more_than_the_budget_and_its_total_is_what_its_products_cost()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);

            var total = solution.Slots.Sum(slot => slot.Chosen?.Price.Amount ?? 0m);
            Assert.Equal(total, solution.Total.Amount);
            Assert.True(solution.Total.Amount <= problem.Budget.Amount);
            Assert.Equal(problem.Budget.Amount - total, solution.Left.Amount);
        }, iter: Iterations);
    }

    [Fact]
    public void No_product_is_in_a_kit_twice_and_each_is_one_of_its_slots_candidates()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);

            var chosen = solution.Slots.Select(slot => slot.Chosen).OfType<KitCandidate>().ToList();
            Assert.Equal(chosen.Count, chosen.Select(candidate => candidate.Code).Distinct().Count());
            Assert.All(solution.Slots.Where(slot => slot.Chosen is not null), slot =>
                Assert.Contains(slot.Chosen, problem.Slots.Single(given => given.Code == slot.Slot).Candidates));
        }, iter: Iterations);
    }

    /// <summary>The slots come back as they were given, one each: a template's order is the page's.</summary>
    [Fact]
    public void Every_slot_is_answered_in_the_order_it_was_asked()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);

            Assert.Equal(
                problem.Slots.Select(slot => (slot.Code, slot.Required)),
                solution.Slots.Select(slot => (slot.Slot, slot.Required)));
        }, iter: Iterations);
    }

    /// <summary>
    /// A slot is filled, or says why not — and the reason is true: *no candidate*
    /// only when nothing was left to put there, *over budget* with the cheapest
    /// thing that could have gone there and exactly how much was missing.
    /// </summary>
    [Fact]
    public void An_empty_slot_says_why_and_what_it_says_is_true()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);
            var taken = solution.Slots.Select(slot => slot.Chosen?.Code).OfType<string>().ToHashSet();

            foreach (var slot in solution.Slots)
            {
                var free = problem.Slots.Single(given => given.Code == slot.Slot).Candidates
                    .Where(candidate => !taken.Contains(candidate.Code)).ToList();

                if (slot.Chosen is not null)
                {
                    Assert.Null(slot.Gap);
                    continue;
                }

                var gap = Assert.IsType<KitGap>(slot.Gap);
                if (free.Count == 0)
                {
                    Assert.Equal((KitGapReason.NoCandidate, null, null), (gap.Reason, gap.Cheapest, gap.Short));
                    continue;
                }

                Assert.Equal(KitGapReason.OverBudget, gap.Reason);
                Assert.Equal(free.Min(candidate => candidate.Price.Amount), gap.Cheapest!.Price.Amount);
                Assert.Equal(gap.Cheapest.Price.Amount - solution.Left.Amount, gap.Short!.Value.Amount);
                Assert.True(gap.Short.Value.Amount > 0m);
            }
        }, iter: Iterations);
    }

    /// <summary>
    /// **Exact.** Of every kit that fits, none is worth more than the one the
    /// solver gave — tried one by one, the slow way, with the order of worth
    /// written here a second time.
    /// </summary>
    [Fact]
    public void No_kit_that_fits_is_better_than_the_one_it_found()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);
            Assert.False(solution.CeilingReached);
            var found = Worth(problem, ChosenBySlot(solution));

            var best = Everything(problem).Select(kit => Worth(problem, kit)).Aggregate((one, other) => Compare(one, other) >= 0 ? one : other);

            Assert.Equal(best, found);
        }, iter: Iterations);
    }

    /// <summary>The same kit whatever order its slots and their candidates arrive in: a search's order is not a reason.</summary>
    [Fact]
    public void The_order_things_arrive_in_never_changes_the_kit()
    {
        Gen.Select(GenProblem, Gen.Int).Sample((problem, seed) =>
        {
            var random = new Random(seed);
            var shuffled = problem with
            {
                Slots = [.. problem.Slots.OrderBy(_ => random.Next()).Select(slot => slot with { Candidates = [.. slot.Candidates.OrderBy(_ => random.Next())] })],
            };

            var one = ChosenBySlot(Solver.Solve(problem));
            var other = ChosenBySlot(Solver.Solve(shuffled));

            Assert.Equal(one.OrderBy(pair => pair.Key), other.OrderBy(pair => pair.Key));
        }, iter: Iterations);
    }

    [Fact]
    public void The_same_problem_is_the_same_solution()
    {
        GenProblem.Sample(problem => Assert.Equivalent(Solver.Solve(problem), Solver.Solve(problem), strict: true), iter: Iterations);
    }

    /// <summary>One more thing to choose from never makes the kit worse: a shop that adds a product does not lose a recommendation.</summary>
    [Fact]
    public void Adding_a_candidate_never_makes_the_kit_worse()
    {
        Gen.Select(GenProblem.Where(problem => problem.Slots.Count > 0), GenCandidate, Gen.Int[0, 3]).Sample((problem, extra, at) =>
        {
            var slot = problem.Slots[at % problem.Slots.Count];
            if (slot.Candidates.Any(candidate => candidate.Code == extra.Code))
                return;

            var more = problem with
            {
                Slots = [.. problem.Slots.Select(given => given == slot ? given with { Candidates = [.. given.Candidates, extra] } : given)],
            };

            var before = Worth(problem, ChosenBySlot(Solver.Solve(problem)));
            var after = Worth(more, ChosenBySlot(Solver.Solve(more)));

            Assert.True(Compare(after, before) >= 0, $"before {before}, after {after}");
        }, iter: Iterations);
    }

    [Fact]
    public void More_money_never_makes_the_kit_worse()
    {
        Gen.Select(GenProblem, Gen.Int[0, 40]).Sample((problem, more) =>
        {
            var richer = problem with { Budget = new Money(problem.Budget.Amount + more, Euro) };

            var before = Worth(problem, ChosenBySlot(Solver.Solve(problem)));
            var after = Worth(richer, ChosenBySlot(Solver.Solve(richer)));

            Assert.True((after.Required, after.Optional, after.Relevance).CompareTo((before.Required, before.Optional, before.Relevance)) >= 0,
                $"before {before}, after {after}");
        }, iter: Iterations);
    }

    /// <summary>
    /// **The trace is true.** *It would have taken X with N more* is what the
    /// explanation is written from, so it is held to the solver itself: X is
    /// better ranked than what was chosen, N is more than nothing, N is exactly
    /// what swapping it in would have cost over what was left — and with N more
    /// the kit is at least as relevant as that swap.
    /// </summary>
    [Fact]
    public void What_it_would_have_taken_with_more_money_is_true()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);
            var taken = solution.Slots.Select(slot => slot.Chosen?.Code).OfType<string>().ToHashSet();
            var worth = Worth(problem, ChosenBySlot(solution));

            foreach (var slot in solution.Slots)
            {
                var better = problem.Slots.Single(given => given.Code == slot.Slot).Candidates
                    .Where(candidate => slot.Chosen is not null && candidate.Relevance > slot.Chosen.Relevance && !taken.Contains(candidate.Code))
                    .ToList();

                if (better.Count == 0)
                {
                    Assert.Null(slot.Better);
                    continue;
                }

                var upgrade = Assert.IsType<KitUpgrade>(slot.Better);
                Assert.Contains(upgrade.Candidate, better);
                Assert.Equal(better.Min(candidate => candidate.Relevance), upgrade.Candidate.Relevance);
                Assert.Equal(upgrade.Candidate.Price.Amount - slot.Chosen!.Price.Amount - solution.Left.Amount, upgrade.Short.Amount);
                Assert.True(upgrade.Short.Amount > 0m);

                var richer = Solver.Solve(problem with { Budget = problem.Budget + upgrade.Short });
                var after = Worth(problem, ChosenBySlot(richer));
                var swapped = (worth.Required, worth.Optional, worth.Relevance - slot.Chosen.Relevance + upgrade.Candidate.Relevance);
                Assert.True((after.Required, after.Optional, after.Relevance).CompareTo(swapped) >= 0, $"swapped {swapped}, after {after}");
            }
        }, iter: Iterations);
    }

    /// <summary>What held the kit back is said, and is one thing: money, nothing to choose from, or nothing at all.</summary>
    [Fact]
    public void What_was_binding_is_what_the_slots_say()
    {
        GenProblem.Sample(problem =>
        {
            var solution = Solver.Solve(problem);

            var money = solution.Slots.Any(slot => slot.Gap?.Reason == KitGapReason.OverBudget || slot.Better is not null);
            var nothing = solution.Slots.Any(slot => slot.Gap?.Reason == KitGapReason.NoCandidate);

            Assert.Equal(money ? KitBinding.Budget : nothing ? KitBinding.Candidates : KitBinding.None, solution.Binding);
        }, iter: Iterations);
    }

    /// <summary>
    /// A solver stopped at its ceiling says so, and what it gives is still a kit
    /// that can be bought: under the budget, no product twice. It is *the best
    /// found*, never passed off as the best there is.
    /// </summary>
    [Fact]
    public void A_solver_stopped_at_its_ceiling_says_so_and_still_gives_a_kit_that_fits()
    {
        Gen.Select(GenProblem, Gen.Int[1, 6]).Sample((problem, ceiling) =>
        {
            var solution = Solver.Solve(problem with { NodeCeiling = ceiling });

            Assert.True(solution.Nodes <= ceiling);
            Assert.True(solution.Total.Amount <= problem.Budget.Amount);
            var chosen = solution.Slots.Select(slot => slot.Chosen?.Code).OfType<string>().ToList();
            Assert.Equal(chosen.Count, chosen.Distinct().Count());

            // What it says of an empty slot is still true: the money left does not reach it.
            Assert.All(solution.Slots.Where(slot => slot.Gap?.Reason == KitGapReason.OverBudget), slot =>
                Assert.True(slot.Gap!.Short!.Value.Amount > 0m));
            Assert.All(solution.Slots.Where(slot => slot.Better is not null), slot => Assert.True(slot.Better!.Short.Amount > 0m));

            if (!solution.CeilingReached)
                Assert.Equivalent(Solver.Solve(problem), solution, strict: true);
        }, iter: Iterations);
    }
}
