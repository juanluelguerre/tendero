using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Kits;

/// <summary>
/// Something a slot could hold: a product by its code, what the shop shows it
/// costs, and how well it answers the slot's own search.
/// </summary>
/// <param name="Relevance">
/// Points, the more the better, **from the candidate's rank and never its
/// score**: scores of two searches are not comparable, ranks are. A whole
/// number so that a sum of them is the same sum in any order — a kit must not
/// change with the order its candidates arrived in, and a sum of fractions
/// does.
/// </param>
public sealed record KitCandidate(string Code, Money Price, int Relevance);

/// <summary>One thing a kit is made of — a pan, a knife — and what could be it, in any order.</summary>
/// <param name="Required">Whether a kit without it is still that kit. A required slot is filled before any optional one.</param>
public sealed record KitSlot(string Code, bool Required, IReadOnlyList<KitCandidate> Candidates);

/// <param name="NodeCeiling">How far the search may go before it gives the best it has found and says that it stopped.</param>
public sealed record KitProblem(Money Budget, IReadOnlyList<KitSlot> Slots, int NodeCeiling = KitProblem.DefaultNodeCeiling)
{
    /// <summary>
    /// Five slots of twenty candidates are four million kits and, with the
    /// bound, under a hundred thousand nodes at their worst measured. The
    /// ceiling is for the problem nobody foresaw, so that it costs a bounded
    /// time and an honest answer.
    /// </summary>
    public const int DefaultNodeCeiling = 2_000_000;
}

public enum KitGapReason
{
    /// <summary>Nothing was left to put there: no candidate, or each one already in another slot.</summary>
    NoCandidate,

    /// <summary>There was something, and the money left did not reach the cheapest.</summary>
    OverBudget,
}

/// <summary>Why a slot is empty.</summary>
/// <param name="Cheapest">Over budget: the cheapest thing that could have gone there.</param>
/// <param name="Short">Over budget: how much more it costs than what was left.</param>
public sealed record KitGap(KitGapReason Reason, KitCandidate? Cheapest = null, Money? Short = null);

/// <summary>
/// The candidate a slot would have held with more money: the next better
/// ranked than the one chosen, and how much was missing to swap it in with
/// everything else as it is.
/// </summary>
public sealed record KitUpgrade(KitCandidate Candidate, Money Short);

/// <summary>A slot of the answer: what it holds, or why nothing; and what it would have held.</summary>
public sealed record KitSlotResult(string Slot, bool Required, KitCandidate? Chosen, KitGap? Gap, KitUpgrade? Better);

/// <summary>What held the kit back.</summary>
public enum KitBinding
{
    /// <summary>Nothing: every slot holds the best there was for it.</summary>
    None,

    /// <summary>Money: a slot is empty or holds less than the best for want of it.</summary>
    Budget,

    /// <summary>The catalogue: a slot had nothing to choose from, and money was not what was missing.</summary>
    Candidates,
}

/// <summary>
/// The kit, **and what the solver knows about its own answer**: the trace an
/// explanation is written from, and the only thing it may be written from.
/// </summary>
/// <param name="Slots">One for each slot asked, in the order asked.</param>
/// <param name="Left">The budget less the total.</param>
/// <param name="Nodes">How many nodes the search visited.</param>
/// <param name="CeilingReached">Whether it stopped at its ceiling: the kit is then the best found, not the best there is.</param>
public sealed record KitSolution(
    IReadOnlyList<KitSlotResult> Slots, Money Total, Money Left, KitBinding Binding, int Nodes, bool CeilingReached);

/// <summary>
/// One product a slot, under a budget (phase 13c, the kit solver's spec).
///
/// **Pure**: no clock, no I/O, no model — a problem in, a solution out, the
/// same one every time. That is what lets it be held to properties (never over
/// the budget, the same kit in any order, never worse for one more candidate,
/// and as good as trying every kit there is), none of which can be stated about
/// a model that picks products.
/// </summary>
public interface IKitSolver
{
    /// <exception cref="ArgumentException">The problem is not one: a budget or a price below nothing, two currencies, a slot or a candidate named twice.</exception>
    KitSolution Solve(KitProblem problem);
}

/// <summary>
/// Branch and bound, exact.
///
/// **What a kit is worth, in this order**: required slots filled; optional
/// slots filled; the sum of relevance; the lower total; and, between two kits
/// equal in all of that, the one a fixed walk meets first — by the slots' and
/// the products' codes, never by the order they arrived in — so a kit is the
/// same on two visits. Coverage comes before relevance on purpose: a kit that
/// drops its knife to afford a better pan is not a kit.
///
/// **The bound** is what the slots still to fill could add at most: as many of
/// them as their cheapest candidates allow, required first, and for each the
/// best candidate it could afford once those have paid for their cheapest. A
/// branch that cannot reach the best kit found is not walked.
/// </summary>
public sealed class BranchAndBoundKitSolver : IKitSolver
{
    public KitSolution Solve(KitProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        Refuse(problem);

        var search = new Search(problem);
        search.Run();
        return search.Solution();
    }

