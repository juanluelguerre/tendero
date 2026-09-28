using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.Knowledge.Features.RecordClaimApplication;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The specs table and the comparison (P10-7, P10-8): approved claims only, a
/// quote only beside the value its claim put there, and "no data" where a product
/// has none — never a guess, and decided by the query rather than by a prompt.
/// </summary>
public sealed class ProductSpecsTests
{
    private const string MokaText = "Cafetera italiana de aluminio para 6 tazas, con asa de baquelita.";

    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private readonly FixedFacts facts = new();
    private readonly ProductId moka = ProductId.New();
    private readonly ProductId drip = ProductId.New();

    public ProductSpecsTests()
    {
        this.facts.Add(this.moka, "CAF-001", "Cafetera italiana", Fact("MATERIAL", "Material", "aluminio", "ALUMINIUM"), Fact("CUPS", "Tazas", null, "6", number: 6));
        this.facts.Add(this.drip, "CAF-002", "Cafetera de goteo", Fact("CUPS", "Tazas", null, "12", number: 12));
    }

    [Fact]
    public async Task A_value_its_approved_claim_put_there_carries_the_quote_and_the_rest_say_catalogue()
    {
        await ApproveAndApplyAsync(Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93), heldAs: "ALUMINIUM");

        var sheet = await SpecsAsync("CAF-001");

        var material = Assert.Single(sheet.Rows, row => row.AttributeCode == "MATERIAL").Cells.Single();
        Assert.Equal(SpecSources.Description, material.Source);
        Assert.Equal(0.93, material.Evidence!.Support);
        Assert.Equal(("es", "de aluminio"), (material.Evidence.Quotes.Single().Culture, material.Evidence.Quotes.Single().Text));

        var cups = Assert.Single(sheet.Rows, row => row.AttributeCode == "CUPS").Cells.Single();
        Assert.Equal((SpecSources.Catalogue, 6m), (cups.Source, cups.Number));
        Assert.Null(cups.Evidence);

