namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

/// <summary>
/// What the store says about itself that the import needs: the currency its
/// prices are in, its own language — the base its texts are written in — and
/// the languages it publishes, of which Tendero keeps the ones it speaks.
/// </summary>
internal sealed record ShopifyShop(string Currency, string PrimaryLocale, IReadOnlyList<string> Locales)
{
    public static async Task<ShopifyShop> ReadAsync(ShopifyAdmin admin, CancellationToken ct)
    {
        var data = await admin.QueryAsync(ShopifyQueries.Shop, ct);
        var locales = data.GetProperty("shopLocales").EnumerateArray()
            .Select(locale => (Code: locale.GetProperty("locale").GetString()!, Primary: locale.GetProperty("primary").GetBoolean()))
            .ToList();

        return new ShopifyShop(
            data.GetProperty("shop").GetProperty("currencyCode").GetString()!,
            locales.FirstOrDefault(locale => locale.Primary).Code ?? locales.FirstOrDefault().Code ?? "en",
            [.. locales.Select(locale => locale.Code)]);
    }
}
