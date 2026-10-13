using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Features.BuildKit;

// ---------- the kits the shop offers ----------

public sealed record ListKitsQuery(string Culture) : IQuery<IReadOnlyList<KitTemplateView>>;

/// <summary>A kit as the form shows it before anything is built: what it is made of, and what can be asked of it.</summary>
/// <param name="Requirements">What a shopper may require of the kit: the yes-or-no facets its slots' categories declare.</param>
public sealed record KitTemplateView(
    string Code, string Name, IReadOnlyList<KitTemplateSlotView> Slots, IReadOnlyList<KitRequirementOption> Requirements);

public sealed record KitTemplateSlotView(string Code, string Name, bool Required);

/// <param name="Facet">The requirement as the request takes it, and as the search writes it: <c>INDUCTION:true</c>.</param>
/// <param name="Label">The attribute's own label, in the culture asked.</param>
public sealed record KitRequirementOption(string Facet, string Label);

public sealed class ListKitsHandler(IKitTemplateReader templates, ICategoryReader categories, IAttributeDefinitionReader definitions)
    : IQueryHandler<ListKitsQuery, IReadOnlyList<KitTemplateView>>
{
    public async Task<IReadOnlyList<KitTemplateView>> HandleAsync(ListKitsQuery query, CancellationToken cancellationToken)
    {
        var tree = await categories.AllAsync(cancellationToken);
        var attributes = await definitions.AllAsync(cancellationToken);

        return
        [
            .. (await templates.AllAsync(cancellationToken)).Select(template => new KitTemplateView(
                template.Code,
                template.Name.In(query.Culture),
                [.. template.Slots.Select(slot => new KitTemplateSlotView(slot.Code, slot.Name.In(query.Culture), slot.Required))],
                [
                    // A yes-or-no the shop filters by, declared by a category one of
                    // the kit's slots looks in (ADR 0056): the only requirement a kit
                    // can hold a product to, and so the only one it offers.
                    .. template.Slots
                        .SelectMany(slot => tree.AttributesOf(slot.Category) ?? [])
                        .Distinct(StringComparer.Ordinal)
                        .Select(attributes.ByCode)
                        .OfType<AttributeDefinition>()
                        .Where(attribute => attribute is { Kind: AttributeKind.Boolean, IsFacet: true })
                        .Select(attribute => new KitRequirementOption(KitRequirements.Yes(attribute.Code), attribute.Label.In(query.Culture))),
                ])),
        ];
    }
}

/// <summary>A requirement is a facet as the search writes it: <c>CODE:VALUE</c>.</summary>
internal static class KitRequirements
{
    public static string Yes(string attribute) => $"{attribute}:true";

    public static string CodeOf(string requirement) => requirement[..requirement.IndexOf(':', StringComparison.Ordinal)];
}

// ---------- building one ----------

/// <param name="Template">The kit's code, as the shop wrote it.</param>
/// <param name="Budget">What it may cost at most, in the shop's shown prices and in the shop's currency.</param>
/// <param name="Requirements">Facets every fitting product must have (<c>INDUCTION:true</c>). Each applies to the slots whose category declares its attribute, and to no other.</param>
public sealed record BuildKitQuery(string Template, Money Budget, string Culture, IReadOnlyList<string> Requirements)
    : IQuery<BuildKitResult>;

public enum BuildKitOutcome
{
    Built,

    /// <summary>The shop offers no kit of that code: no model makes one up.</summary>
    UnknownTemplate,
}

public sealed record BuildKitResult(BuildKitOutcome Outcome, KitView? Kit);

/// <summary>
/// A kit, with what the solver knows about its own answer (the spec's *trace*):
/// everything an explanation may say is here, and nothing else may be said.
/// </summary>
/// <param name="Total">What the chosen products cost, at the prices the shop shows.</param>
/// <param name="Left">The budget less the total.</param>
/// <param name="Complete">Whether every slot the kit needs is filled.</param>
/// <param name="Exact">False when the search stopped at its ceiling: the kit is then the best found, not the best there is.</param>
/// <param name="UnusedRequirements">Requirements no slot of this kit could be held to, said rather than ignored in silence.</param>
public sealed record KitView(
    string Template, string Name, Money Budget, Money Total, Money Left,
    KitBinding Binding, bool Complete, bool Exact,
    IReadOnlyList<KitSlotView> Slots, IReadOnlyList<string> UnusedRequirements)
{
    /// <summary>
    /// Why the kit is what it is, **written by code from the fields above and
    /// nothing else**, a sentence a fact, in the culture asked. The feature's
    /// path with no model in it, and the only text a model may later reword.
    ///
    /// Each is a <see cref="Message{TCode}"/>: its code says what kind of thing
    /// is said and what it is about, so a page composes its own sentence from
    /// the kit's fields and an agent reads this one.
    /// </summary>
    public required IReadOnlyList<Message<KitSaid>> Explanation { get; init; }
}

