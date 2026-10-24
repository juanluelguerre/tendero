using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ProposePromotions;

/// <param name="Culture">The language the notes are said in, picked as every endpoint picks it (ADR 0013).</param>
/// <param name="Rehearse">Read the source and answer what would be proposed, with no file written (phase 16, P16-13's rule).</param>
public sealed record ProposePromotionsCommand(string Source, string Culture = "es", bool Rehearse = false) : ICommand<ProposePromotionsResult>;

/// <param name="Proposed">The promotions written to the proposal, in the committed file's terms.</param>
/// <param name="Left">The discounts not proposed; each is a note saying why.</param>
/// <param name="File">The proposal's file name, beside the committed promotions; null when nothing was written.</param>
public sealed record ProposePromotionsResult(
    IReadOnlyList<ProposedPromotionView> Proposed, int Left, string? File, IReadOnlyList<Message<DiscountNoteCode>> Notes);

public sealed record ProposedPromotionView(
    string Code,
    IReadOnlyDictionary<string, string> Name,
    string Effect,
    CombinationPolicy Combination,
    int Priority,
    string? CouponCode,
    string? CategoryCode,
    string? Sku,
    Money? MinimumSubtotal,
    int? MinimumQuantity,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);

public sealed class ProposePromotionsValidator : AbstractValidator<ProposePromotionsCommand>
{
    /// <summary>The source must exist, not just look like one: the catalogue's lesson.</summary>
    public ProposePromotionsValidator(IDiscountSourceRegistry sources)
    {
        RuleFor(command => command.Source)
            .NotEmpty()
            .Must(source => sources.Sources.Contains(source, StringComparer.Ordinal))
            .WithMessage(command =>
                $"Unknown discount source '{command.Source}'. Registered sources: {string.Join(", ", sources.Sources)}.");
    }
}

public sealed class ProposePromotionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/pricing/promotions/proposal  { "source": "shopify" }, or with "rehearse": true
        app.MapPost("/api/pricing/promotions/proposal",
                async Task<Ok<ProposePromotionsResult>> (
                    ProposePromotionsCommand command, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await dispatcher.SendAsync(command with { Culture = language }, ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result);
                })
            // Shopkeeper only: a store's discounts carry its coupon codes.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Pricing")
            .WithName("ProposePromotions");
    }
}

