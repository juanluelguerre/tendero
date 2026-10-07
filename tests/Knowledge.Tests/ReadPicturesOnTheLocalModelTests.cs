using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The job against the shop's own model, started with its vision projector
/// (Tendero__Vision=true, the steward-model container on its pinned port), over
/// pictures of the seed catalogue. A CI runner cannot hold the model, so with no
/// such server answering the test skips; what it holds is that a real picture
/// comes back as a document in each language, saying who read it — what the
/// description says is a person's to judge, and the test writes it out for one
/// to read (the phase 13a spec, I-4).
/// </summary>
public sealed class ReadPicturesOnTheLocalModelTests(ITestOutputHelper output)
{
    private static readonly Uri Server = new("http://localhost:38085/");

    private const string Model = "qwen3.5-4b-q8_0";

    private static bool ReadsPictures()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var props = JsonDocument.Parse(http.GetStringAsync(new Uri(Server, "props")).GetAwaiter().GetResult());
            return props.RootElement.TryGetProperty("modalities", out var modalities)
                && modalities.TryGetProperty("vision", out var sees) && sees.ValueKind == JsonValueKind.True;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static string SeedPicture(string itemId)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, "seed", "images", itemId + ".webp");
    }

    private sealed class Seed(IReadOnlyList<ProductPicture> pictures) : IProductPictures
    {
        public Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(pictures);
    }

    /// <summary>The seed's folder as the store: a picture's id is its item's.</summary>
    private sealed class FromDisk : IImageStore
    {
        public Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
            Task.FromResult<StoredImage?>(new StoredImage(File.OpenRead(SeedPicture(id.Value)), "image/webp"));
    }

    private sealed class Asker : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, "lucía", "Lucía", ["shopkeeper"]);
    }

    private static readonly (string ItemId, string Name)[] Pictures =
    [
        ("B10SHO0106", "Hogar house slippers"),
        ("B05DEFG606", "Set of 3 induction-ready non-stick frying pans"),
        ("B11COF0401", "6-cup stovetop coffee maker"),
        ("B11TAB0504", "Set of 6 recycled glass tumblers"),
        ("B12LAM0601", "Taller articulated LED desk lamp"),
    ];

    [Fact]
    public async Task A_batch_of_seed_pictures_comes_back_as_a_document_in_each_language()
    {
        Assert.SkipUnless(ReadsPictures(), "needs the local model started with its vision projector (Tendero__Vision=true)");
        Assert.SkipUnless(Pictures.All(picture => File.Exists(SeedPicture(picture.ItemId))), "needs the seed's pictures");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PictureReading:Model"] = "local",
            ["Steward:Models:local:Endpoint"] = new Uri(Server, "v1/").ToString(),
            ["Steward:Models:local:Model"] = Model,
            ["Steward:Models:local:ReadsPictures"] = "true",
        }).Build();
        var knowledge = new InMemoryKnowledge();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ReadPicturesCommand).Assembly);
        services.AddSingleton<TimeProvider>(new TestClock());
        services.AddSingleton<IPrincipalAccessor>(new Asker());
        services.AddSingleton<IKnowledgeRepository>(knowledge);
        services.AddSingleton<IUnitOfWork>(knowledge);
        services.AddSingleton<IProductPictures>(new Seed([.. Pictures.Select(picture => new ProductPicture(Guid.CreateVersion7(), picture.ItemId, picture.ItemId))]));
        services.AddSingleton<IImageStore>(new FromDisk());
        services.AddKnowledge(configuration);
        services.AddModelProfiles(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new ReadPicturesCommand(Pictures.Length), TestContext.Current.CancellationToken);

        foreach (var document in knowledge.Documents.OrderBy(document => document.Reading!.Picture.Value).ThenBy(document => document.Culture))
            output.WriteLine($"{document.Reading!.Picture} · {document.Culture} ({document.Text.Length}): {document.Text}");

        Assert.Equal((PictureReadingStatus.Ran, Pictures.Length, 0, 0), (result.Status, result.Read, result.Skipped, result.Failed));
        Assert.All(knowledge.Documents, document =>
        {
            Assert.Equal(SourceKind.ProductImage, document.Kind);
            Assert.Equal((Model, PictureReadingPrompt.Version), (document.Reading!.Model, document.Reading.PromptVersion));
            Assert.Equal(document.Culture, WrittenLanguage.Of(document.Text));
        });
    }
}