/// <summary>What a kit's explanation says, as the closed set a reader branches on (ADR 0043).</summary>
public enum KitSaid
{
    /// <summary>The kit against its budget: about the kit.</summary>
    Summary,

    /// <summary>What a slot holds, and nothing ranked above it was left out.</summary>
    Chosen,

    /// <summary>What a slot holds, when a better ranked product did not fit: by how much, and what was left.</summary>
    ChosenBelowBetter,

    /// <summary>A slot left empty for money: its cheapest candidate cost more than was left.</summary>
    OverBudget,

    /// <summary>A slot left empty because nothing in stock fits it.</summary>
    NothingFits,

    /// <summary>A requirement no slot of the kit could be held to: about the requirement, as it was sent.</summary>
    RequirementUnused,

    /// <summary>The search stopped at its limit: the best kit found, not necessarily the best there is.</summary>
    SearchStopped,

    /// <summary>The kit lacks something it needs.</summary>
    Incomplete,
}

/// <param name="Requirements">The requirements this slot's candidates were held to.</param>
/// <param name="Product">What it holds, as the shop's own search shows it; null when empty.</param>
public sealed record KitSlotView(
    string Code, string Name, bool Required, IReadOnlyList<string> Requirements,
    SearchHit? Product, KitGapView? Gap, KitUpgradeView? Better);

/// <param name="Cheapest">Over budget: the cheapest product that could have gone there.</param>
/// <param name="Short">Over budget: how much more it costs than what was left.</param>
public sealed record KitGapView(KitGapReason Reason, SearchHit? Cheapest, Money? Short);

/// <param name="Short">How much was missing to swap it in, with everything else as it is.</param>
public sealed record KitUpgradeView(SearchHit Product, Money Short);

public sealed partial class BuildKitValidator : AbstractValidator<BuildKitQuery>
{
    /// <summary>More than any kit of this catalogue costs, and a bound on a number anybody can type.</summary>
    public const decimal MaxBudget = 100_000m;

    public const int MaxRequirements = 10;

    public BuildKitValidator()
    {
        RuleFor(x => x.Template).NotEmpty().Matches("^[A-Za-z0-9_]{1,60}$")
            .WithMessage("A kit is its code: letters, digits and underscores (KITCHEN_STARTER).");
        RuleFor(x => x.Budget.Amount).GreaterThan(0).LessThanOrEqualTo(MaxBudget).OverridePropertyName("budget");
        RuleFor(x => x.Budget.Currency).Equal(BuildKitHandler.Currency)
            .WithMessage($"The shop sells in {BuildKitHandler.Currency}: a budget is in it.")
            .OverridePropertyName("budget");
        RuleFor(x => x.Culture).Must(c => c is "es" or "en").WithMessage("Supported cultures: es, en.");

        RuleFor(x => x.Requirements.Count).LessThanOrEqualTo(MaxRequirements).OverridePropertyName("requirements");
        RuleForEach(x => x.Requirements).Must(pair => pair is not null && CodeAndValue().IsMatch(pair))
            .WithMessage("A requirement is CODE:VALUE, the code in upper case (INDUCTION:true).")
            .OverridePropertyName("requirements");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z0-9_]{1,60}:[^\s:]{1,100}$")]
    private static partial System.Text.RegularExpressions.Regex CodeAndValue();
}

