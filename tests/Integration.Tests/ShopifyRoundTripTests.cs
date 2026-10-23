using System.Text.Json;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Shopify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The round trip of phase 16 (P16-6, spec *Testing*): the seed filled into a
/// Shopify development store by <c>tools/shopify-seed/seed.mjs</c>, read back
/// through the Shopify adapter, and compared with the seed product by product,
/// field by field. Both go through the same mapper afterwards, so equal
/// <see cref="ExternalProduct"/>s are equal products.
///
/// It runs only where the store's credentials are — the AppHost's user secrets,
/// the same three the API is given — and is skipped everywhere else, CI included.
/// The seeding script gives each product its item id, lowercased, as its handle;
/// that is how the two sides are paired.
///
/// What Shopify cannot hold is said here, by name, rather than compared:
/// <list type="bullet">
/// <item>the external id (Shopify's own number) and each variant's key;</item>
/// <item>the date it became available: Shopify's is when it was published to a
/// sales channel, set by Shopify and never in the past;</item>
/// <item>the pictures' locations (Shopify's CDN against the seed's files) —
/// their number and what each shows are compared.</item>
/// </list>
/// </summary>
public sealed class ShopifyRoundTripTests
{
    private static string Seed(string file)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, file);
    }

    /// <summary>The AppHost's user secrets, read as a file: no package for one read.</summary>
    private static Dictionary<string, string>? StoreSecrets()
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", "tendero-apphost", "secrets.json");
        if (!File.Exists(file))
            return null;

        var all = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file)) ?? [];
        string? Get(string key) => all.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return Get("Parameters:shopify-store") is { } store && Get("Parameters:shopify-client-id") is { } id && Get("Parameters:shopify-client-secret") is { } secret
            ? new() { ["Shopify:Store"] = store, ["Shopify:ClientId"] = id, ["Shopify:ClientSecret"] = secret }
            : null;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_seed_read_back_from_a_shopify_store_is_the_seed()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;

        await using var provider = Services(secrets!);
        await using var scope = provider.CreateAsyncScope();

        var definitions = await scope.ServiceProvider.GetRequiredService<IAttributeDefinitionReader>().AllAsync(ct);
        var seed = await All(scope.ServiceProvider.GetRequiredKeyedService<ICatalogSourceConnector>("seed"), ct);
        var shopify = scope.ServiceProvider.GetRequiredKeyedService<ICatalogSourceConnector>("shopify");
        var store = (await All(shopify, ct)).ToDictionary(product => product.Handle!.ToUpperInvariant());

        var differences = new List<string>();
        foreach (var expected in seed)
        {
            if (!store.TryGetValue(expected.ExternalId.ToUpperInvariant(), out var actual))
            {
                differences.Add($"{expected.ExternalId}: not in the store");
                continue;
            }

            Compare(differences, expected, actual, definitions);
        }

        Assert.True(differences.Count == 0,
            $"{differences.Count} differences over {seed.Count} products:\n{string.Join('\n', differences)}");
        Assert.Empty(shopify.SourceNotes);
    }

    /// <summary>
    /// The store's collections as the categories the import keeps for the
    /// redirects (P16-11; spec, *Measures*: every known address leads to a live
    /// page). tools/shopify-seed/collections.mjs made one per category that has
    /// products, named for its Spanish name; each must become that category, or
    /// one below it when all its products are in one subcategory — the page
    /// shows the same products. A collection the seeding did not make (Shopify's
    /// own, say) is reported, not judged.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Each_collection_seeded_into_the_store_leads_to_its_category()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;

        await using var provider = Services(secrets!);
        await using var scope = provider.CreateAsyncScope();

        var tree = await scope.ServiceProvider.GetRequiredService<ICategoryReader>().AllAsync(ct);
        var seeded = tree.All.ToDictionary(category => Slugs.Of(category.Name.In("es")), category => category.Code);
        var shopify = scope.ServiceProvider.GetRequiredKeyedService<ICatalogSourceConnector>("shopify");
        var categoryOf = (await All(shopify, ct)).ToDictionary(product => product.ExternalId, product => product.Category);

        var wrong = new List<string>();
        var exact = 0;
        var collections = 0;
        await foreach (var collection in shopify.StreamCollectionsAsync(ct))
        {
            if (!seeded.TryGetValue(collection.Handle, out var expected))
                continue;

            collections++;
            var kept = tree.SharedBy(collection.ProductExternalIds.Select(id => categoryOf.GetValueOrDefault(id)));
            if (kept == expected)
                exact++;
            else if (kept is null || !tree.ByCode(kept)!.Ancestry.Contains(expected))
                wrong.Add($"{collection.Handle}: expected {expected}, kept {kept ?? "nothing"}");
        }

        TestContext.Current.SendDiagnosticMessage($"{exact} of {collections} collections lead to their own category; the rest to one below it.");
        Assert.True(collections > 0, "the store has none of the seeded collections: run tools/shopify-seed/collections.mjs");
        Assert.True(wrong.Count == 0, string.Join('\n', wrong));
    }

    /// <summary>The catalogue and the Shopify source, configured as the API is, over the seed's files.</summary>
    private static ServiceProvider Services(Dictionary<string, string> secrets)
    {
        var settings = new Dictionary<string, string?>(secrets.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
        {
            ["Shopify:CategoriesFile"] = Seed(Path.Combine("tools", "shopify-seed", "categories.json")),
            ["Catalog:Connectors:Seed:FilePath"] = Seed(Path.Combine("seed", "products.sample.json")),
            ["Catalog:Attributes:FilePath"] = Seed(Path.Combine("seed", "attributes.sample.json")),
            ["Catalog:Categories:FilePath"] = Seed(Path.Combine("seed", "categories.sample.json")),
        };
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.AddCatalog(configuration);
        services.AddShopify(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Attributes are compared as the catalogue resolves them: the store holds each
    /// option's label and "sí" where the seed has the supplier's word and "si"
    /// (tools/shopify-seed/attributes.mjs), and both are the same option.
    /// </summary>
    private static void Compare(List<string> differences, ExternalProduct seed, ExternalProduct store, AttributeDefinitions definitions)
    {
        void Same(string field, object? expected, object? actual)
        {
            if (!Equals(expected, actual))
                differences.Add($"{seed.ExternalId} {field}: seed «{expected}», store «{actual}»");
        }

        foreach (var culture in new[] { "es", "en" })
        {
            Same($"name[{culture}]", seed.Names.GetValueOrDefault(culture), store.Names.GetValueOrDefault(culture));
            Same($"description[{culture}]", seed.Descriptions?.GetValueOrDefault(culture), store.Descriptions?.GetValueOrDefault(culture));
        }

        Same("brand", seed.Brand, store.Brand);
        Same("category", seed.Category, store.Category);
        Same("price", seed.Price, store.Price);
        Same("attributes", Flat(ExternalProductMapper.Coordinates(seed.Attributes, definitions)), Flat(ExternalProductMapper.Coordinates(store.Attributes, definitions)));
        Same("variants", Variants(seed, definitions), Variants(store, definitions));
        Same("pictures", seed.Images.Count, store.Images.Count);
        Same("pictures shown", Shown(seed), Shown(store));
        Same("notes", null, store.Notes is { Count: > 0 } notes ? string.Join("; ", notes) : null);
    }

    /// <summary>Each variant as the catalogue will sell it: its SKU, what it varies by, its price.</summary>
    /// A product sold one way is the seed's default variant on one side and
    /// Shopify's "Default Title" variant, which varies by nothing, on the other;
    /// each with its tax class (S2a).
    private static string Variants(ExternalProduct product, AttributeDefinitions definitions) =>
        product.Variants is not { Count: > 0 } variants
            ? $"{product.ExternalId}-DEFAULT [] {product.PriceAmount:0.00} standard"
            : string.Join(" | ", variants
                .Select(variant =>
                    $"{(variant.Sku ?? ExternalProductMapper.SkuOf(product, variant)).ToUpperInvariant()} " +
                    $"[{Flat(ExternalProductMapper.Coordinates(variant.Attributes, definitions))}] {variant.PriceAmount ?? product.PriceAmount:0.00} " +
                    // No class is the standard one (ADR 0028), which is what a store taxes.
                    $"{variant.TaxClass ?? "standard"}")
                .Order(StringComparer.Ordinal));

    private static string Flat(IReadOnlyDictionary<string, string> values) =>
        string.Join(",", values.Select(pair => $"{pair.Key.ToLowerInvariant()}={pair.Value}").Order(StringComparer.Ordinal));

    private static string Shown(ExternalProduct product) =>
        string.Join(" | ", product.Images.Select(image => image.Shows is { } shows ? Flat(shows) : "-").Order(StringComparer.Ordinal));

    private static async Task<List<ExternalProduct>> All(ICatalogSourceConnector connector, CancellationToken ct)
    {
        var products = new List<ExternalProduct>();
        await foreach (var product in connector.StreamProductsAsync(ct))
            products.Add(product);
        return products;
    }
}
