using System.Text.Json;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Inventory.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

public static class ShopifyServiceCollectionExtensions
{
    private const string Client = "shopify";

    /// <summary>
    /// The Shopify source, registered only when a store's credentials are
    /// configured (spec, *The degraded path*): without them <c>shopify</c> is not a
    /// source at all, asking for it is the import's ordinary 400 naming the ones
    /// that are, and a fresh clone runs exactly as before.
    /// </summary>
    /// <remarks>Composed by the host beside <c>AddCatalog</c>: Shopify is an
    /// adapter of the catalogue's port, and the catalogue does not know it.</remarks>
    public static IServiceCollection AddShopify(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ShopifyOptions.Section);
        if (section.Get<ShopifyOptions>() is not { IsConfigured: true })
            return services;

        services.Configure<ShopifyOptions>(section);
        services.PostConfigure<ShopifyOptions>(options =>
        {
            Merge(options.CategoriesFile, options.Categories);
            Merge(options.LocationsFile, options.Locations);
        });

        services.AddHttpClient(Client);
        // One token for the process: it is a 24-hour credential, not a request's.
        services.AddSingleton(provider => new ShopifyAccessTokens(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(Client),
            provider.GetRequiredService<IOptions<ShopifyOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider => new ShopifyAdmin(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(Client),
            provider.GetRequiredService<ShopifyAccessTokens>(),
            provider.GetRequiredService<IOptions<ShopifyOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<ICatalogSourceConnector, ShopifyCatalogConnector>(ShopifyCatalogConnector.Key);
        services.AddKeyedScoped<IStockSourceConnector, ShopifyStockConnector>(ShopifyStockConnector.Key);
        services.AddKeyedScoped<ICustomerSourceConnector, ShopifyCustomerConnector>(ShopifyCustomerConnector.Key);

        return services;
    }

    /// <summary>A mapping a person wrote, from its file into the options.</summary>
    private static void Merge(string? file, Dictionary<string, string> into)
    {
        if (file is not { Length: > 0 })
            return;

        foreach (var (shopify, code) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [])
            into[shopify] = code;
    }
}
