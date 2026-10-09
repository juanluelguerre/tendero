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
/// <param name="Budget">What it may cost at most, in the shop's shown prices.</param>
/// <param name="Requirements">Facets every fitting product must have (<c>INDUCTION:true</c>). Each applies to the slots whose category declares its attribute, and to no other.</param>
public sealed record BuildKitQuery(string Template, decimal Budget, string Culture, IReadOnlyList<string> Requirements)
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
    string Template, string Name, decimal Budget, decimal Total, decimal Left, string Currency,
    KitBinding Binding, bool Complete, bool Exact,
    IReadOnlyList<KitSlotView> Slots, IReadOnlyList<string> UnusedRequirements);

/// <param name="Requirements">The requirements this slot's candidates were held to.</param>
/// <param name="Product">What it holds, as the shop's own search shows it; null when empty.</param>
public sealed record KitSlotView(
    string Code, string Name, bool Required, IReadOnlyList<string> Requirements,
    SearchHit? Product, KitGapView? Gap, KitUpgradeView? Better);

/// <param name="Cheapest">Over budget: the cheapest product that could have gone there.</param>
/// <param name="Short">Over budget: how much more it costs than what was left.</param>
public sealed record KitGapView(KitGapReason Reason, SearchHit? Cheapest, decimal? Short);

/// <param name="Short">How much was missing to swap it in, with everything else as it is.</param>
public sealed record KitUpgradeView(SearchHit Product, decimal Short);

public sealed partial class BuildKitValidator : AbstractValidator<BuildKitQuery>
{
    /// <summary>More than any kit of this catalogue costs, and a bound on a number anybody can type.</summary>
    public const decimal MaxBudget = 100_000m;

    public const int MaxRequirements = 10;

    public BuildKitValidator()
    {
        RuleFor(x => x.Template).NotEmpty().Matches("^[A-Za-z0-9_]{1,60}$")
            .WithMessage("A kit is its code: letters, digits and underscores (KITCHEN_STARTER).");
        RuleFor(x => x.Budget).GreaterThan(0).LessThanOrEqualTo(MaxBudget);
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
    public const string Currency = "EUR";

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

            var found = page.Hits.Where(hit => hit.PriceCurrency == Currency).ToList();
            foreach (var hit in found)
                hits[hit.Code] = hit;

            // Relevance is the rank, never the score: scores of two searches are
            // not comparable. Whole points, so their sum is the same in any order.
            slots.Add(new KitSlot(slot.Code, slot.Required,
            [
                .. found.Select((hit, rank) => new KitCandidate(
                    hit.Code, new Money(hit.PriceAmount, Currency), RelevanceOfTheFirst * (found.Count - rank) / found.Count)),
            ]));
        }

        // Solve: pure. Whatever a model may do before or after this line, it does not do this.
        var solution = solver.Solve(new KitProblem(new Money(query.Budget, Currency), slots));

        activity?.SetTag("kit.slots", solution.Slots.Count);
        activity?.SetTag("kit.filled", solution.Slots.Count(slot => slot.Chosen is not null));
        activity?.SetTag("kit.binding", solution.Binding.ToString());
        activity?.SetTag("kit.nodes", solution.Nodes);

        var kit = new KitView(
            template.Code, template.Name.In(query.Culture), query.Budget, solution.Total.Amount, solution.Left.Amount, Currency,
            solution.Binding,
            Complete: solution.Slots.All(slot => !slot.Required || slot.Chosen is not null),
            Exact: !solution.CeilingReached,
            [
                .. solution.Slots.Zip(template.Slots, (solved, written) => new KitSlotView(
                    solved.Slot, written.Name.In(query.Culture), solved.Required, applied[solved.Slot],
                    solved.Chosen is { } chosen ? hits[chosen.Code] : null,
                    solved.Gap is { } gap
                        ? new KitGapView(gap.Reason, gap.Cheapest is { } cheapest ? hits[cheapest.Code] : null, gap.Short?.Amount)
                        : null,
                    solved.Better is { } better ? new KitUpgradeView(hits[better.Candidate.Code], better.Short.Amount) : null)),
            ],
            [.. query.Requirements.Where(requirement => applied.Values.All(of => !of.Contains(requirement)))]);

        return new BuildKitResult(BuildKitOutcome.Built, kit);
    }
}

/// <summary>What a kit is asked with: its code is in the address.</summary>
public sealed record BuildKitRequest(decimal Budget, IReadOnlyList<string>? Requirements = null);

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
