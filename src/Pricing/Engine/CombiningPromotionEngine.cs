using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Engine;

/// <summary>
/// What a combination policy does to the rest of the evaluation.
///
/// Two booleans: that is the whole rule. Fitting in a table is the point — the
/// alternative is a <c>switch</c> spread through the loop, and then adding a
/// fourth policy means finding every branch instead of writing a row.
/// </summary>
internal sealed record CombinationRule(bool StopsEvaluation, bool SuppressesGroup);

/// <summary>
/// The promotions engine, with the combination rules as a DECLARATIVE TABLE —
/// the same idiom as <c>Order.AllowedTransitions</c>, which is the best idea
/// this repository already had.
///
/// Three decisions that are the substance:
///
/// **Evaluation order is total and deterministic**: <c>(Priority, Code)</c>.
/// Without the tiebreak on code, two promotions of equal priority would apply in
/// whatever order the store returned them, and the same cart would total two
/// different amounts on two requests.
///
/// **Conditions are read against the ALREADY discounted cart.** A minimum of
/// 60 € stops being met once an earlier promotion took the cart below it, which
/// is what anyone who has read a shop's terms expects. Evaluating them all
/// against the original cart sounds fairer and is worse: two discounts that
/// overlap can leave the total below zero.
///
/// **What is suppressed comes back, with its reason.** A discount that did not
/// apply and a discount of 0,00 € look identical in a total and are not the same
/// thing. This is the piece that turns the engine into something the shop can
/// explain.
/// </summary>
public sealed class CombiningPromotionEngine : IPromotionEngine
{
    /// <summary>
    /// The entire combination semantics, in three rows. A test walks the whole
    /// table and checks no policy is missing, the same way the order transition
    /// matrix is walked.
    /// </summary>
    internal static readonly IReadOnlyDictionary<CombinationPolicy, CombinationRule> Rules =
        new Dictionary<CombinationPolicy, CombinationRule>
        {
            [CombinationPolicy.ExclusiveGlobal]  = new(StopsEvaluation: true,  SuppressesGroup: false),
            [CombinationPolicy.ExclusiveInGroup] = new(StopsEvaluation: false, SuppressesGroup: true),
            [CombinationPolicy.Stackable]        = new(StopsEvaluation: false, SuppressesGroup: false)
        };

    /// <summary>
    /// One rounding policy for every amount a shopper sees, and it has a name.
    /// Away from zero is what most people mean by rounding and what several tax
    /// authorities require; banker's rounding biases less and surprises people
    /// on the receipt.
    /// </summary>
    private const Rounding Policy = Rounding.AwayFromZero;

    public PromotionOutcome Apply(
        IReadOnlyList<Promotion> promotions, PricedCart cart, PromotionContext context)
    {
        var currency = cart.Currency;
        var results = new List<AppliedDiscount>();

        // Exclusivity group -> who took the slot. The NAME is kept rather than
        // the code, because what gets shown is "not combinable with Summer
        // sale", not "not combinable with SUMMER25".
        var closedGroups = new Dictionary<string, LocalizedText>(StringComparer.OrdinalIgnoreCase);
        LocalizedText? exclusiveWinner = null;

        var current = cart;

        foreach (var promotion in Eligible(promotions, cart.Segment, context))
        {
            if (exclusiveWinner is not null)
            {
                results.Add(AppliedDiscount.Suppressed(
                    promotion, RuleReasons.SupersededByExclusive(exclusiveWinner), currency));
                continue;
            }

            if (promotion.ExclusivityGroup is { } group &&
                closedGroups.TryGetValue(group, out var groupWinner))
            {
                results.Add(AppliedDiscount.Suppressed(
                    promotion, RuleReasons.NotCombinableWith(groupWinner), currency));
                continue;
            }

            if (!IsMetBy(promotion.Condition, current))
            {
                results.Add(AppliedDiscount.Suppressed(
                    promotion, RuleReasons.ConditionNotMet(Describe(promotion.Condition)), currency));
                continue;
            }

            var (next, amount) = Charge(promotion, current);
            if (amount.IsZero)
            {
                results.Add(AppliedDiscount.Suppressed(promotion, RuleReasons.NothingToDiscount, currency));
                continue;
            }

            current = next;
            results.Add(AppliedDiscount.Applied(promotion, amount));

            var rule = Rules[promotion.Combination];
            if (rule.SuppressesGroup && promotion.ExclusivityGroup is { } won)
                closedGroups[won] = promotion.Name;

            if (rule.StopsEvaluation)
                exclusiveWinner = promotion.Name;
        }

        return new PromotionOutcome(current, results);
    }

    /// <summary>
    /// In force, for this segment, and with their coupon if they ask for one —
    /// in total order.
    ///
    /// What fails these does not appear in the result at all, and that is
    /// deliberate: showing "not applied: not your segment" leaks the existence of
    /// the VIP tariff to whoever does not have it, and listing every coupon
    /// promotion turns the cart into a coupon finder.
    /// </summary>
    private static IEnumerable<Promotion> Eligible(
        IReadOnlyList<Promotion> promotions, string segment, PromotionContext context) =>
        promotions
            .Where(promotion => promotion.IsActiveAt(context.At)
                             && promotion.Serves(segment)
                             && promotion.CouponSatisfiedBy(context.Coupons))
            .OrderBy(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Code, StringComparer.Ordinal);

    private static bool IsMetBy(PromotionCondition condition, PricedCart cart)
    {
        var reached = Reached(condition, cart);
        if (reached.Count == 0)
            return false;

        if (condition.MinimumSubtotal is { } minimum && cart.Net < minimum)
            return false;

        return condition.MinimumQuantity is not { } quantity
            || reached.Sum(index => cart.Lines[index].Quantity) >= quantity;
    }

