using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.BuildKit;
using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation.TestHelper;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.Adapters;

namespace ElGuerre.Tendero.Search.Tests.Kits;

/// <summary>
/// A kit under a budget (phase 13c, K-2): the shop's template, the shop's own
/// search for each slot, the solver's choice — and what comes back, with the
/// search's hits in it. What the solver does is in <see cref="KitSolverTests"/>;
/// here it is the real one, so these say what a shopper gets.
/// </summary>
public sealed class BuildKitTests
{
    private static readonly KitTemplate Kitchen = new("KITCHEN_STARTER", Text("Kit de cocina", "Kitchen starter kit"),
    [
        new KitTemplateSlot("pan", Text("Sartén", "Frying pan"), "COOKWARE", Text("sartén", "frying pan"), Required: true),
        new KitTemplateSlot("knife", Text("Cuchillo", "Knife"), "KNIVES", Text("cuchillo", "knife"), Required: true),
        new KitTemplateSlot("board", Text("Tabla de cortar", "Chopping board"), "COOKWARE", Text("tabla de cortar", "chopping board"), Required: false),
    ]);

    /// <summary>HOME › KITCHEN › COOKWARE, which declares INDUCTION, and KNIVES, which does not.</summary>
    private static CategoryTree Categories()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
        var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
        var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje", "Cookware"), kitchen, attributes: ["INDUCTION", "MATERIAL"]);
        var knives = Category.Define(Clock, "KNIVES", Text("Cuchillos", "Knives"), kitchen, attributes: ["MATERIAL"]);
        return new CategoryTree([home, kitchen, cookware, knives]);
    }

    private static SearchHit Hit(string code, decimal price, string currency = "EUR") =>
        new($"id-{code}", code, $"Name of {code}", code.ToLowerInvariant(), "Kuisina", "COOKWARE", $"v-{code}", $"SKU-{code}",
            price, price, price, currency, null, InStock: true, Score: 1);

    /// <summary>The shop's search, answering each slot by its words and remembering what it was asked.</summary>
    private sealed class Shop(Func<ProductSearchQuery, SearchHit[]> answers) : ISearchStrategy
    {
        public List<ProductSearchQuery> Asked { get; } = [];

        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            Asked.Add(query);
            var hits = answers(query);
            return Task.FromResult(new SearchResultPage(hits, hits.Length, 1, query.PageSize, 0));
        }
    }

    private sealed class Templates(params KitTemplate[] kits) : IKitTemplateReader
    {
        public Task<IReadOnlyList<KitTemplate>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KitTemplate>>(kits);
    }

    private sealed class Tree(CategoryTree tree) : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(tree);
    }

    private sealed class Definitions(AttributeDefinitions definitions) : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(definitions);
    }

    /// <summary>The spec's story, as the search would answer it: best ranked first.</summary>
    private static SearchHit[] TheStory(ProductSearchQuery query) => query.Text switch
    {
        "sartén" or "frying pan" => [Hit("PAN-IRON", 49.90m), Hit("PAN-SET", 44.95m)],
        "cuchillo" or "knife" => [Hit("KNIVES-3", 119m), Hit("KNIFE-CHEF", 64m)],
        "tabla de cortar" or "chopping board" => [Hit("BOARD", 24.90m)],
        _ => [],
    };

    private static async Task<(BuildKitResult Result, Shop Shop)> Build(
        decimal budget, string culture = "es", string[]? requirements = null, Func<ProductSearchQuery, SearchHit[]>? answers = null,
        string template = "KITCHEN_STARTER", KitTemplate? kit = null, IKitSolver? solver = null)
    {
        var shop = new Shop(answers ?? TheStory);
        var handler = new BuildKitHandler(new Templates(kit ?? Kitchen), new Tree(Categories()), shop, solver ?? new BranchAndBoundKitSolver());
        var result = await handler.HandleAsync(new BuildKitQuery(template, budget, culture, requirements ?? []), TestContext.Current.CancellationToken);
        return (result, shop);
    }

    [Fact]
    public async Task A_kit_the_shop_does_not_offer_is_not_made_up()
    {
        var (result, shop) = await Build(120m, template: "PICNIC");

        Assert.Equal((BuildKitOutcome.UnknownTemplate, null), (result.Outcome, result.Kit));
        Assert.Empty(shop.Asked);
    }

    /// <summary>A kit's code is found in any case: it is typed into an address.</summary>
    [Fact]
    public async Task A_kit_is_found_by_its_code_in_any_case()
    {
        var (result, _) = await Build(120m, template: "kitchen_starter");

        Assert.Equal("KITCHEN_STARTER", result.Kit!.Template);
    }

    /// <summary>
    /// **Each slot is the shop's own search**: its words in the culture asked, its
    /// category, in stock only, one page of twenty — what the storefront would
    /// ask, and nothing a second index or a second ranking could disagree with.
    /// </summary>
    [Fact]
    public async Task Each_slot_is_searched_as_the_shop_searches_with_its_words_its_category_and_only_what_is_in_stock()
    {
        var (_, shop) = await Build(120m, culture: "en");

        Assert.Equal(
            [("frying pan", "COOKWARE"), ("knife", "KNIVES"), ("chopping board", "COOKWARE")],
            shop.Asked.Select(query => (query.Text, query.Category!)));
        Assert.All(shop.Asked, query =>
        {
            Assert.Equal(("en", 1, 20, ProductSort.Relevance), (query.Culture, query.Page, query.PageSize, query.Sort));
            Assert.True(query.Filters!.InStock);
        });
    }

    /// <summary>The spec's story, end to end: what fits, what did not, and why — with the shop's own hits in the slots.</summary>
    [Fact]
    public async Task The_kit_holds_the_searchs_own_hits_and_says_what_did_not_fit()
    {
        var (result, _) = await Build(120m);
        var kit = result.Kit!;

        Assert.Equal(BuildKitOutcome.Built, result.Outcome);
        Assert.Equal(("KITCHEN_STARTER", "Kit de cocina", 120m, 113.90m, 6.10m, "EUR"),
            (kit.Template, kit.Name, kit.Budget, kit.Total, kit.Left, kit.Currency));
        Assert.Equal((KitBinding.Budget, true, true), (kit.Binding, kit.Complete, kit.Exact));

        Assert.Equal(
            [("pan", "Sartén", true, "PAN-IRON"), ("knife", "Cuchillo", true, "KNIFE-CHEF"), ("board", "Tabla de cortar", false, null)],
            kit.Slots.Select(slot => (slot.Code, slot.Name, slot.Required, slot.Product?.Code)));

        // A hit as the search gave it: its SKU is what a cart is filled with.
        Assert.Equal(("SKU-PAN-IRON", 49.90m), (kit.Slots[0].Product!.MatchedSku, kit.Slots[0].Product!.PriceAmount));

        Assert.Equal(("KNIVES-3", 48.90m), (kit.Slots[1].Better!.Product.Code, kit.Slots[1].Better!.Short));
        Assert.Equal((KitGapReason.OverBudget, "BOARD", 18.80m),
            (kit.Slots[2].Gap!.Reason, kit.Slots[2].Gap!.Cheapest!.Code, kit.Slots[2].Gap!.Short));
    }

    /// <summary>The names are the shop's, in the language asked (invariant 6).</summary>
    [Fact]
    public async Task The_kit_and_its_slots_are_named_in_the_language_asked()
    {
        var (result, _) = await Build(120m, culture: "en");

        Assert.Equal("Kitchen starter kit", result.Kit!.Name);
        Assert.Equal(["Frying pan", "Knife", "Chopping board"], result.Kit.Slots.Select(slot => slot.Name));
    }

    /// <summary>
    /// **Relevance is the rank.** The search put the iron pan first; with money
    /// for either, it is the one taken — though the other is cheaper and the
    /// search's scores say nothing here (they are all the same).
    /// </summary>
    [Fact]
    public async Task What_the_search_ranked_first_is_taken_when_both_fit()
    {
        var (result, _) = await Build(500m);

        Assert.Equal(["PAN-IRON", "KNIVES-3", "BOARD"], result.Kit!.Slots.Select(slot => slot.Product!.Code));
        Assert.Equal(KitBinding.None, result.Kit.Binding);
    }

    /// <summary>
    /// **A requirement binds the slots whose category declares its attribute**
    /// (ADR 0056): *for induction* narrows the pan and the board's category, and
    /// says nothing to the knife's. The category tree decides.
    /// </summary>
    [Fact]
    public async Task A_requirement_is_asked_of_the_slots_whose_category_declares_it_and_of_no_other()
    {
        var (result, shop) = await Build(120m, requirements: ["INDUCTION:true"]);

        Assert.Equal(
            [("COOKWARE", "INDUCTION:true"), ("KNIVES", ""), ("COOKWARE", "INDUCTION:true")],
            shop.Asked.Select(query => (query.Category!, string.Join(",", query.Filters!.Facets))));
        Assert.Equal(
            [("pan", "INDUCTION:true"), ("knife", ""), ("board", "INDUCTION:true")],
            result.Kit!.Slots.Select(slot => (slot.Code, string.Join(",", slot.Requirements))));
        Assert.Empty(result.Kit.UnusedRequirements);
    }

    /// <summary>
    /// A category that declares nothing — or one the catalogue does not have —
    /// has said nothing about induction: its slot is not held to it. *Nobody
    /// has said* is not *yes*, and a filter on an attribute no product of the
    /// category carries would empty the slot for no reason a shopper could see.
    /// </summary>
    [Theory]
    [InlineData("HOME")]
    [InlineData("GARDEN")]
    public async Task A_slot_whose_category_declares_nothing_is_not_held_to_a_requirement(string category)
    {
        var loose = new KitTemplate("KITCHEN_STARTER", Text("Kit", "Kit"),
            [new KitTemplateSlot("pan", Text("Sartén", "Frying pan"), category, Text("sartén", "frying pan"), Required: true)]);

        var (result, shop) = await Build(120m, requirements: ["INDUCTION:true"], kit: loose);

        Assert.Empty(Assert.Single(shop.Asked).Filters!.Facets);
        Assert.Equal(["INDUCTION:true"], result.Kit!.UnusedRequirements);
    }

    /// <summary>A requirement no slot of the kit can be held to is said, not ignored in silence: the shopper asked for it.</summary>
    [Fact]
    public async Task A_requirement_no_slot_declares_is_said_to_be_unused()
    {
        var (result, shop) = await Build(120m, requirements: ["INDUCTION:true", "COLOR:BLACK"]);

        Assert.Equal(["COLOR:BLACK"], result.Kit!.UnusedRequirements);
        Assert.All(shop.Asked, query => Assert.DoesNotContain("COLOR:BLACK", query.Filters!.Facets));
    }

    /// <summary>A price in another currency cannot be added to a budget in euros: such a hit is not a candidate.</summary>
    [Fact]
    public async Task A_product_priced_in_another_currency_is_not_a_candidate()
    {
        var (result, _) = await Build(500m, answers: query => query.Text == "sartén" ? [Hit("PAN-USD", 10m, "USD"), Hit("PAN-EUR", 40m)] : TheStory(query));

        Assert.Equal("PAN-EUR", result.Kit!.Slots[0].Product!.Code);
    }

    /// <summary>A kit that cannot hold what it needs is still an answer — and says it is not whole.</summary>
    [Fact]
    public async Task A_kit_missing_a_slot_it_needs_is_an_answer_that_says_it_is_not_complete()
    {
        var (result, _) = await Build(60m);
        var kit = result.Kit!;

        Assert.False(kit.Complete);
        Assert.Equal(KitBinding.Budget, kit.Binding);
        Assert.Contains(kit.Slots, slot => slot is { Required: true, Product: null, Gap.Reason: KitGapReason.OverBudget });
    }

    [Fact]
    public async Task A_slot_the_search_finds_nothing_for_says_there_was_nothing()
    {
        var (result, _) = await Build(500m, answers: query => query.Text == "cuchillo" ? [] : TheStory(query));

        Assert.Equal((KitGapReason.NoCandidate, null, null),
            (result.Kit!.Slots[1].Gap!.Reason, result.Kit.Slots[1].Gap!.Cheapest, result.Kit.Slots[1].Gap!.Short));
        Assert.False(result.Kit.Complete);
    }

    // ---------- the explanation: written by code from the trace ----------

    private static (string? Slot, string Text)[] Said(KitView kit) => [.. kit.Explanation.Select(sentence => (sentence.Slot, sentence.Text))];

    /// <summary>The solver, stopped after so many nodes: what a search that ran out of its budget gives.</summary>
    private sealed class StoppedAt(int nodes) : IKitSolver
    {
        public KitSolution Solve(KitProblem problem) => new BranchAndBoundKitSolver().Solve(problem with { NodeCeiling = nodes });
    }

    /// <summary>
    /// **The spec's story, said.** A sentence a fact: the kit against its
    /// budget; each slot with what it holds; the knives that ranked higher and
    /// did not fit, by how much; the board there was no money for. Every amount
    /// is the trace's, or the difference of two of them.
    /// </summary>
    [Fact]
    public async Task The_explanation_says_what_each_slot_holds_what_ranked_higher_and_what_did_not_fit()
    {
        var (result, _) = await Build(120m);

        Assert.Equal(
        [
            (null, "Kit de cocina: 113.90 EUR de 120.00 EUR."),
            ("pan", "Sartén: Name of PAN-IRON, 49.90 EUR."),
            ("knife", "Cuchillo: Name of KNIFE-CHEF, 64.00 EUR. Name of KNIVES-3 está mejor valorado y no cabía: cuesta 55.00 EUR más y quedaban 6.10 EUR."),
            ("board", "Sin tabla de cortar: lo más barato cuesta 24.90 EUR y quedaban 6.10 EUR."),
        ], Said(result.Kit!));
    }

    [Fact]
    public async Task The_explanation_is_said_in_english_when_asked_to()
    {
        var (result, _) = await Build(120m, culture: "en");

        Assert.Equal(
        [
            (null, "Kitchen starter kit: 113.90 EUR of 120.00 EUR."),
            ("pan", "Frying pan: Name of PAN-IRON, 49.90 EUR."),
            ("knife", "Knife: Name of KNIFE-CHEF, 64.00 EUR. Name of KNIVES-3 ranks higher and did not fit: it costs 55.00 EUR more and 6.10 EUR was left."),
            ("board", "No chopping board: the cheapest costs 24.90 EUR and 6.10 EUR was left."),
        ], Said(result.Kit!));
    }

    /// <summary>A kit that took the best of everything has nothing to explain beyond what it holds.</summary>
    [Fact]
    public async Task A_kit_held_back_by_nothing_says_what_it_holds_and_no_more()
    {
        var (result, _) = await Build(500m);

        Assert.Equal(
        [
            (null, "Kit de cocina: 193.80 EUR de 500.00 EUR."),
            ("pan", "Sartén: Name of PAN-IRON, 49.90 EUR."),
            ("knife", "Cuchillo: Name of KNIVES-3, 119.00 EUR."),
            ("board", "Tabla de cortar: Name of BOARD, 24.90 EUR."),
        ], Said(result.Kit!));
    }

    /// <summary>Nothing to choose from is said as that, and a kit missing what it needs says it is not whole — last, about the kit.</summary>
    [Theory]
    [InlineData("es", "Sin cuchillo: no hay nada en stock que encaje.", "El kit no está completo: le falta algo que necesita.")]
    [InlineData("en", "No knife: nothing in stock fits.", "The kit is not complete: something it needs is missing.")]
    public async Task A_slot_with_nothing_to_choose_from_and_a_kit_that_is_not_whole_are_said(string culture, string nothing, string notWhole)
    {
        var (result, _) = await Build(500m, culture, answers: query => query.Text is "cuchillo" or "knife" ? [] : TheStory(query));

        Assert.Contains(("knife", nothing), Said(result.Kit!));
        Assert.Equal((null, notWhole), Said(result.Kit!)[^1]);
    }

    /// <summary>An optional slot left empty does not make the kit incomplete, and nothing says it does.</summary>
    [Fact]
    public async Task A_kit_missing_only_what_it_can_do_without_does_not_say_it_is_incomplete()
    {
        var (result, _) = await Build(120m);

        Assert.DoesNotContain(Said(result.Kit!), sentence => sentence.Text.Contains("completo", StringComparison.Ordinal));
    }

    /// <summary>What was asked and could not be held to anything is said, in the shopper's words for it: the facet as they sent it.</summary>
    [Theory]
    [InlineData("es", "No se ha podido exigir COLOR:BLACK: ningún hueco de este kit lo declara.")]
    [InlineData("en", "COLOR:BLACK could not be required: no slot of this kit declares it.")]
    public async Task A_requirement_that_was_not_used_is_said(string culture, string unused)
    {
        var (result, _) = await Build(120m, culture, requirements: ["INDUCTION:true", "COLOR:BLACK"]);

        Assert.Contains((null, unused), Said(result.Kit!));
        Assert.DoesNotContain(Said(result.Kit!), sentence => sentence.Text.Contains("INDUCTION", StringComparison.Ordinal));
    }

    /// <summary>A search that stopped at its ceiling gives the best kit it found, and the explanation never passes it off as the best there is.</summary>
    [Theory]
    [InlineData("es", "Es el mejor kit encontrado, no necesariamente el mejor: la búsqueda se detuvo en su límite.")]
    [InlineData("en", "This is the best kit found, not necessarily the best there is: the search stopped at its limit.")]
    public async Task A_kit_from_a_search_that_stopped_says_it_is_the_best_found(string culture, string bestFound)
    {
        var (stopped, _) = await Build(120m, culture, solver: new StoppedAt(1));
        var (whole, _) = await Build(120m, culture);

        Assert.False(stopped.Kit!.Exact);
        Assert.Contains((null, bestFound), Said(stopped.Kit));
        Assert.DoesNotContain((null, bestFound), Said(whole.Kit!));
    }

    /// <summary>
    /// **The explanation says only the trace.** Every amount in it, in either
    /// language and at any budget, is one the kit carries — a price, the total,
    /// the budget, what was left — or how much more a better product costs,
    /// which is two of those subtracted. The property a model's paragraph
    /// cannot be given, stated here about the text it would be handed.
    /// </summary>
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Every_amount_the_explanation_names_is_one_the_kit_carries(string culture)
    {
        foreach (var budget in (decimal[])[10m, 60m, 75m, 115m, 120m, 140m, 190m, 500m])
        {
            var kit = (await Build(budget, culture)).Result.Kit!;

            HashSet<decimal> carried = [kit.Budget, kit.Total, kit.Left];
            foreach (var slot in kit.Slots)
            {
                if (slot.Product is { } product)
                    carried.Add(product.PriceAmount);
                if (slot.Gap?.Cheapest is { } cheapest)
                    carried.Add(cheapest.PriceAmount);
                if (slot is { Better: { } better, Product: { } held })
                    carried.Add(better.Product.PriceAmount - held.PriceAmount);
            }

            var named = kit.Explanation
                .SelectMany(sentence => System.Text.RegularExpressions.Regex.Matches(sentence.Text, @"(\d+\.\d{2}) EUR"))
                .Select(match => decimal.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            Assert.NotEmpty(named);
            Assert.All(named, amount => Assert.Contains(amount, carried));
        }
    }

    // ---------- the kits the shop offers ----------

    /// <summary>
    /// What the form shows before anything is built: the kit, its slots, and
    /// what can be required of it — the yes-or-no facets its slots' categories
    /// declare, under the attribute's own label. MATERIAL is declared and is
    /// not a yes-or-no; COLOR is one of the shop's facets and no slot declares
    /// it; and whether something goes in the dishwasher is a yes-or-no the shop
    /// filters by that none of this kit's categories declares.
    /// </summary>
    [Theory]
    [InlineData("es", "Kit de cocina", "Sartén", "inducción")]
    [InlineData("en", "Kitchen starter kit", "Frying pan", "induction")]
    public async Task The_shops_kits_are_listed_with_what_can_be_required_of_each(string culture, string name, string firstSlot, string label)
    {
        var dishwasher = AttributeDefinition.Define(Clock, "DISHWASHER", Text("lavavajillas", "dishwasher"), AttributeKind.Boolean, isFacet: true);
        var handler = new ListKitsHandler(new Templates(Kitchen), new Tree(Categories()),
            new Definitions(new AttributeDefinitions([.. ShopDefinitions().All, dishwasher])));

        var kit = Assert.Single(await handler.HandleAsync(new ListKitsQuery(culture), TestContext.Current.CancellationToken));

        Assert.Equal(("KITCHEN_STARTER", name), (kit.Code, kit.Name));
        Assert.Equal([("pan", firstSlot, true), ("knife", kit.Slots[1].Name, true), ("board", kit.Slots[2].Name, false)],
            kit.Slots.Select(slot => (slot.Code, slot.Name, slot.Required)));
        Assert.Equal([new KitRequirementOption("INDUCTION:true", label)], kit.Requirements);
    }

    // ---------- what is not a request ----------

    private static readonly BuildKitValidator Validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_000.01)]
    public void A_budget_of_nothing_or_beyond_the_ceiling_is_refused(decimal budget)
    {
        Validator.TestValidate(new BuildKitQuery("KITCHEN_STARTER", budget, "es", [])).ShouldHaveValidationErrorFor(x => x.Budget);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kitchen starter")]
    [InlineData("KIT/../X")]
    public void A_template_that_is_not_a_code_is_refused(string template)
    {
        Validator.TestValidate(new BuildKitQuery(template, 120m, "es", [])).ShouldHaveValidationErrorFor(x => x.Template);
    }

    [Theory]
    [InlineData("induction")]
    [InlineData("induction:true")]
    [InlineData("INDUCTION:")]
    public void A_requirement_that_is_not_a_facet_is_refused(string requirement)
    {
        Validator.TestValidate(new BuildKitQuery("KITCHEN_STARTER", 120m, "es", [requirement])).ShouldHaveValidationErrorFor("requirements[0]");
    }

    [Fact]
    public void More_requirements_than_a_form_has_are_refused_and_a_plain_request_is_not()
    {
        var many = Enumerable.Range(0, BuildKitValidator.MaxRequirements + 1).Select(index => $"A{index}:true").ToArray();

        Validator.TestValidate(new BuildKitQuery("KITCHEN_STARTER", 120m, "es", many)).ShouldHaveValidationErrorFor("requirements");
        Validator.TestValidate(new BuildKitQuery("KITCHEN_STARTER", 120m, "fr", [])).ShouldHaveValidationErrorFor(x => x.Culture);
        Validator.TestValidate(new BuildKitQuery("KITCHEN_STARTER", 120m, "en", ["INDUCTION:true"])).ShouldNotHaveAnyValidationErrors();
    }
}