    private static void Refuse(KitProblem problem)
    {
        if (problem.Budget.IsNegative)
            throw new ArgumentException("A budget is not below nothing.", nameof(problem));
        if (problem.NodeCeiling < 1)
            throw new ArgumentException("The search needs at least one node.", nameof(problem));
        if (problem.Slots.Select(slot => slot.Code).Distinct(StringComparer.Ordinal).Count() != problem.Slots.Count)
            throw new ArgumentException("Two slots have the same code.", nameof(problem));

        foreach (var slot in problem.Slots)
        {
            if (slot.Candidates.Select(candidate => candidate.Code).Distinct(StringComparer.Ordinal).Count() != slot.Candidates.Count)
                throw new ArgumentException($"Slot '{slot.Code}' names a candidate twice.", nameof(problem));

            foreach (var candidate in slot.Candidates)
            {
                if (candidate.Price.Currency != problem.Budget.Currency)
                    throw new ArgumentException($"'{candidate.Code}' is priced in {candidate.Price.Currency}, the budget is in {problem.Budget.Currency}.", nameof(problem));
                if (candidate.Price.IsNegative || candidate.Relevance < 0)
                    throw new ArgumentException($"'{candidate.Code}' has a price or a relevance below nothing.", nameof(problem));
            }
        }
    }

    /// <summary>What a kit, or a part of one, is worth: compared in this order, the total the other way round.</summary>
    private readonly record struct Worth(int Required, int Optional, int Relevance)
    {
        public int CompareTo(Worth other) =>
            this.Required != other.Required ? this.Required.CompareTo(other.Required)
            : this.Optional != other.Optional ? this.Optional.CompareTo(other.Optional)
            : this.Relevance.CompareTo(other.Relevance);
    }

    private sealed class Search
    {
        private readonly KitProblem problem;

        /// <summary>
        /// The slots as the search walks them: required first, then by code, and
        /// each one's candidates best ranked first, then cheaper, then by code.
        /// **An order of its own, and it is part of the answer**: of two kits
        /// worth the same the first met is kept, so the order things arrived in
        /// changes nothing only because it is never the order walked. Best ranked
        /// first also finds a good kit early, which is what the bound prunes
        /// against.
        /// </summary>
        private readonly KitSlot[] slots;

        private readonly KitCandidate[][] candidates;

        private readonly decimal[] cheapest;

        private readonly KitCandidate?[] current;

        private readonly HashSet<string> taken = new(StringComparer.Ordinal);

        private KitCandidate?[] best;

        private Worth bestWorth;

        private decimal bestTotal;

        private int nodes;

        private bool stopped;

        public Search(KitProblem problem)
        {
            this.problem = problem;
            this.slots = [.. problem.Slots.OrderByDescending(slot => slot.Required).ThenBy(slot => slot.Code, StringComparer.Ordinal)];
            this.candidates =
            [
                .. this.slots.Select(slot => slot.Candidates
                    .OrderByDescending(candidate => candidate.Relevance)
                    .ThenBy(candidate => candidate.Price.Amount)
                    .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
                    .ToArray()),
            ];
            this.cheapest = [.. this.candidates.Select(of => of.Length == 0 ? Decimal.MaxValue : of.Min(candidate => candidate.Price.Amount))];
            this.current = new KitCandidate?[this.slots.Length];

            // The empty kit always fits: there is an answer before the first node.
            this.best = new KitCandidate?[this.slots.Length];
        }

        public void Run() => this.Walk(0, default, 0m);

        private void Walk(int depth, Worth worth, decimal total)
        {
            if (this.nodes >= this.problem.NodeCeiling)
            {
                this.stopped = true;
                return;
            }

            this.nodes++;

            if (depth == this.slots.Length)
            {
                if (this.Better(worth, total))
                    (this.best, this.bestWorth, this.bestTotal) = ([.. this.current], worth, total);
                return;
            }

            var left = this.problem.Budget.Amount - total;
            if (this.Reachable(depth, worth, left).CompareTo(this.bestWorth) < 0)
                return;

            var required = this.slots[depth].Required;
            foreach (var candidate in this.candidates[depth])
            {
                if (candidate.Price.Amount > left || !this.taken.Add(candidate.Code))
                    continue;

                this.current[depth] = candidate;
                this.Walk(
                    depth + 1,
                    required
                        ? worth with { Required = worth.Required + 1, Relevance = worth.Relevance + candidate.Relevance }
                        : worth with { Optional = worth.Optional + 1, Relevance = worth.Relevance + candidate.Relevance },
                    total + candidate.Price.Amount);
                this.current[depth] = null;
                this.taken.Remove(candidate.Code);

                if (this.stopped)
                    return;
            }

            this.Walk(depth + 1, worth, total);
        }

        private bool Better(Worth worth, decimal total)
        {
            if (worth.CompareTo(this.bestWorth) is var compared and not 0)
                return compared > 0;
            // As good and as cheap: the one the walk met first stays. The walk has an
            // order of its own, so that is always the same kit.
            return total < this.bestTotal;
        }