/// <summary>
/// A kit under a budget (phase 13c): **retrieve, then solve** — and nothing here
/// chooses. Each slot is the shop's own search, as the storefront runs it; the
/// choice is the solver's, a pure function; and what comes back is its answer
/// with the search's own hits in the slots, so a kit shows the shop's cards and
/// not a second kind of product.
/// </summary>
public sealed class BuildKitHandler(
    IKitTemplateReader templates,
    ICategoryReader categories,
    [FromKeyedServices(SearchStrategies.Hybrid)] ISearchStrategy search,
    IKitSolver solver)
    : IQueryHandler<BuildKitQuery, BuildKitResult>
{
    /// <summary>What a slot is given to choose from: the first page of its search.</summary>
    public const int CandidatesPerSlot = 20;

    /// <summary>The shop's one currency (laboratory scale): a budget is in it, and so is every candidate.</summary>
    public const string Currency = ShopOptions.DefaultCurrency;

    /// <summary>What the best ranked candidate is worth; the last of a full page is worth a twentieth of it.</summary>
    private const int RelevanceOfTheFirst = 1000;

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<BuildKitResult> HandleAsync(BuildKitQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("search.build_kit");
        activity?.SetTag("kit.template", query.Template);

        var template = (await templates.AllAsync(cancellationToken))
            .FirstOrDefault(kit => string.Equals(kit.Code, query.Template, StringComparison.OrdinalIgnoreCase));
        if (template is null)
            return new BuildKitResult(BuildKitOutcome.UnknownTemplate, null);

        var tree = await categories.AllAsync(cancellationToken);

        // A requirement binds the slots whose category declares its attribute
        // (ADR 0056), and no other: "for induction" narrows the pan and the pot
        // and says nothing about a knife. The category tree decides, not the
        // template's author and not a model.
        var applied = template.Slots.ToDictionary(
            slot => slot.Code,
            slot => (IReadOnlyList<string>)
            [
                .. query.Requirements.Where(requirement =>
                    tree.AttributesOf(slot.Category)?.Contains(KitRequirements.CodeOf(requirement), StringComparer.Ordinal) == true),
            ]);

        // Retrieve: each slot's words in the culture asked, its category, its
        // requirements, in stock only — ordinary search, already gated.
        Dictionary<string, SearchHit> hits = new(StringComparer.Ordinal);
        List<KitSlot> slots = [];
        foreach (var slot in template.Slots)
        {
            var page = await search.SearchAsync(
                new ProductSearchQuery(
                    slot.Query.In(query.Culture), query.Culture, Page: 1, PageSize: CandidatesPerSlot, Category: slot.Category,
                    Filters: new SearchFilters { InStock = true, Facets = applied[slot.Code] }),
                cancellationToken);

            var found = page.Hits.Where(hit => hit.Price.Currency == Currency).ToList();
            foreach (var hit in found)
                hits[hit.Code] = hit;

            // Relevance is the rank, never the score: scores of two searches are
            // not comparable. Whole points, so their sum is the same in any order.
            slots.Add(new KitSlot(slot.Code, slot.Required,
            [
                .. found.Select((hit, rank) => new KitCandidate(
                    hit.Code, hit.Price, RelevanceOfTheFirst * (found.Count - rank) / found.Count)),
            ]));
        }

        // Solve: pure. Whatever a model may do before or after this line, it does not do this.
        var solution = solver.Solve(new KitProblem(query.Budget, slots));

        activity?.SetTag("kit.slots", solution.Slots.Count);
        activity?.SetTag("kit.filled", solution.Slots.Count(slot => slot.Chosen is not null));
        activity?.SetTag("kit.binding", solution.Binding.ToString());
        activity?.SetTag("kit.nodes", solution.Nodes);

        var kit = new KitView(
            template.Code, template.Name.In(query.Culture), query.Budget, solution.Total, solution.Left,
            solution.Binding,
            Complete: solution.Slots.All(slot => !slot.Required || slot.Chosen is not null),
            Exact: !solution.CeilingReached,
            [
                .. solution.Slots.Zip(template.Slots, (solved, written) => new KitSlotView(
                    solved.Slot, written.Name.In(query.Culture), solved.Required, applied[solved.Slot],
                    solved.Chosen is { } chosen ? hits[chosen.Code] : null,
                    solved.Gap is { } gap
                        ? new KitGapView(gap.Reason, gap.Cheapest is { } cheapest ? hits[cheapest.Code] : null, gap.Short)
                        : null,
                    solved.Better is { } better ? new KitUpgradeView(hits[better.Candidate.Code], better.Short) : null)),
            ],
            [.. query.Requirements.Where(requirement => applied.Values.All(of => !of.Contains(requirement)))])
        {
            // Said from the kit itself, so it is put together first and explained after.
            Explanation = [],
        };

        return new BuildKitResult(BuildKitOutcome.Built, kit with { Explanation = new KitWords(query.Culture).Of(kit) });
    }
}

/// <summary>
/// A kit's explanation in the shopper's words, es and en (invariant 6) —
/// **read off the kit and nothing else**. It is given the answer and not the
/// search, the template or the solver, so there is nothing here it could say
/// that the kit does not carry: every amount is one of the kit's, or how much
/// more a better product costs, which is two of them subtracted.
///
/// A sentence a fact, each about a slot or about the kit, so a page can put
/// each beside what it is about and a model handed these to reword can be held
/// to them one by one.
/// </summary>
internal sealed class KitWords(string culture)
{
    private bool English => culture == "en";

