using Elastic.Clients.Elasticsearch;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The Elasticsearch adapters with what they read besides the engine — the
/// attribute definitions, the category tree, the running offers, the clock —
/// answered from memory, so a test states them rather than a seed file.
/// </summary>
internal static class Adapters
{
    public static readonly TestClock Clock = new();

    public static ElasticsearchLexicalSearch Lexical(
        ElasticsearchClient client,
        OfferScope? offers = null,
        AttributeDefinitions? definitions = null,
        CategoryTree? categories = null,
        OfferScope? campaign = null) =>
        new(client, new Definitions(definitions ?? AttributeDefinitions.Empty), new Categories(categories ?? CategoryTree.Empty),
            new Offers(offers ?? OfferScope.Nothing, campaign), Clock);

    public static ElasticsearchHitReader HitReader(ElasticsearchClient client, OfferScope? offers = null) =>
        new(client, new Offers(offers ?? OfferScope.Nothing), Clock);

    public static LocalizedText Text(string es, string en) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    /// <summary>COLOR (an axis), SIZE (an axis), MATERIAL (an option attribute) and INDUCTION (a yes-or-no), all facets, in that order.</summary>
    public static AttributeDefinitions ShopDefinitions()
    {
        var color = AttributeDefinition.Define(Clock, "COLOR", Text("color", "colour"), AttributeKind.Option, isVariantAxis: true, isFacet: true);
        color.AddOption(Clock, "BLACK", Text("negro", "black"), "#201D1B");
        color.AddOption(Clock, "RED", Text("rojo", "red"));

        var size = AttributeDefinition.Define(Clock, "SIZE", Text("talla", "size"), AttributeKind.Option, isVariantAxis: true, isFacet: true);
        size.AddOption(Clock, "40", Text("40", "40"));
        size.AddOption(Clock, "42", Text("42", "42"));

        var material = AttributeDefinition.Define(Clock, "MATERIAL", Text("material", "material"), AttributeKind.Option, isFacet: true);
        material.AddOption(Clock, "ALUMINIUM", Text("aluminio", "aluminium"));
        material.AddOption(Clock, "CAST_IRON", Text("hierro fundido", "cast iron"));

        var induction = AttributeDefinition.Define(Clock, "INDUCTION", Text("inducción", "induction"), AttributeKind.Boolean, isFacet: true);

        return new AttributeDefinitions([color, size, material, induction]);
    }

    /// <summary>HOME › KITCHEN › COOKWARE and APPAREL › SHOES.</summary>
    public static CategoryTree ShopCategories()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
        var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
        var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje de cocina", "Cookware"), kitchen);
        var apparel = Category.Define(Clock, "APPAREL", Text("Ropa", "Clothing"));
        var shoes = Category.Define(Clock, "SHOES", Text("Zapatillas", "Shoes"), apparel);

        return new CategoryTree([home, kitchen, cookware, apparel, shoes]);
    }

    private sealed class Definitions(AttributeDefinitions definitions) : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(definitions);
    }

    private sealed class Categories(CategoryTree tree) : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(tree);
    }

    /// <summary>The running offers as given, and whichever campaign is asked reaching <paramref name="campaign"/> (nothing by default).</summary>
    private sealed class Offers(OfferScope scope, OfferScope? campaign = null) : IRunningOffers
    {
        public Task<OfferScope> NowAsync(CancellationToken ct = default) => Task.FromResult(scope);

        public Task<OfferScope> CampaignNowAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(campaign ?? OfferScope.Nothing);
    }
}
