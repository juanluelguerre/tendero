using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// POST /api/search/by-image, GET /api/search/pictures and the status the
/// storefront reads first (phase 13a, I-6): anyone may ask, so what bounds them
/// is how much is asked; and a shop without the model or the store says so
/// rather than failing (invariant 8).
/// </summary>
public sealed class VisualSearchEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nobody_signed_in_searches_with_a_picture_and_gets_the_shops_cards_in_their_language()
    {
        var (client, model, owner) = Compose(new PictureMatch("lamp", "img-lamp", 0.91), new PictureMatch("stool", "img-stool", 0.74));
        using var _ = owner;

        var response = await client.SendAsync(WithPicture(APicture(), "/api/search/by-image?limit=5", language: "en-GB"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        Assert.Equal(["lamp", "stool"], items.EnumerateArray().Select(item => item.GetProperty("product").GetProperty("productId").GetString()));
        Assert.Equal(("img-lamp", 0.91), (items[0].GetProperty("picture").GetString(), items[0].GetProperty("similarity").GetDouble()));
        Assert.Equal(1, model.Pictures);
    }

    /// <summary>
    /// The path with no AI in it, which is every fresh clone: the endpoint says
    /// visual search is not here, the status said so first, and neither is a 500.
    /// </summary>
    [Fact]
    public async Task A_shop_started_without_visual_search_says_so_before_and_when_it_is_asked()
    {
        using var factory = new TenderoApiFactory();
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<VisualSearchAvailability>("/api/search/by-image/status", Ct);
        var byPicture = await client.SendAsync(WithPicture(APicture()), Ct);
        var byWords = await client.GetAsync("/api/search/pictures?q=mesa redonda", Ct);

        Assert.False(status!.Available);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, byPicture.StatusCode);
        Assert.Contains("not available here", (await byPicture.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, byWords.StatusCode);
    }

    [Fact]
    public async Task With_the_model_and_the_store_the_status_says_a_picture_can_be_searched_with()
    {
        var (client, _, owner) = Compose();
        using var _ = owner;

        Assert.True((await client.GetFromJsonAsync<VisualSearchAvailability>("/api/search/by-image/status", Ct))!.Available);
    }

    /// <summary>
    /// The folder of the export without a vector store is a model with nothing
    /// to compare against: the control must not be offered on half the feature.
    /// </summary>
    [Fact]
    public async Task The_model_alone_is_not_visual_search()
    {
        using var root = new TenderoApiFactory();
        var client = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IImageEmbeddingGenerator>(new Model()))).CreateClient();

        Assert.False((await client.GetFromJsonAsync<VisualSearchAvailability>("/api/search/by-image/status", Ct))!.Available);
    }

    /// <summary>The store's own words name a host and a port; they stay in the log.</summary>
    [Fact]
    public async Task A_store_that_is_down_is_a_503_that_says_nothing_of_the_store()
    {
        var (client, _, owner) = Compose(failure: new SearchUnavailableException("picture search", "RpcException: failed to connect to 10.0.0.7:6334"));
        using var _ = owner;

        var response = await client.SendAsync(WithPicture(APicture()), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not answering right now", body, StringComparison.Ordinal);
        Assert.DoesNotContain("6334", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_not_a_picture_is_refused_at_the_door_and_the_model_is_never_asked()
    {
        var (client, model, owner) = Compose();
        using var _ = owner;

        var response = await client.SendAsync(WithPicture("<svg onload='alert(1)'/>"u8.ToArray(), contentType: "image/png"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("JPEG, a PNG or a WebP", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, model.Pictures);
    }

    [Fact]
    public async Task A_picture_heavier_than_the_bound_is_refused()
    {
        var (client, model, owner) = Compose();
        using var _ = owner;

        var response = await client.SendAsync(WithPicture(new byte[UploadedPicture.MaxBytes + 1]), Ct);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"answered {(int)response.StatusCode}");
        Assert.Equal(0, model.Pictures);
    }

    [Fact]
    public async Task Words_against_the_pictures_are_embedded_as_text_and_answered_as_cards()
    {
        var (client, model, owner) = Compose(new PictureMatch("table", "img-table", 0.14));
        using var _ = owner;

        var response = await client.GetAsync("/api/search/pictures?q=mesa redonda de madera&culture=es", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray());
        Assert.Equal("table", item.GetProperty("product").GetProperty("productId").GetString());
        Assert.Equal((0, 1, "mesa redonda de madera"), (model.Pictures, model.Texts, model.LastText));
    }

    [Fact]
    public async Task One_letter_is_not_a_question_for_the_pictures()
    {
        var (client, model, owner) = Compose();
        using var _ = owner;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search/pictures?q=m", Ct)).StatusCode);
        Assert.Equal(0, model.Texts);
    }

    /// <summary>One budget for both questions — each runs a model here — and twelve a minute of it.</summary>
    [Fact]
    public async Task The_thirteenth_visual_search_in_a_minute_is_refused_whichever_question_it_is()
    {
        var (client, _, owner) = Compose();
        using var _ = owner;

        for (var asked = 0; asked < 6; asked++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithPicture(APicture()), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/search/pictures?q=lamp", Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(WithPicture(APicture()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/search/pictures?q=lamp", Ct)).StatusCode);

        // Asking whether it can be asked costs nothing.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/search/by-image/status", Ct)).StatusCode);
    }

    /// <summary>Public is a decision written in the code (invariant 10), and these three are that decision.</summary>
    [Theory]
    [InlineData("/api/search/by-image")]
    [InlineData("/api/search/by-image/status")]
    [InlineData("/api/search/pictures")]
    public void The_visual_endpoints_are_public_by_decision(string route)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static (HttpClient Client, Model Model, IDisposable Owner) Compose(params PictureMatch[] near) => Compose(null, near);

    private static (HttpClient Client, Model Model, IDisposable Owner) Compose(Exception? failure, params PictureMatch[] near)
    {
        var model = new Model();
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var pictures = new Pictures(near) { Failure = failure };
            services.AddSingleton<IImageEmbeddingGenerator>(model);
            services.AddScoped<IPictureSearch>(_ => pictures);
            services.AddScoped<IPictureVectorIndexer>(_ => pictures);
            services.AddScoped<IProductProfileReader>(_ => new Index());
        }));
        return (factory.CreateClient(), model, root);
    }

    private static HttpRequestMessage WithPicture(
        byte[] bytes, string route = "/api/search/by-image", string contentType = "image/png", string? language = null)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new MultipartFormDataContent { { file, "picture", "photo.png" } } };
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);
        return request;
    }

    private static byte[] APicture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class Model : IImageEmbeddingGenerator
    {
        EmbeddingModelDescriptor IImageEmbeddingGenerator.Model => EmbeddingModelDescriptor.Siglip2Base;

        public int Pictures { get; private set; }
        public int Texts { get; private set; }
        public string? LastText { get; private set; }

        public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken ct = default)
        {
            Pictures++;
            return ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }

        public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken ct = default)
        {
            Texts++;
            LastText = text;
            return ValueTask.FromResult<ReadOnlyMemory<float>>(new float[4]);
        }
    }

    private sealed class Pictures(PictureMatch[] near) : IPictureSearch, IPictureVectorIndexer
    {
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<PictureMatch>> NearestAsync(ReadOnlyMemory<float> vector, int limit, CancellationToken ct = default) =>
            Failure is not null ? throw Failure : Task.FromResult<IReadOnlyList<PictureMatch>>(near);

        public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);
    }

    /// <summary>An index that holds whatever it is asked for: the card is the lexical index's, and here it is a stand-in.</summary>
    private sealed class Index : IProductProfileReader
    {
        public Task<IReadOnlyList<ProductProfile>> ByIdsAsync(IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductProfile>>([.. productIds.Select(id => new ProductProfile(
                new SearchHit(id, $"CODE-{id}", $"Product {id}", $"product-{id}", "Kuisina", "KITCHEN", $"{id}-variant", $"{id}-SKU",
                    29.90m, 29.90m, 34.90m, "EUR", null, InStock: true, Score: 0), null, []))]);

        public Task<IReadOnlyList<ProductProfile>> ByCodesAsync(IReadOnlyCollection<string> codes, string culture, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProductProfile>> InCategoriesAsync(IReadOnlyCollection<string> categories, string culture, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
