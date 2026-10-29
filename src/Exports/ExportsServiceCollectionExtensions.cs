using ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;
using ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Exports;

public static class ExportsServiceCollectionExtensions
{
    /// <summary>
    /// The exports and their formats (phase 16, E-1). Each format registers keyed by
    /// its name for its kind of row; a slice finds it through <see cref="IExportFormats"/>.
    /// The contexts' read ports it reads through are registered by the contexts.
    /// </summary>
    public static IServiceCollection AddExports(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExportsOptions>(configuration.GetSection(ExportsOptions.SectionName));
        services.AddScoped<IExportFormats, KeyedExportFormats>();

        // The product feed (E-2): Merchant Center's format, and the category mapping a person wrote.
        services.AddKeyedSingleton<IExportFormat<FeedItem>, GoogleMerchantFeed>(GoogleMerchantFeed.Key);
        services.AddSingleton(provider => GoogleCategoryMapping.Load(provider.GetRequiredService<IOptions<ExportsOptions>>().Value.GoogleCategoriesFile));

        // The catalogue for another platform (E-3): Shopify's product CSV.
        services.AddKeyedSingleton<IExportFormat<ProductForAnotherPlatform>, ShopifyProductCsv>(ShopifyProductCsv.Key);

        // Orders for the accountant (E-4): a spreadsheet in the reader's language. XLSX waits for its evidence (decision 3).
        services.AddKeyedSingleton<IExportFormat<OrderLineForTheBooks>, OrdersForTheBooksCsv>(OrdersForTheBooksCsv.Key);

        return services;
    }
}
