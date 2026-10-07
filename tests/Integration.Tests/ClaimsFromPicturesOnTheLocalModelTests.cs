using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Knowledge;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ProposeClaims;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// What the shop's own model makes of a picture, end to end and against a real
/// Postgres (the phase 13a spec, I-4): seed pictures read by the model with its
/// vision projector, and claims proposed from each reading by the extractor — the
/// same weights, on the same server — twice: offered the whole vocabulary, as a
/// description is, and offered only what a picture can show.
///
/// A CI runner cannot hold the model, so with no such server the test skips. What
/// it holds is small and structural: every picture is read, and with the
/// picture's vocabulary no claim is typed with an attribute a picture cannot
/// show. **What it is for is what it writes out**: each reading and each claim,
/// under both vocabularies, for a person to count the wrong ones.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClaimsFromPicturesOnTheLocalModelTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private static readonly Uri Server = new("http://localhost:38085/");

    private static readonly (string ItemId, string Name)[] Pictures =
    [
        ("B05DEFG606", "Set of 3 frying pans"),
        ("B10SHI0205", "Cycling jersey"),
        ("B10SHO0106", "House slippers"),
        ("B11COF0401", "Stovetop coffee maker"),
        ("B11COF0407", "Toaster"),
        ("B11COO0310", "Set of 3 kitchen knives"),
        ("B11TAB0502", "Set of 4 breakfast bowls"),
        ("B11TAB0504", "Set of 6 tumblers"),
        ("B12LAM0601", "Desk lamp"),
        ("B13BAG0708", "Hip pack"),
        ("B13BAG0712", "Packing cubes, set of 3"),
        ("B14HOM0806", "Set of 2 cushion covers"),
    ];

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

    private static ServiceProvider Compose(string connectionString, IReadOnlyList<ProductPicture> pictures, bool wholeVocabulary)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
                ["PictureReading:Model"] = "local",
                ["Steward:Models:local:Endpoint"] = new Uri(Server, "v1/").ToString(),
                ["Steward:Models:local:Model"] = "qwen3.5-4b-q8_0",
                ["Steward:Models:local:ReadsPictures"] = "true",
                // The extractor is the same weights: here it is the same server.
                [KnowledgeServiceCollectionExtensions.ExtractorEndpointKey] = Server.ToString(),
                // What is measured is the typing, not the support: a fixed score, so the model's claims are proposed.
                ["Support:Scripted"] = "0.9",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ReadPicturesCommand).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalogReaders(configuration);
        services.AddKnowledge(configuration);
        services.AddModelProfiles(configuration);
        services.AddSingleton<IPrincipalAccessor>(new Asker());
        services.AddSingleton<IProductPictures>(new Seed(pictures));
        services.AddSingleton<IImageStore>(new FromDisk());
        if (wholeVocabulary)
            services.Decorate();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Seed_pictures_are_read_and_claims_proposed_from_each_reading(bool wholeVocabulary)
    {
        Assert.SkipUnless(ReadsPictures(), "needs the local model started with its vision projector on port 38085");
        Assert.SkipUnless(Pictures.All(picture => File.Exists(SeedPicture(picture.ItemId))), "needs the seed's pictures");
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync(wholeVocabulary ? "pictures_whole_vocabulary" : "pictures_their_vocabulary");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var pictures = Pictures.Select(picture => new ProductPicture(Guid.CreateVersion7(), picture.ItemId, picture.ItemId)).ToList();
        await using var provider = Compose(factory.ConnectionString, pictures, wholeVocabulary);

        var read = 0;
        for (var batch = 0; batch < 4; batch++)
        {
            await using var scope = provider.CreateAsyncScope();
            read += (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ReadPicturesCommand(PictureReadingOptions.MaxBatch), ct)).Read;
        }

        foreach (var picture in pictures)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ProposeClaimsCommand(new ProductId(picture.ProductId)), ct);
        }

        await using var reread = factory.Create();
        var documents = await reread.SourceDocuments.ToListAsync(ct);
        var claims = await reread.Claims.ToListAsync(ct);

        output.WriteLine($"=== {(wholeVocabulary ? "the whole vocabulary, as for a description" : "what a picture can show")}");
        foreach (var picture in pictures)
        {
            var name = Pictures.Single(known => known.ItemId == picture.Code).Name;
            var reading = documents.Single(document => document.ProductId.Value == picture.ProductId && document.Culture == "en");
            output.WriteLine($"{picture.Code} · {name} · READ: {reading.Text}");
            foreach (var claim in claims.Where(claim => claim.ProductId.Value == picture.ProductId).OrderBy(claim => claim.AttributeCode is null))
                output.WriteLine($"{picture.Code} ·   {(claim.AttributeCode is null ? "(untyped)" : $"{claim.AttributeCode} = {claim.Value}")} · {claim.Statement.In("en")}");
        }

        var typed = claims.Where(claim => claim.AttributeCode is not null).ToList();
        output.WriteLine($"=== {claims.Count} claims, {typed.Count} typed: {string.Join(", ", typed.GroupBy(claim => claim.AttributeCode).Select(group => $"{group.Key} {group.Count()}"))}");

        Assert.Equal(Pictures.Length, read);
        if (!wholeVocabulary)
            Assert.All(typed, claim => Assert.Equal("PIECES", claim.AttributeCode));
    }
}

/// <summary>Puts the vocabulary a description is offered in front of every source: the pipeline as it was.</summary>
file static class Vocabularies
{
    extension(IServiceCollection services)
    {
        public void Decorate()
        {
            var registered = services.Last(service => service.ServiceType == typeof(IAttributeVocabulary));
            services.Remove(registered);
            services.AddScoped<IAttributeVocabulary>(provider =>
                new Wrapped((IAttributeVocabulary)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));
        }
    }

    private sealed class Wrapped(IAttributeVocabulary inner) : IAttributeVocabulary
    {
        public Task<IReadOnlyList<AttributeSpec>> CurrentAsync(SourceKind readFrom, CancellationToken cancellationToken = default) =>
            inner.CurrentAsync(SourceKind.ProductDescription, cancellationToken);
    }
}