/// <summary>
/// A store's discounts as a proposal of promotions (phase 16, S2b, P16-19; spec
/// decision 5). Pricing has no writer of promotions, so the proposal <em>is</em>
/// the migration: a file in the committed one's shape, written beside it, that a
/// person reads and commits.
///
/// Each discount the engine can express becomes the promotion it would be: its
/// effect, its target as one category or one SKU, its minimums, its dates, its
/// first code as the coupon, after every promotion already committed. A proposal
/// is narrowed, never widened: a discount that combined with only some others
/// applies alone, and a second code is dropped, each said. A discount whose
/// restriction the engine cannot hold — a usage limit, some customers, some
/// countries — is not proposed at all, because without it the shop would give
/// away more than the store did. That, too, is said.
/// </summary>
public sealed class ProposePromotionsHandler(
    IDiscountSourceRegistry sources,
    IPromotionReader committed,
    IPromotionProposals proposals)
    : ICommandHandler<ProposePromotionsCommand, ProposePromotionsResult>
{
    /// <summary>The distance between two proposed promotions' priorities, as the committed file keeps them.</summary>
    private const int PriorityStep = 10;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<ProposePromotionsResult> HandleAsync(ProposePromotionsCommand command, CancellationToken cancellationToken)
    {
        var source = sources.Get(command.Source);

        using var activity = Telemetry.StartActivity("pricing.propose_promotions");
        activity?.SetTag("catalog.source", source.Source);
        activity?.SetTag("pricing.proposal.rehearsal", command.Rehearse);

        var shop = await committed.AllAsync(cancellationToken);
        var codes = shop.Select(promotion => promotion.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var priority = shop.Count == 0 ? 0 : shop.Max(promotion => promotion.Priority);

        var notes = new List<DiscountNote>();
        var proposed = new List<Promotion>();
        var left = 0;

        await foreach (var discount in source.StreamDiscountsAsync(cancellationToken))
        {
            if (Refusal(discount, codes) is { } refused)
            {
                notes.Add(refused);
                left++;
                continue;
            }

            priority += PriorityStep;
            var code = Unique(CodeOf(discount), codes);
            proposed.Add(Promotion(discount, code, priority, notes));
        }

        string? file = null;
        if (!command.Rehearse && proposed.Count > 0)
            file = await proposals.WriteAsync(proposed, cancellationToken);

        activity?.SetTag("pricing.proposal.proposed", proposed.Count);
        activity?.SetTag("pricing.proposal.left", left);
        if (!command.Rehearse)
            ProposePromotionsMetrics.Record(source.Source, proposed.Count, left, notes);

        var english = command.Culture == "en";
        return new ProposePromotionsResult(
            [.. proposed.Select(View)], left, file, [.. notes.Select(note => ProposePromotionsNotes.Said(note, english))]);
    }

    /// <summary>Why a discount is not proposed, or null when the engine can express it.</summary>
    private static DiscountNote? Refusal(SourceDiscountRule discount, IReadOnlySet<string> committed)
    {
        DiscountNote Note(DiscountNoteCode code, string? detail = null) => new(code, discount.Title, detail);

        if (discount.Expired)
            return Note(DiscountNoteCode.Expired);

        if (discount.Codes.FirstOrDefault(committed.Contains) is { } taken)
            return Note(DiscountNoteCode.AlreadyHere, taken);

        // One is enough to leave it; the first the store named is the one said.
        if (discount.Restrictions.Count > 0)
        {
            var restriction = discount.Restrictions[0];
            return Note(restriction switch
            {
                SourceDiscountRestriction.UsageLimit or SourceDiscountRestriction.OncePerCustomer or SourceDiscountRestriction.UsesPerOrder
                    => DiscountNoteCode.LimitNotExpressible,
                SourceDiscountRestriction.SomeCustomers => DiscountNoteCode.CustomersNotExpressible,
                SourceDiscountRestriction.SomeCountries or SourceDiscountRestriction.MaximumShippingPrice or SourceDiscountRestriction.SubscriptionsOnly
                    => DiscountNoteCode.ConditionNotExpressible,
            }, restriction.ToString());
        }

        var expressible = discount.Effect switch
        {
            SourcePercentOff or SourceFreeShipping => true,
            SourceAmountOff(_, var onEachItem) => !onEachItem,
            SourceBuyXGetY(_, _, var percent, var sameItems) => percent == 100m && sameItems,
            SourceOtherEffect => false,
        };
        if (!expressible)
            return Note(DiscountNoteCode.EffectNotExpressible, EffectName(discount.Effect));

        // Free shipping is the order's, whatever the store said it reached.
        if (discount.Effect is SourceFreeShipping)
            return null;

        return discount.Target switch
        {
            AllItems or CategoryItems or SkuItems { Skus.Count: 1 } => null,
            SkuItems(var skus) => Note(DiscountNoteCode.TargetNotExpressible, string.Join(", ", skus)),
            UnplacedItems(var what) => Note(DiscountNoteCode.TargetNotExpressible, what),
        };
    }

    /// <summary>The promotion a discount the engine can express becomes; what was narrowed on the way, said.</summary>
    private static Promotion Promotion(SourceDiscountRule discount, string code, int priority, List<DiscountNote> notes)
    {
        if (discount.Combination == SourceCombination.Some)
            notes.Add(new DiscountNote(DiscountNoteCode.CombinationNarrowed, discount.Title, null));
        if (discount.Codes.Count > 1)
            notes.Add(new DiscountNote(DiscountNoteCode.CodesNarrowed, discount.Title, string.Join(", ", discount.Codes.Skip(1))));

        PromotionEffect effect = discount.Effect switch
        {
            SourcePercentOff(var percent) => new PercentOffLine(percent),
            SourceAmountOff(var amount, _) => new AmountOffOrder(amount),
            SourceBuyXGetY(var buy, var get, _, _) => new BuyXGetY(buy, get),
            SourceFreeShipping => new FreeShipping(),
            SourceOtherEffect => throw new UnreachableException("An effect the port has no case for is refused before it is translated."),
        };

        var target = effect is FreeShipping ? new AllItems() : discount.Target;
        var condition = new PromotionCondition(
            discount.MinimumSubtotal,
            (target as CategoryItems)?.CategoryCode,
            (target as SkuItems)?.Skus[0],
            // The engine's deal counts the units it is taken on (3 for 2 needs 3).
            effect is BuyXGetY deal ? deal.Buy + deal.Free : discount.MinimumQuantity);

        return new Promotion(
            code,
            LocalizedText.From(discount.Language, discount.Title),
            condition,
            effect,
            discount.Combination == SourceCombination.Everything ? CombinationPolicy.Stackable : CombinationPolicy.ExclusiveGlobal,
            priority,
            CouponCode: discount.Codes.Count > 0 ? discount.Codes[0] : null,
            ValidFrom: discount.StartsAt,
            ValidTo: discount.EndsAt);
    }

    /// <summary>A coupon is its own code; an automatic discount is named for its title, or its id when the title has no letters.</summary>
    private static string CodeOf(SourceDiscountRule discount) =>
        (discount.Codes.Count > 0 ? discount.Codes[0]
            : Slugs.Of(discount.Title) is { Length: > 0 } slug ? slug
            : $"discount-{discount.ExternalId}").ToUpperInvariant();

    /// <summary>A code nobody uses yet: two discounts with one title are two promotions.</summary>
    private static string Unique(string code, HashSet<string> taken)
    {
        var candidate = code;
        for (var suffix = 2; !taken.Add(candidate); suffix++)
            candidate = $"{code}-{suffix}";
        return candidate;
    }

    private static string EffectName(SourceDiscountEffect effect) => effect switch
    {
        SourcePercentOff => "percent-off",
        SourceAmountOff => "amount-off-each-item",
        SourceBuyXGetY(var buy, var get, var percent, var same) => same ? $"buy {buy} get {get} at {percent}% off" : $"buy {buy} get {get} of other items",
        SourceFreeShipping => "free-shipping",
        SourceOtherEffect(var kind) => kind,
    };

    private static ProposedPromotionView View(Promotion promotion) => new(
        promotion.Code,
        promotion.Name.Values,
        promotion.Effect.Kind,
        promotion.Combination,
        promotion.Priority,
        promotion.CouponCode,
        promotion.Condition.CategoryCode,
        promotion.Condition.Sku,
        promotion.Condition.MinimumSubtotal,
        promotion.Condition.MinimumQuantity,
        promotion.ValidFrom,
        promotion.ValidTo);
}

/// <summary>A discount note as the answer says it: the code, the discount's title, and the sentence in the language asked.</summary>
internal static class ProposePromotionsNotes
{
    public static Message<DiscountNoteCode> Said(DiscountNote note, bool english) =>
        new(note.Code, note.About, Sentences.StartingUpper(Sentence(note, english)));

    private static string Sentence(DiscountNote note, bool english)
    {
        var about = $"{note.About}: ";
        var detail = note.Detail ?? "";

        return note.Code switch
        {
            DiscountNoteCode.Expired => english
                ? $"{about}it has ended in the store; it was not proposed."
                : $"{about}ya ha terminado en la tienda; no se ha propuesto.",
            DiscountNoteCode.AlreadyHere => english
                ? $"{about}a promotion with the code {detail} is already committed; it was not proposed."
                : $"{about}ya hay una promoción con el código {detail}; no se ha propuesto.",
            DiscountNoteCode.LimitNotExpressible => english
                ? $"{about}it is limited in use ({detail}), which promotions cannot be; without the limit it would give away more, so it was not proposed."
                : $"{about}tiene un límite de uso ({detail}) que las promociones no admiten; sin él regalaría más, así que no se ha propuesto.",
            DiscountNoteCode.CustomersNotExpressible => english
                ? $"{about}only some customers get it, and the store's customers are not the shop's segments; it was not proposed."
                : $"{about}solo lo tienen algunos clientes, y los de la tienda no son los segmentos de este comercio; no se ha propuesto.",
            DiscountNoteCode.ConditionNotExpressible => english
                ? $"{about}it has a condition promotions have no field for ({detail}); it was not proposed."
                : $"{about}tiene una condición que las promociones no recogen ({detail}); no se ha propuesto.",
            DiscountNoteCode.TargetNotExpressible => english
                ? $"{about}what it reaches ({detail}) is not one category or one SKU here; it was not proposed."
                : $"{about}a lo que se aplica ({detail}) no es aquí una categoría ni un SKU; no se ha propuesto.",
            DiscountNoteCode.EffectNotExpressible => english
                ? $"{about}what it takes off ({detail}) is not one of the promotions' effects; it was not proposed."
                : $"{about}lo que descuenta ({detail}) no es ninguno de los efectos de las promociones; no se ha propuesto.",
            DiscountNoteCode.CombinationNarrowed => english
                ? $"{about}it combined with some discounts in the store; the proposal applies it alone."
                : $"{about}en la tienda se combinaba con algunos descuentos; la propuesta lo aplica solo.",
            DiscountNoteCode.CodesNarrowed => english
                ? $"{about}it had several codes; the proposal keeps the first and not {detail}."
                : $"{about}tenía varios códigos; la propuesta conserva el primero y no {detail}.",
        };
    }
}

/// <summary>
/// `tendero.pricing.import.discounts` and `tendero.pricing.import.notes` (the
/// tracking plan; phase 16, spec *Evidence of use*): how much of a store's
/// discounts the engine can express, by source, labelled as the other imports are.
/// </summary>
internal static class ProposePromotionsMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Pricing);

    private static readonly Counter<long> Discounts = Meter.CreateCounter<long>(
        "tendero.pricing.import.discounts", unit: "{discount}", description: "Discounts a proposal turned into promotions or left out, by source.");

    private static readonly Counter<long> Notes = Meter.CreateCounter<long>(
        "tendero.pricing.import.notes", unit: "{note}", description: "What a discount source could not hand over as the store ran it, by source and code.");

    public static void Record(string source, int proposed, int left, IEnumerable<DiscountNote> notes)
    {
        Add(source, "proposed", proposed);
        Add(source, "left", left);

        foreach (var group in notes.GroupBy(note => note.Code))
        {
            Notes.Add(
                group.Count(),
                new KeyValuePair<string, object?>("catalog.source", source),
                new KeyValuePair<string, object?>("pricing.import.note", group.Key.ToString()));
        }
    }

    private static void Add(string source, string outcome, int count)
    {
        if (count == 0)
            return;

        Discounts.Add(
            count,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("pricing.import.outcome", outcome));
    }
}