    public IReadOnlyList<Message<KitSaid>> Of(KitView kit)
    {
        List<Message<KitSaid>> says =
        [
            new(KitSaid.Summary, null, English
                ? $"{kit.Name}: {Amount(kit.Total)} of {Amount(kit.Budget)}."
                : $"{kit.Name}: {Amount(kit.Total)} de {Amount(kit.Budget)}."),
        ];

        foreach (var slot in kit.Slots)
            says.Add(new Message<KitSaid>(Said(slot), slot.Code, Of(slot, kit)));

        foreach (var unused in kit.UnusedRequirements)
        {
            says.Add(new Message<KitSaid>(KitSaid.RequirementUnused, unused, English
                ? $"{unused} could not be required: no slot of this kit declares it."
                : $"No se ha podido exigir {unused}: ningún hueco de este kit lo declara."));
        }

        if (!kit.Exact)
        {
            says.Add(new Message<KitSaid>(KitSaid.SearchStopped, null, English
                ? "This is the best kit found, not necessarily the best there is: the search stopped at its limit."
                : "Es el mejor kit encontrado, no necesariamente el mejor: la búsqueda se detuvo en su límite."));
        }

        if (!kit.Complete)
        {
            says.Add(new Message<KitSaid>(KitSaid.Incomplete, null, English
                ? "The kit is not complete: something it needs is missing."
                : "El kit no está completo: le falta algo que necesita."));
        }

        return says;
    }

    /// <summary>Which of the four things a slot's sentence says: the same decision the sentence below is written from.</summary>
    private static KitSaid Said(KitSlotView slot) => slot switch
    {
        { Product: not null, Better: not null } => KitSaid.ChosenBelowBetter,
        { Product: not null } => KitSaid.Chosen,
        { Gap.Cheapest: not null } => KitSaid.OverBudget,
        _ => KitSaid.NothingFits,
    };

    private string Of(KitSlotView slot, KitView kit)
    {
        // A slot's name inside a sentence: "Sin tabla de cortar", "No chopping board".
        var inside = slot.Name.ToLowerInvariant();

        switch (slot)
        {
            case { Product: { } product, Better: { } better }:
                var more = Amount(Price(better.Product) - Price(product));
                return English
                    ? $"{slot.Name}: {product.Name}, {Amount(Price(product))}. {better.Product.Name} ranks higher and did not fit: it costs {more} more and {Amount(kit.Left)} was left."
                    : $"{slot.Name}: {product.Name}, {Amount(Price(product))}. {better.Product.Name} está mejor valorado y no cabía: cuesta {more} más y quedaban {Amount(kit.Left)}.";

            case { Product: { } product }:
                return $"{slot.Name}: {product.Name}, {Amount(Price(product))}.";

            case { Gap.Cheapest: { } cheapest }:
                return English
                    ? $"No {inside}: the cheapest costs {Amount(Price(cheapest))} and {Amount(kit.Left)} was left."
                    : $"Sin {inside}: lo más barato cuesta {Amount(Price(cheapest))} y quedaban {Amount(kit.Left)}.";

            default:
                return English ? $"No {inside}: nothing in stock fits." : $"Sin {inside}: no hay nada en stock que encaje.";
        }
    }

    /// <summary>As an order's total and the shipping terms are written for the same reader: two decimals and the currency's code.</summary>
    private static string Amount(Money money) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{money.Amount:0.00} {money.Currency}");

    private static Money Price(SearchHit hit) => hit.Price;
}

/// <summary>What a kit is asked with: its code is in the address.</summary>
public sealed record BuildKitRequest(Money Budget, IReadOnlyList<string>? Requirements = null);

public sealed class KitsEndpoint : ICarterModule
{
    /// <summary>The rate limit's name: a kit is up to five searches for anybody who asks.</summary>
    public const string PerClient = "Kits.PerClient";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/kits — the kits the shop offers, and what can be required of each.
        app.MapGet("/api/kits",
                async Task<Ok<IReadOnlyList<KitTemplateView>>> (
                    string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var kits = await dispatcher.SendAsync(new ListKitsQuery(resolved), ct);

                    http.Response.Headers.ContentLanguage = resolved;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(kits);
                })
            // By explicit decision: what the shop offers anybody.
            .AllowAnonymous()
            .WithTags("Kits")
            .WithName("ListKits");

        // POST /api/kits/KITCHEN_STARTER/build   { "budget": 120, "requirements": ["INDUCTION:true"] }
        // A POST that changes nothing: it is asked with a body because a budget and
        // a list of requirements are a request, not an address.
        app.MapPost("/api/kits/{template}/build",
                async Task<Results<Ok<KitView>, NotFound>> (
                    string template, BuildKitRequest request, string? culture,
                    HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await dispatcher.SendAsync(
                        new BuildKitQuery(template, request.Budget, resolved, request.Requirements ?? []), ct);

                    if (result.Kit is null)
                        return TypedResults.NotFound();

                    http.Response.Headers.ContentLanguage = resolved;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result.Kit);
                })
            // By explicit decision: what search shows anybody, put together under a budget.
            .AllowAnonymous()
            .RequireRateLimiting(PerClient)
            .WithTags("Kits")
            .WithName("BuildKit");
    }
}
