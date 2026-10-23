using System.Net;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// A Shopify store's old addresses, as the API answers them (phase 16, P16-11):
/// every shape Shopify writes — with the language in front or not, a product
/// under a collection — is a 301 with no token, to the storefront's page in the
/// language the path or the browser names. What the store never had is a 404.
/// </summary>
public sealed class SourceAddressEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/products/moka", "es", "/es/p/cafetera-moka/7KQ2M9X4TB")]
    [InlineData("/products/moka", "en", "/en/p/moka-coffee-maker/7KQ2M9X4TB")]
    [InlineData("/en/products/moka", "es", "/en/p/moka-coffee-maker/7KQ2M9X4TB")]
    [InlineData("/collections/cocina/products/moka", "es", "/es/p/cafetera-moka/7KQ2M9X4TB")]
    [InlineData("/collections/menaje", "es", "/es/c/COOKWARE")]
    [InlineData("/es/collections/menaje", "en", "/es/c/COOKWARE")]
    public async Task An_old_address_is_a_permanent_redirect_to_the_page_here(string path, string browser, string expected)
    {
        using var client = AClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(browser);

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    [Theory]
    [InlineData("/products/unknown")]
    [InlineData("/collections/unknown")]
    public async Task An_address_the_store_never_had_is_not_found(string path)
    {
        using var client = AClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpClient AClient()
    {
        var factory = new TenderoApiFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISourceAddresses, Addresses>()));
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    /// <summary>One product on sale, "moka", and one collection, "menaje", as an import kept them.</summary>
    private sealed class Addresses : ISourceAddresses
    {
        private static readonly LocalizedText Slug = LocalizedText.From("es", "cafetera-moka").With("en", "moka-coffee-maker");

        public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) =>
            Task.FromResult(handle == "moka" ? new ProductAddress("7KQ2M9X4TB", Slug, ProductStatus.Active) : null);

        public Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
            Task.FromResult(collectionHandle == "menaje" ? "COOKWARE" : null);
    }
}
