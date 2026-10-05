using System.Text.Json;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>What the storefront's assistant may read of the shop's knowledge (phase 12b).</summary>
public sealed class ProductSpecsToolTests
{
    /// <summary>The dispatcher, answering with a sheet and keeping the query.</summary>
    private sealed class Shop(SpecSheet sheet) : IQueryDispatcher
    {
        public ProductSpecsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (ProductSpecsQuery)(object)query;
            return Task.FromResult((TResult)(object)sheet);
        }
    }

    private static readonly SpecCell None = new(SpecSources.None, null, null, null, null, null, null);

    private static SpecCell Held(string? value = null, decimal? number = null, string? unit = null, bool? flag = null) =>
        new(SpecSources.Catalogue, FactKind.Text, value, number, unit, flag, null);

    private static SpecCell Approved(bool flag, double support, params string[] quotes) =>
        new(SpecSources.Description, FactKind.Boolean, null, null, null, flag,
            new SpecEvidence(support, [.. quotes.Select(quote => new SpecQuote("es", quote))]));

    /// <summary>Two pans: the aluminium one's induction was approved from its description; the iron one has no data for it.</summary>
    private static SpecSheet Pans() => new(
        [new SpecProduct("id-alu", "PAN-ALU-24", "Sartén de aluminio", "sarten-de-aluminio"), new SpecProduct("id-iron", "PAN-IRON-26", "Sartén de hierro", "sarten-de-hierro")],
        [
            new SpecRow("INDUCTION", "Apta para inducción", [Approved(true, 0.97, "base de acero para inducción"), None]),
            new SpecRow("MATERIAL", "Material", [Held("aluminio"), Held("hierro fundido")]),
            new SpecRow("WEIGHT", "Peso", [Held(number: 0.80m, unit: "kg"), Held(number: 2.3m, unit: "kg")]),
            new SpecRow("DISHWASHER", "Apta para lavavajillas", [None, Held(flag: false)]),
        ],
        [new SpecStatement("id-iron", "Gana antiadherencia con el uso.", new SpecEvidence(0.91, [new SpecQuote("es", "gana antiadherencia con el uso")]))]);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Task<ReadAnswer> Read(SpecSheet sheet, string arguments = """{"codes":["PAN-ALU-24","PAN-IRON-26"],"culture":"es"}""") =>
        new ProductSpecsTool(new Shop(sheet)).ReadAsync(Arguments(arguments), TestContext.Current.CancellationToken);

    [Fact]
    public async Task It_is_a_shoppers_read_of_the_sheet_the_product_page_reads()
    {
        var shop = new Shop(Pans());
        var tool = new ProductSpecsTool(shop);

        await tool.ReadAsync(Arguments("""{"codes":["PAN-ALU-24"],"culture":"en"}"""), TestContext.Current.CancellationToken);

        Assert.Equal((ToolAudience.Shopper, "get_product_specs"), (tool.Audience, tool.Name));
        Assert.Equal(["PAN-ALU-24"], shop.Asked!.Codes);
        Assert.Equal("en", shop.Asked.Culture);
    }

    [Fact]
    public async Task Each_product_has_a_line_for_what_the_shop_holds_of_it_and_none_for_what_it_does_not()
    {
        var specs = Assert.IsType<SpecsForShopper>((await Read(Pans())).Data);

        Assert.Equal(
            ["Apta para inducción: sí", "Material: aluminio", "Peso: 0.8 kg"],
            specs.Products[0].Specs.Select(spec => spec.Text));
        // No line for induction — the shop does not hold it — which is not a "no"; the dishwasher's is one.
        Assert.Equal(
            ["Material: hierro fundido", "Peso: 2.3 kg", "Apta para lavavajillas: no", "Gana antiadherencia con el uso."],
            specs.Products[1].Specs.Select(spec => spec.Text));
    }

    /// <summary>The source is what the shopper is shown beside a sentence: the two must not be confused.</summary>
    [Fact]
    public async Task What_a_person_approved_is_a_claim_with_its_evidence_and_what_the_catalogue_holds_is_the_data_sheets()
    {
        var facts = (await Read(Pans())).Facts!;

        Assert.Equal(7, facts.Count);
        var induction = facts.Single(fact => fact.Text == "Apta para inducción: sí");
        Assert.Equal(("PAN-ALU-24", FactSource.Claim, 0.97), (induction.Subject, induction.Source, induction.Support));
        Assert.Equal(["base de acero para inducción"], induction.Quotes);

        var weight = facts.Single(fact => fact.Text == "Peso: 2.3 kg");
        Assert.Equal(("PAN-IRON-26", FactSource.DataSheet, (double?)null), (weight.Subject, weight.Source, weight.Support));
        Assert.Null(weight.Quotes);

        // A statement is its product's, by its id, and a claim.
        var statement = facts.Single(fact => fact.Text == "Gana antiadherencia con el uso.");
        Assert.Equal(("PAN-IRON-26", FactSource.Claim, 0.91), (statement.Subject, statement.Source, statement.Support));
    }

    [Theory]
    [InlineData("es", "Apta para inducción: sí")]
    [InlineData("en", "Apta para inducción: yes")]
    public async Task A_flag_is_a_word_of_the_language_asked_for(string culture, string expected)
    {
        var answer = await Read(Pans(), $$"""{"codes":["PAN-ALU-24"],"culture":"{{culture}}"}""");

        Assert.Contains(answer.Facts!, fact => fact.Text == expected);
    }

    [Fact]
    public async Task The_rows_are_the_lines_of_every_product()
    {
        var answer = await Read(Pans());

        Assert.Equal((7, (int?)null), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task Codes_no_product_on_sale_has_are_the_models_mistake()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => Read(new SpecSheet([], [], []), """{"codes":["NOPE-1"]}"""));

        Assert.Contains("search_products", failure.Message);
    }
}