        /// <summary>
        /// The most the slots from <paramref name="depth"/> on could bring this
        /// kit to. Never less than what any way of filling them gives — a product
        /// already taken is counted as if it were free, which only raises it.
        /// </summary>
        private Worth Reachable(int depth, Worth worth, decimal left)
        {
            // As many slots as their cheapest candidates allow: the required ones
            // first, cheapest first, then the optional ones with what is left.
            var (required, optional, spent) = (0, 0, 0m);
            foreach (var wanted in (bool[])[true, false])
            {
                var prices = new List<decimal>();
                for (var index = depth; index < this.slots.Length; index++)
                {
                    if (this.slots[index].Required == wanted && this.cheapest[index] != Decimal.MaxValue)
                        prices.Add(this.cheapest[index]);
                }

                prices.Sort();
                foreach (var price in prices)
                {
                    if (spent + price > left)
                        break;

                    spent += price;
                    if (wanted)
                        required++;
                    else
                        optional++;
                }
            }

            // Relevance only decides between kits that fill as many slots as that,
            // and in such a kit a slot can spend what is left less the cheapest of
            // the others in it — never less than `spent` without its own cheapest,
            // since the slots counted above are the cheapest there are. A slot with
            // nothing to choose from adds nothing.
            var relevance = 0;
            for (var index = depth; index < this.slots.Length; index++)
            {
                if (this.cheapest[index] == Decimal.MaxValue)
                    continue;

                var most = left - (spent - this.cheapest[index]);
                foreach (var candidate in this.candidates[index])
                {
                    if (candidate.Price.Amount <= most)
                    {
                        // Best ranked first: the first that fits is the most it adds.
                        relevance += candidate.Relevance;
                        break;
                    }
                }
            }

            return new Worth(worth.Required + required, worth.Optional + optional, worth.Relevance + relevance);
        }

        /// <summary>
        /// A search stopped at its ceiling may have left a slot empty that money
        /// reaches: what still fits is put in, the walk's order and best ranked
        /// first. Not the best kit — it says so — but never one that reports a
        /// slot as over the budget when it is not.
        /// </summary>
        private void FillWhatFits()
        {
            var held = this.best.OfType<KitCandidate>().Select(candidate => candidate.Code).ToHashSet(StringComparer.Ordinal);
            var left = this.problem.Budget.Amount - this.best.Sum(candidate => candidate?.Price.Amount ?? 0m);

            for (var index = 0; index < this.slots.Length; index++)
            {
                if (this.best[index] is not null)
                    continue;

                var fits = this.candidates[index].FirstOrDefault(candidate => candidate.Price.Amount <= left && !held.Contains(candidate.Code));
                if (fits is null)
                    continue;

                this.best[index] = fits;
                held.Add(fits.Code);
                left -= fits.Price.Amount;
            }
        }

        public KitSolution Solution()
        {
            if (this.stopped) this.FillWhatFits();

            var currency = this.problem.Budget.Currency;
            var chosen = Enumerable.Range(0, this.slots.Length).ToDictionary(index => this.slots[index].Code, index => this.best[index]);
            var total = chosen.Values.Sum(candidate => candidate?.Price.Amount ?? 0m);
            var left = this.problem.Budget.Amount - total;
            var held = chosen.Values.OfType<KitCandidate>().Select(candidate => candidate.Code).ToHashSet(StringComparer.Ordinal);

            List<KitSlotResult> results = [];
            foreach (var slot in this.problem.Slots)
            {
                var free = slot.Candidates.Where(candidate => !held.Contains(candidate.Code)).ToList();

                if (chosen[slot.Code] is not { } product)
                {
                    var cheapestFree = free
                        .OrderBy(candidate => candidate.Price.Amount)
                        .ThenByDescending(candidate => candidate.Relevance)
                        .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
                        .FirstOrDefault();

                    results.Add(new KitSlotResult(slot.Code, slot.Required, null,
                        cheapestFree is null
                            ? new KitGap(KitGapReason.NoCandidate)
                            : new KitGap(KitGapReason.OverBudget, cheapestFree, new Money(cheapestFree.Price.Amount - left, currency)),
                        null));
                    continue;
                }

                // The next better ranked than what it holds; the cheaper of two as
                // well ranked. What was missing is what swapping it in would cost
                // over what is left — more than nothing, or it would be in the kit.
                var next = free
                    .Where(candidate => candidate.Relevance > product.Relevance)
                    .OrderBy(candidate => candidate.Relevance)
                    .ThenBy(candidate => candidate.Price.Amount)
                    .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
                    .FirstOrDefault();
                var missing = next is null ? 0m : next.Price.Amount - product.Price.Amount - left;

                results.Add(new KitSlotResult(slot.Code, slot.Required, product, null,
                    next is not null && missing > 0m ? new KitUpgrade(next, new Money(missing, currency)) : null));
            }

            var binding = results.Any(slot => slot.Gap?.Reason == KitGapReason.OverBudget || slot.Better is not null) ? KitBinding.Budget
                : results.Any(slot => slot.Gap?.Reason == KitGapReason.NoCandidate) ? KitBinding.Candidates
                : KitBinding.None;

            return new KitSolution(results, new Money(total, currency), new Money(left, currency), binding, this.nodes, this.stopped);
        }
    }
}
