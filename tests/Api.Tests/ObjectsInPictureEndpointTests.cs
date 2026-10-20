using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// POST /api/search/by-image/objects (the camera search, CS-2): a photograph in,
/// the objects in it out — numbered in reading order, named in the shopper's
/// language, each saying whether the shop sells its kind — for the browser to crop
/// and search one by one. Anyone may ask; a shop without a detector says so, and
/// the photograph is then searched whole (CS-9).
/// </summary>
public sealed class ObjectsInPictureEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Width = 64;
    private const int Height = 48;

    /// <summary>The author's selfie, as a script: the book at the top right, the t-shirt below on the left, and the glasses under the floor.</summary>
    private static ScriptedObjectDetector Selfie() => new(
        new ScriptedObject("book", new PictureBox(40, 5, 20, 40), 0.47),
        new ScriptedObject("SHIRT", new PictureBox(2, 30, 34, 18), 0.44),
        new ScriptedObject("sunglasses", new PictureBox(14, 8, 15, 5), 0.39));

    [Fact]
    public async Task Nobody_signed_in_finds_the_objects_of_a_photograph_named_in_their_language()
    {
        var (client, detector, owner) = Compose(Selfie());
        using var _ = owner;

        var response = await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects?culture=es"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("es", response.Content.Headers.ContentLanguage.Single());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((Width, Height), (body.GetProperty("width").GetInt32(), body.GetProperty("height").GetInt32()));
        var objects = body.GetProperty("objects").EnumerateArray().ToList();
        Assert.Equal(
            [(1, "book", false, "libro"), (2, "SHIRT", true, "Camisetas")],
            objects.Select(found => (found.GetProperty("number").GetInt32(), found.GetProperty("kind").GetString(), found.GetProperty("sold").GetBoolean(), found.GetProperty("name").GetString())));
        Assert.Equal(
            (2, 30, 34, 18, 0.44),
            (objects[1].GetProperty("box").GetProperty("left").GetInt32(), objects[1].GetProperty("box").GetProperty("top").GetInt32(),
                objects[1].GetProperty("box").GetProperty("width").GetInt32(), objects[1].GetProperty("box").GetProperty("height").GetInt32(),
                objects[1].GetProperty("score").GetDouble()));
        Assert.Equal(1, detector.Asked);
    }

    /// <summary>The floor is a setting (ADR 0062): the glasses at 0.39 come back once it is lowered under them.</summary>
    [Fact]
    public async Task What_is_under_the_floor_is_not_an_object_and_the_floor_is_a_setting()
    {
        var (client, _, owner) = Compose(Selfie(), minScore: "0.35");
        using var _ = owner;

        var body = await (await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects?culture=en"), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Contains("sunglasses", body.GetProperty("objects").EnumerateArray().Select(found => found.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task A_shop_started_without_a_detector_says_so_before_and_when_it_is_asked()
    {
        using var factory = new TenderoApiFactory();
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/search/by-image/status", Ct);
        var response = await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects"), Ct);

        Assert.False(status.GetProperty("objects").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ObjectsNotAvailable", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_a_detector_the_status_says_the_objects_can_be_found()
    {
        var (client, _, owner) = Compose(Selfie());
        using var _ = owner;

        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/search/by-image/status", Ct)).GetProperty("objects").GetBoolean());
    }

    [Fact]
    public async Task What_is_not_a_picture_is_refused_at_the_door_and_the_detector_is_never_asked()
    {
        var (client, detector, owner) = Compose(Selfie());
        using var _ = owner;

        var response = await client.SendAsync(WithPicture("<svg onload='alert(1)'/>"u8.ToArray(), "/api/search/by-image/objects?culture=en"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("PictureNotTaken", problem.GetProperty("code").GetString());
        Assert.Contains("JPEG, a PNG or a WebP", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, detector.Asked);
    }

    /// <summary>A detector's own words name its files; they stay in the log.</summary>
    [Fact]
    public async Task A_detector_that_fails_is_a_503_that_says_nothing_of_the_detector()
    {
        var (client, _, owner) = Compose(new Failing());
        using var _ = owner;

        var response = await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects"), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("ObjectsNotAnswering", body, StringComparison.Ordinal);
        Assert.DoesNotContain("detector.onnx", body, StringComparison.Ordinal);
    }

    /// <summary>A photograph's objects are found once and then searched one by one (CS-3): twelve photographs a minute, on a budget of their own.</summary>
    [Fact]
    public async Task The_thirteenth_photograph_in_a_minute_is_refused_and_its_crops_are_still_searched()
    {
        var (client, _, owner) = Compose(Selfie());
        using var _ = owner;

        for (var asked = 0; asked < 12; asked++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects"), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(WithPicture(APicture(), "/api/search/by-image/objects"), Ct)).StatusCode);
    }

    [Fact]
    public void Finding_the_objects_of_a_photograph_is_public_by_decision()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/api/search/by-image/objects");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static (HttpClient Client, Counting Detector, IDisposable Owner) Compose(IObjectDetector detector, string? minScore = null)
    {
        var counting = new Counting(detector);
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder =>
        {
            if (minScore is not null)
                builder.UseSetting("Search:Objects:MinScore", minScore);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IObjectDetector>(counting);
                services.AddSingleton<ICategoryReader>(new Tree());
            });
        });
        return (factory.CreateClient(), counting, root);
    }

    private static HttpRequestMessage WithPicture(byte[] bytes, string route)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpRequestMessage(HttpMethod.Post, route) { Content = new MultipartFormDataContent { { file, "picture", "photo.png" } } };
    }

    private static byte[] APicture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class Counting(IObjectDetector inner) : IObjectDetector
    {
        public int Asked { get; private set; }

        public string Name => inner.Name;

        public Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default)
        {
            Asked++;
            return inner.DetectAsync(picture, kinds, cancellationToken);
        }
    }

    private sealed class Tree : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoryTree([Category.Define(TimeProvider.System, "SHIRT", LocalizedText.Both("Shirts", "Camisetas"))]));
    }

    private sealed class Failing : IObjectDetector
    {
        public string Name => "failing";

        public Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default) =>
            throw new OnnxLikeException("Load model from models/owlv2/detector.onnx failed");
    }

    private sealed class OnnxLikeException(string message) : Exception(message);
}