        // An applied claim is a row, not also a sentence; the rows read by label.
        Assert.Empty(sheet.Statements);
        Assert.Equal(["Material", "Tazas"], sheet.Rows.Select(row => row.Label));
    }

    [Fact]
    public async Task Of_two_claims_behind_one_value_the_better_supported_is_shown()
    {
        await ApproveAndApplyAsync(Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.71), heldAs: "ALUMINIUM");
        await ApproveAndApplyAsync(Claim(this.moka, "Cafetera italiana de aluminio", "MATERIAL", "aluminio", 0.96), heldAs: "ALUMINIUM");

        var material = (await SpecsAsync("CAF-001")).Rows.Single(row => row.AttributeCode == "MATERIAL").Cells.Single();

        Assert.Equal(0.96, material.Evidence!.Support);
    }

    [Fact]
    public async Task A_claim_with_no_words_in_the_pages_language_shows_the_words_it_has()
    {
        await ApproveAndApplyAsync(Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93), heldAs: "ALUMINIUM");

        var sheet = await new ProductSpecsHandler(this.facts, this.knowledge)
            .HandleAsync(new ProductSpecsQuery(["CAF-001"], "en"), TestContext.Current.CancellationToken);

        var quote = Assert.Single(sheet.Rows.Single(row => row.AttributeCode == "MATERIAL").Cells.Single().Evidence!.Quotes);
        Assert.Equal("es", quote.Culture);
    }

    [Fact]
    public void Four_products_are_compared_and_a_blank_code_or_an_unknown_language_is_not()
    {
        var validator = new ProductSpecsValidator();

        Assert.True(validator.Validate(new ProductSpecsQuery(["A", "B", "C", "D"], "en")).IsValid);
        Assert.False(validator.Validate(new ProductSpecsQuery(["A", ""], "es")).IsValid);
        Assert.False(validator.Validate(new ProductSpecsQuery(["A", "B"], "fr")).IsValid);
    }

    [Fact]
    public async Task A_comparison_aligns_rows_and_says_no_data_where_a_product_has_none()
    {
        var sheet = await SpecsAsync("CAF-001", "caf-002");

        Assert.Equal(["CAF-001", "CAF-002"], sheet.Products.Select(product => product.Code));
        var material = Assert.Single(sheet.Rows, row => row.AttributeCode == "MATERIAL");
        Assert.Equal([SpecSources.Catalogue, SpecSources.None], material.Cells.Select(cell => cell.Source));
        Assert.Null(material.Cells[1].Value);
        Assert.Equal([6m, 12m], sheet.Rows.Single(row => row.AttributeCode == "CUPS").Cells.Select(cell => cell.Number));
    }

    [Fact]
    public async Task A_proposed_claim_appears_nowhere()
    {
        Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93);
        Claim(this.moka, "asa de baquelita", null, null, 0.9);

        var sheet = await SpecsAsync("CAF-001");

        Assert.All(sheet.Rows.SelectMany(row => row.Cells), cell => Assert.NotEqual(SpecSources.Description, cell.Source));
        Assert.Empty(sheet.Statements);
    }

    /// <summary>
    /// A reimport set the material to steel after the claim applied aluminium:
    /// the row shows steel from the catalogue, and the aluminium sentence backs
    /// nothing — it is not shown beside a value it does not support.
    /// </summary>
    [Fact]
    public async Task A_quote_is_never_shown_beside_a_value_it_did_not_put_there()
    {
        await ApproveAndApplyAsync(Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93), heldAs: "STEEL_SOMEWHERE_ELSE");

        var material = (await SpecsAsync("CAF-001")).Rows.Single(row => row.AttributeCode == "MATERIAL").Cells.Single();

        Assert.Equal(SpecSources.Catalogue, material.Source);
        Assert.Null(material.Evidence);
    }

    [Fact]
    public async Task A_sentence_and_a_value_the_catalogue_refused_are_statements_not_rows()
    {
        var handle = Claim(this.moka, "asa de baquelita", null, null, 0.9);
        handle.Approve("Ana", this.clock);
        var refused = Claim(this.moka, "6 tazas", "MATERIAL", "6 tazas", 0.7);
        await ApproveAndApplyAsync(refused, heldAs: null, refusal: "value_rejected");

        var sheet = await SpecsAsync("CAF-001");

        Assert.Equal(2, sheet.Statements.Count);
        Assert.All(sheet.Statements, statement => Assert.Equal(this.moka.ToString(), statement.ProductId));
        Assert.Contains(sheet.Statements, statement => statement.Text == "Tiene asa de baquelita." && statement.Evidence.Support == 0.9);
    }

    /// <summary>
    /// Found in the first live run: "glass carafe" under "jarra de vidrio" on a
    /// Spanish page. The page's language when the claim has words in it, and
    /// every quote when it has none — never no evidence at all.
    /// </summary>
    [Fact]
    public async Task Quotes_come_in_the_pages_language_when_the_claim_has_words_in_it()
    {
        var spanish = SourceDocument.Describe(this.moka, "es", MokaText, this.clock);
        var english = SourceDocument.Describe(this.moka, "en", "Aluminium stovetop coffee maker for 6 cups.", this.clock);
        this.knowledge.Add(spanish);
        this.knowledge.Add(english);
        var claim = ProductClaim.Propose(
            this.moka,
            LocalizedText.From("es", "Es de aluminio.").With("en", "It is aluminium."),
            "MATERIAL", "aluminio",
            [new Evidence(spanish.Id, spanish.Locate("de aluminio")!, 0.95), new Evidence(english.Id, english.Locate("Aluminium")!, 0.9)],
            new Provenance("rules", null, null, null, "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        await ApproveAndApplyAsync(claim, heldAs: "ALUMINIUM");

        var sheet = await new ProductSpecsHandler(this.facts, this.knowledge)
            .HandleAsync(new ProductSpecsQuery(["CAF-001"], "en"), TestContext.Current.CancellationToken);

        var quote = Assert.Single(sheet.Rows.Single(row => row.AttributeCode == "MATERIAL").Cells.Single().Evidence!.Quotes);
        Assert.Equal(("en", "Aluminium"), (quote.Culture, quote.Text));
        // The claim's support is still its weakest quote's, whichever language is shown.
        Assert.Equal(0.9, sheet.Rows.Single(row => row.AttributeCode == "MATERIAL").Cells.Single().Evidence!.Support);
    }

    [Fact]
    public async Task A_product_a_shopper_cannot_see_is_simply_not_there()
    {
        var sheet = await SpecsAsync("DRAFT-404");

        Assert.Empty(sheet.Products);
        Assert.Empty(sheet.Rows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void One_to_four_products_are_compared(int count) =>
        Assert.False(new ProductSpecsValidator().Validate(
            new ProductSpecsQuery([.. Enumerable.Range(1, count).Select(n => $"CAF-{n:000}")], "es")).IsValid);

    [Fact]
    public async Task Recording_catalogues_answer_needs_a_claim_that_is_still_approved()
    {
        var superseded = Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93);
        superseded.Approve("Ana", this.clock);
        superseded.Supersede(this.clock);

        var recorded = await new RecordClaimApplicationHandler(this.knowledge, this.knowledge)
            .HandleAsync(new RecordClaimApplicationCommand(superseded.Id, "ALUMINIUM", null), TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.Null(superseded.AppliedAs);
    }

    [Fact]
    public async Task Catalogs_answer_is_saved()
    {
        var claim = Claim(this.moka, "de aluminio", "MATERIAL", "aluminio", 0.93);
        var saves = this.knowledge.SaveCount;

        await ApproveAndApplyAsync(claim, heldAs: "ALUMINIUM");

        Assert.Equal((saves + 1, "ALUMINIUM"), (this.knowledge.SaveCount, claim.AppliedAs));
    }

    private Task<SpecSheet> SpecsAsync(params string[] codes) =>
        new ProductSpecsHandler(this.facts, this.knowledge).HandleAsync(new ProductSpecsQuery(codes, "es"), TestContext.Current.CancellationToken);

    private async Task ApproveAndApplyAsync(ProductClaim claim, string? heldAs, string? refusal = null)
    {
        claim.Approve("Ana", this.clock);
        Assert.True(await new RecordClaimApplicationHandler(this.knowledge, this.knowledge)
            .HandleAsync(new RecordClaimApplicationCommand(claim.Id, heldAs, refusal), TestContext.Current.CancellationToken));
    }

    private ProductClaim Claim(ProductId product, string quote, string? attribute, string? value, double support)
    {
        var document = SourceDocument.Describe(product, "es", MokaText, this.clock);
        if (this.knowledge.Documents.All(existing => existing.Id != document.Id))
            this.knowledge.Add(document);

        var claim = ProductClaim.Propose(
            product,
            LocalizedText.From("es", $"Tiene {quote}.").With("en", $"It has {quote}."),
            attribute, value,
            [new Evidence(document.Id, document.Locate(quote)!, support)],
            new Provenance("rules", null, null, null, "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    private static ProductFact Fact(string code, string label, string? value, string key, decimal? number = null) =>
        new(code, label, number is null ? FactKind.Option : FactKind.Number, value, number, null, null, key);

    private sealed class FixedFacts : IProductFacts
    {
        private readonly List<ProductFacts> products = [];

        public void Add(ProductId id, string code, string name, params ProductFact[] attributes) =>
            this.products.Add(new ProductFacts(id, code, name, name.ToLowerInvariant().Replace(' ', '-'), attributes));

        public Task<IReadOnlyList<ProductFacts>> OfAsync(IReadOnlyList<string> productCodes, string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductFacts>>(
                [.. productCodes.Select(code => this.products.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))).OfType<ProductFacts>()]);
    }
}
