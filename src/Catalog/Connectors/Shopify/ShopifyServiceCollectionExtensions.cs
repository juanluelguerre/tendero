using System.Text.Json;
using ElGuerre.Tendero.Catalog.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

internal static class ShopifyServiceCollectionExtensions
{
    private const string Client = "shopify";

    /// <summary>
    /// The Shopify source, registered only when a store's credentials are
    /// configured (spec, *The degraded path*): without them <c>shopify</c> is not a
    /// source at all, asking for it is the import's ordinary 400 naming the ones
    /// that are, and a fresh clone runs exactly as before.
    /// </summary>
    public static IServiceCollection AddShopifySource(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ShopifyOptions.Section);
        if (section.Get<ShopifyOptions>() is not { IsConfigured: true })
            return services;

        services.Configure<ShopifyOptions>(section);
        services.PostConfigure<ShopifyOptions>(options =>
        {
            if (options.CategoriesFile is { Length: > 0 } file)
            {
                foreach (var (shopify, code) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [])
                    options.Categories[shopify] = code;
            }
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

        return services;
    }
}
