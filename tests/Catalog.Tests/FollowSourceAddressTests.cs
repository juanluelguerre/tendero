using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.FollowSourceAddress;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// A store's old addresses after the shop has left it (phase 16, P16-11; spec
/// decision 7): <c>/products/&lt;handle&gt;</c> leads to the product's page and
/// <c>/collections/&lt;handle&gt;</c> to the category's, in the language asked.
/// Only to a page that is there: a permanent redirect to a page that answers
/// 404 is cached by every browser and crawler that follows it.
/// </summary>
public sealed class FollowSourceAddressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalizedText Slug = LocalizedText.From("es", "cafetera-moka").With("en", "moka-coffee-maker");

    private static FollowSourceAddressHandler Handler(ProductStatus status = ProductStatus.Active) =>
        new(new Addresses(new ProductAddress("7KQ2M9X4TB", Slug, status)));

    [Theory]
    [InlineData("es", "/es/p/cafetera-moka/7KQ2M9X4TB")]
    [InlineData("en", "/en/p/moka-coffee-maker/7KQ2M9X4TB")]
    public async Task A_products_old_address_leads_to_its_page_in_the_language_asked(string culture, string expected)
    {
        var location = await Handler().HandleAsync(new FollowSourceAddressQuery(SourceAddressKind.Product, "moka", culture), Ct);

        Assert.Equal(expected, location);
    }

    /// <summary>A handle is the same name in any case: Shopify answers <c>/products/Moka</c> too.</summary>
    [Fact]
    public async Task A_handle_is_matched_in_any_case()
    {
        var location = await Handler().HandleAsync(new FollowSourceAddressQuery(SourceAddressKind.Product, "MOKA", "es"), Ct);

        Assert.Equal("/es/p/cafetera-moka/7KQ2M9X4TB", location);
    }

    /// <summary>Imported and not yet published, or withdrawn: there is no page to send anyone to.</summary>
    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Archived)]
    public async Task A_product_that_is_not_on_sale_leads_nowhere(ProductStatus status)
    {
        var location = await Handler(status).HandleAsync(new FollowSourceAddressQuery(SourceAddressKind.Product, "moka", "es"), Ct);

        Assert.Null(location);
    }

    [Fact]
    public async Task A_collections_old_address_leads_to_its_categorys_page()
    {
        var location = await Handler().HandleAsync(new FollowSourceAddressQuery(SourceAddressKind.Collection, "menaje", "en"), Ct);

        Assert.Equal("/en/c/COOKWARE", location);
    }

    [Theory]
    [InlineData(SourceAddressKind.Product)]
    [InlineData(SourceAddressKind.Collection)]
    public async Task An_address_the_store_never_had_leads_nowhere(SourceAddressKind kind)
    {
        var location = await Handler().HandleAsync(new FollowSourceAddressQuery(kind, "unknown", "es"), Ct);

        Assert.Null(location);
    }

    /// <summary>One product, "moka", and one collection, "menaje" (cookware), as the import kept them.</summary>
    private sealed class Addresses(ProductAddress moka) : ISourceAddresses
    {
        public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) =>
            Task.FromResult(handle == "moka" ? moka : null);

        public Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
            Task.FromResult(collectionHandle == "menaje" ? "COOKWARE" : null);
    }
}
