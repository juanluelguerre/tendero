using Microsoft.Extensions.Configuration;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The Shopify development store as the corpus's source (phase 16, P16-6,
/// <c>--source shopify</c>): the same three secrets the AppHost gives the API,
/// read from its user secrets, and the category mapping a person approved.
/// The relevance gate itself always measures the seed; this measures the round
/// trip — whether a catalogue read back from a store still ranks as the seed does.
/// </summary>
internal static class ShopifySource
{
    public static void Configure(ConfigurationManager configuration)
    {
        var secrets = new ConfigurationBuilder().AddUserSecrets("tendero-apphost").Build();

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Shopify:Store"] = secrets["Parameters:shopify-store"],
            ["Shopify:ClientId"] = secrets["Parameters:shopify-client-id"],
            ["Shopify:ClientSecret"] = secrets["Parameters:shopify-client-secret"],
            ["Shopify:CategoriesFile"] = Path.Combine("tools", "shopify-seed", "categories.json"),
        });
    }
}
