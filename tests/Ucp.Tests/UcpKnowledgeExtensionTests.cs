using System.Text.Json;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Catalog;
using ElGuerre.Tendero.Ucp.Negotiation;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The shop's own UCP extension, <c>com.elguerre.tendero.knowledge</c> (P11-8): a
/// product's specs with their sources, only when a trusted platform negotiated it,
/// valid against the schema the shop publishes — with negative controls, so a schema
/// that accepts anything is caught.
/// </summary>
[Collection(UcpSchemas.Collection)]
public sealed class UcpKnowledgeExtensionTests
{
    private const string Knowledge = "urn:elguerre:tendero:ucp:com.elguerre.tendero.knowledge#/$defs/knowledge";

    private static readonly SpecSheet Sheet = new(
        [new SpecProduct("p1", "B13BAG0701", "Mochila urbana", "mochila-urbana"), new SpecProduct("p2", "B13BAG0702", "Mochila de senderismo", "mochila-senderismo")],
        [
            new SpecRow("capacity", "Capacidad", [
                new SpecCell(SpecSources.Catalogue, FactKind.Number, "18 L", 18m, "L", null, null),
                new SpecCell(SpecSources.Description, FactKind.Number, "30 L", 30m, "L", null, new SpecEvidence(0.91, [new SpecQuote("es", "capacidad de 30 litros")])),
            ]),
            new SpecRow("waterproof", "Impermeable", [
                new SpecCell(SpecSources.None, null, null, null, null, null, null),
                new SpecCell(SpecSources.Catalogue, FactKind.Boolean, "Sí", null, null, true, null),
            ]),
        ],
        [new SpecStatement("p1", "Tiene un bolsillo antirrobo.", new SpecEvidence(0.88, [new SpecQuote("es", "bolsillo antirrobo en la espalda")]))]);

    [Fact]
    public void Each_product_gets_its_column_known_values_only_and_its_own_statements()
    {
        var byCode = UcpKnowledgeExtension.Of(Sheet).ToDictionary(pair => pair.Code, pair => pair.Knowledge);

        var urban = byCode["B13BAG0701"];
        Assert.Equal([("capacity", "18 L", "catalogue")], urban.Specs.Select(spec => (spec.Attribute, spec.Value, spec.Source)));
        Assert.Equal("Tiene un bolsillo antirrobo.", Assert.Single(urban.Statements).Text);

        var hiking = byCode["B13BAG0702"];
        Assert.Equal(["capacity", "waterproof"], hiking.Specs.Select(spec => spec.Attribute));
        Assert.Equal((0.91, "capacidad de 30 litros"), (hiking.Specs[0].Evidence!.Support, hiking.Specs[0].Evidence!.Quotes.Single()));
        Assert.Empty(hiking.Statements);
    }

    [Fact]
    public void What_it_attaches_validates_against_the_schema_the_shop_publishes()
    {
        foreach (var (code, knowledge) in UcpKnowledgeExtension.Of(Sheet))
            UcpSchemas.Validate(Knowledge, JsonSerializer.SerializeToElement(knowledge), $"The knowledge of {code}");
    }

    [Theory]
    [InlineData("a value from the description with no evidence",
        """{"specs":[{"attribute":"capacity","label":"Capacidad","value":"30 L","source":"description"}],"statements":[]}""")]
    [InlineData("a source the extension does not define",
        """{"specs":[{"attribute":"capacity","label":"Capacidad","value":"30 L","source":"guessed"}],"statements":[]}""")]
    [InlineData("support above 1",
        """{"specs":[],"statements":[{"text":"x","evidence":{"support":1.2,"quotes":["x"]}}]}""")]
    public void A_knowledge_member_that_breaks_the_schema_is_rejected(string what, string json) =>
        Assert.False(UcpSchemas.Get(Knowledge).Evaluate(JsonDocument.Parse(json).RootElement).IsValid,
            $"Knowledge with {what} must not validate — if it does, the schema is not being applied.");

    [Fact]
    public void An_attached_page_names_the_extension_and_a_product_nobody_knows_carries_an_empty_member()
    {
        var page = new UcpCatalogPage
        {
            Ucp = new UcpEnvelope { Version = UcpSpecification.Version },
            Products = [Product("B13BAG0701"), Product("UNKNOWN")],
        };

        var attached = UcpKnowledgeExtension.Attach(page, UcpKnowledgeExtension.Of(Sheet).ToDictionary(pair => pair.Code, pair => pair.Knowledge));

        Assert.Equal([KnowledgeCapability.Name], attached.Ucp.Capabilities!.Keys);
        Assert.Single(attached.Products[0].Knowledge!.Specs);
        Assert.Empty(attached.Products[1].Knowledge!.Specs);
    }

    [Fact]
    public async Task Only_a_trusted_profile_that_speaks_the_extension_turns_it_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var speaks = new PlatformProfile(new Dictionary<string, IReadOnlySet<string>>
        {
            ["dev.ucp.shopping.catalog.lookup"] = new HashSet<string> { UcpSpecification.Version },
            [KnowledgeCapability.Name] = new HashSet<string> { new KnowledgeCapability().Version },
        });
        var silent = new PlatformProfile(new Dictionary<string, IReadOnlySet<string>>
        {
            ["dev.ucp.shopping.catalog.lookup"] = new HashSet<string> { UcpSpecification.Version },
        });

        Assert.True(await UcpKnowledgeExtension.NegotiatedAsync("https://agent.example/p", new Fixed(speaks), ct));
        Assert.False(await UcpKnowledgeExtension.NegotiatedAsync("https://agent.example/p", new Fixed(silent), ct));
        Assert.False(await UcpKnowledgeExtension.NegotiatedAsync("https://evil.example/p", new Fixed(null), ct));
        Assert.False(await UcpKnowledgeExtension.NegotiatedAsync(null, new Fixed(speaks), ct));
    }

    private static UcpProduct Product(string code) => new()
    {
        Id = code,
        Title = code,
        Description = new UcpDescription { Text = "" },
        PriceRange = new UcpPriceRange { Min = new UcpPrice { Amount = 100, Currency = "EUR" }, Max = new UcpPrice { Amount = 100, Currency = "EUR" } },
        Variants = [],
    };

    private sealed class Fixed(PlatformProfile? profile) : IPlatformProfiles
    {
        public Task<(PlatformProfile? Profile, ProfileProblem? Problem)> GetAsync(string profileUrl, CancellationToken cancellationToken) =>
            Task.FromResult((profile, profile is null ? ProfileProblem.InvalidUrl : (ProfileProblem?)null));
    }
}