    /// <summary>
    /// Which lines an effect reaches, BY POSITION.
    ///
    /// Positions and not SKUs, and that distinction was found by a property test
    /// rather than by thinking: a cart may legitimately carry the same SKU on
    /// two lines, and an engine that indexed by SKU discounted both when one was
    /// meant, or threw outright while allocating an order-level amount. The
    /// quoting slice happens to forbid duplicate SKUs today — but an engine that
    /// is only correct because of a validator somewhere else is not correct.
    /// </summary>
    private static IReadOnlyList<int> Reached(PromotionCondition condition, PricedCart cart) =>
        [.. cart.Lines
            .Select((line, index) => (line, index))
            .Where(entry => condition.Reaches(entry.line.Sku, entry.line.CategoryPath))
            .Select(entry => entry.index)];

    /// <summary>
    /// The arithmetic of the four effects, in one place and in plain sight.
    /// Each branch returns the resulting cart and what was ACTUALLY taken, which
    /// is not always what was asked for: a line never drops below zero.
    /// </summary>
    private static (PricedCart Cart, Money Amount) Charge(Promotion promotion, PricedCart cart)
    {
        var reached = Reached(promotion.Condition, cart);
        var currency = cart.Currency;

        switch (promotion.Effect)
        {
            case PercentOffLine(var percent):
                return ChargePerLine(cart, reached, (line, _) => line.Net.Percent(percent).Round(Policy));

            case BuyXGetY(var buy, var free):
                return ChargePerLine(cart, reached, (line, _) =>
                    (line.UnitPrice * (line.Quantity / (buy + free) * free)).Round(Policy));

            case AmountOffOrder(var requested):
                return ChargeOrderAmount(cart, reached, requested);

            case FreeShipping:
                return cart.Shipping.IsZero
                    ? (cart, Money.Zero(currency))
                    : (cart with { Shipping = Money.Zero(currency) }, cart.Shipping);

            default:
                // An effect the engine does not know is not silently worth
                // zero: a zero looks exactly like a promotion that simply did
                // not apply.
                throw new NotSupportedException(
                    $"Promotion '{promotion.Code}' carries effect '{promotion.Effect.Kind}', which the engine does not know.");
        }
    }

    /// <summary>
    /// A flat order amount, spread across the lines it reaches.
    ///
    /// This is where <c>Money.Allocate</c> earns its place: splitting 10,00 €
    /// across three equal lines loses a cent if it is improvised, and that cent
    /// ends up as an invoice that does not balance.
    /// </summary>
    private static (PricedCart Cart, Money Amount) ChargeOrderAmount(
        PricedCart cart, IReadOnlyList<int> reached, Money requested)
    {
        var currency = cart.Currency;
        var room = reached.Aggregate(Money.Zero(currency), (total, index) => total + cart.Lines[index].Net);

        if (room.IsZero || requested.IsZero)
            return (cart, Money.Zero(currency));

        var capped = (requested > room ? room : requested).Round(Policy);
        var shares = capped.Allocate([.. reached.Select(index => cart.Lines[index].Net.Amount)]);

        // The share is looked up by the line's place in `reached`, not by its
        // SKU. Two lines carrying one SKU would have collided in a dictionary,
        // and a cart is perfectly entitled to carry one.
        var byPosition = reached
            .Select((lineIndex, position) => (lineIndex, share: shares[position]))
            .ToDictionary(pair => pair.lineIndex, pair => pair.share);

        return ChargePerLine(cart, reached, (_, index) => byPosition[index]);
    }

    /// <summary>
    /// Applies a discount line by line and returns what actually landed. The
    /// subtraction between the new discount and the old is not a detour: the cap
    /// in <c>PricedLine.Discounted</c> can trim what was asked for, and claiming
    /// 5 € came off when only 2 € fitted breaks the total.
    /// </summary>
    private static (PricedCart Cart, Money Amount) ChargePerLine(
        PricedCart cart, IReadOnlyList<int> reached, Func<PricedLine, int, Money> amountFor)
    {
        var targeted = reached.ToHashSet();
        var applied = Money.Zero(cart.Currency);
        var lines = new List<PricedLine>(cart.Lines.Count);

        for (var index = 0; index < cart.Lines.Count; index++)
        {
            var line = cart.Lines[index];

            if (!targeted.Contains(index))
            {
                lines.Add(line);
                continue;
            }

            var discounted = line.Discounted(amountFor(line, index));
            applied += discounted.Discount - line.Discount;
            lines.Add(discounted);
        }

        return (cart.WithLines(lines), applied);
    }

    /// <summary>
    /// The condition in words, so the suppression reason says something. It is
    /// user-facing text, so it exists in both cultures (invariant 6).
    /// </summary>
    private static LocalizedText Describe(PromotionCondition condition)
    {
        List<string> es = [];
        List<string> en = [];

        if (condition.MinimumSubtotal is { } minimum)
        {
            es.Add($"un mínimo de {minimum}");
            en.Add($"a minimum of {minimum}");
        }

        if (condition.MinimumQuantity is { } quantity)
        {
            es.Add($"al menos {quantity} unidades");
            en.Add($"at least {quantity} units");
        }

        if (condition.CategoryCode is { } category)
        {
            es.Add($"artículos de {category}");
            en.Add($"items from {category}");
        }

        if (condition.Sku is { } sku)
        {
            es.Add($"el artículo {sku}");
            en.Add($"the item {sku}");
        }

        return es.Count == 0
            ? RuleReasons.Both("artículos en el carrito", "items in the cart")
            : RuleReasons.Both(string.Join(" y ", es), string.Join(" and ", en));
    }
}
