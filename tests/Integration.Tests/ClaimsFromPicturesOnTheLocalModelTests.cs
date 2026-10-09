using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Ports;
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
/// vision projector — described, and asked how many items are drawn — and claims
/// built by code from what each answered, beside what the catalogue holds.
///
/// A CI runner cannot hold the model, so with no such server the test skips. What
/// it holds is small and structural: every picture is read, and every claim from
/// a picture is typed with what a picture was asked. **What it is for is what it
/// writes out**: each reading, each count beside the pieces the seed declares,
/// and each claim, for a person to count the wrong ones. The same twelve
/// pictures the extractor was measured on, and four shoes: a pair would count
/// two where the catalogue does not call it two pieces, and the seed draws one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClaimsFromPicturesOnTheLocalModelTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private static readonly Uri Server = new("http://localhost:38085/");

    /// <param name="Pieces">What the seed declares for the product, or null when it declares none.</param>
    private sealed record Seeded(string ItemId, string Name, int? Pieces = null);

    private static readonly Seeded[] Pictures =
    [
        new("B05DEFG606", "Set of 3 frying pans", 3),
        new("B10SHI0205", "Cycling jersey"),
        new("B10SHO0106", "House slippers"),
        new("B11COF0401", "Stovetop coffee maker"),
        new("B11COF0407", "Toaster"),
        new("B11COO0310", "Set of 3 kitchen knives", 3),
        new("B11TAB0502", "Set of 4 breakfast bowls", 4),
        new("B11TAB0504", "Set of 6 tumblers", 6),
        new("B12LAM0601", "Desk lamp"),
        new("B13BAG0708", "Hip pack"),
        new("B13BAG0712", "Packing cubes, set of 3", 3),
        new("B14HOM0806", "Set of 2 cushion covers", 2),
        new("B10SHO0101", "Shoe, drawn alone"),
        new("B10SHO0102", "Shoe, drawn alone"),
        new("B10SHO0103", "Shoe, drawn alone"),
        new("B15SHO1101", "Shoe, drawn alone"),
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
        /// <summary>The colours the seed's catalogue defines, when the test is about them.</summary>
        public IReadOnlyList<PictureColour> Colours { get; set; } = [];

        public Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(pictures);

        public Task<IReadOnlyList<PictureColour>> ColoursAsync(CancellationToken cancellationToken = default) => Task.FromResult(Colours);
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

    /// <summary>The pieces the seed declares, as the catalogue would hold them: no product is imported here.</summary>
    private sealed class DeclaredPieces(IReadOnlyDictionary<ProductId, int> pieces) : ICatalogueValues
    {
        public Task<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>> OfAsync(
            IReadOnlyCollection<ProductId> products, string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>>(products
                .Where(pieces.ContainsKey)
                .ToDictionary(
                    product => product,
                    product => (IReadOnlyList<HeldValue>)[new HeldValue(PictureAnswers.Pieces, $"{pieces[product]}", $"{pieces[product]}")]));
    }

    private static ServiceProvider Compose(string connectionString, IProductPictures pictures, ICatalogueValues catalogue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
                ["PictureReading:Model"] = "local",
                ["Steward:Models:local:Endpoint"] = new Uri(Server, "v1/").ToString(),
                ["Steward:Models:local:Model"] = "qwen3.5-4b-q8_0",
                ["Steward:Models:local:ReadsPictures"] = "true",
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
        services.AddSingleton(pictures);
        services.AddSingleton<IImageStore>(new FromDisk());
        services.AddSingleton(catalogue);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Seed_pictures_are_read_and_counted_and_code_builds_the_claims()
    {
        Assert.SkipUnless(ReadsPictures(), "needs the local model started with its vision projector on port 38085");
        Assert.SkipUnless(Pictures.All(picture => File.Exists(SeedPicture(picture.ItemId))), "needs the seed's pictures");
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("pictures_answers");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var pictures = Pictures.Select(picture => new ProductPicture(Guid.CreateVersion7(), picture.ItemId, picture.ItemId)).ToList();
        var declared = pictures
            .Select(picture => (picture.ProductId, Pictures.Single(known => known.ItemId == picture.Code).Pieces))
            .Where(product => product.Pieces is not null)
            .ToDictionary(product => new ProductId(product.ProductId), product => product.Pieces!.Value);
        await using var provider = Compose(factory.ConnectionString, new Seed(pictures), new DeclaredPieces(declared));

        var read = 0;
        for (var batch = 0; batch < 4; batch++)
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ReadPicturesCommand(PictureReadingOptions.MaxBatch), ct);
            output.WriteLine($"batch {batch + 1}: {result.Status}, read {result.Read}, skipped {result.Skipped}, failed {result.Failed}");
            read += result.Read;
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

        var (sets, countedRight) = (0, 0);
        foreach (var picture in pictures)
        {
            var known = Pictures.Single(seeded => seeded.ItemId == picture.Code);
            var reading = documents.SingleOrDefault(document => document.ProductId.Value == picture.ProductId && document.Culture == "en");
            if (reading is null)
            {
                output.WriteLine($"{picture.Code} · {known.Name} · NOT READ");
                continue;
            }

            var counted = PictureAnswers.CountIn(reading.Text, "en")?.Count;
            if (known.Pieces is { } pieces)
            {
                sets++;
                countedRight += counted == pieces ? 1 : 0;
            }

            output.WriteLine($"{picture.Code} · {known.Name} · declares {known.Pieces?.ToString() ?? "nothing"} · counted {counted} · READ: {reading.Text.ReplaceLineEndings(" | ")}");
            foreach (var claim in claims.Where(claim => claim.ProductId.Value == picture.ProductId))
                output.WriteLine($"{picture.Code} ·   {(claim.AttributeCode is null ? "(untyped)" : $"{claim.AttributeCode} = {claim.Value}")} · {claim.Statement.In("en")}");
        }

        output.WriteLine($"=== {claims.Count} claims from {pictures.Count} pictures; {countedRight} of {sets} sets counted as the seed declares");

        Assert.Equal(Pictures.Length, read);
        Assert.All(claims, claim => Assert.Equal(PictureAnswers.Pieces, claim.AttributeCode));
    }

    /// <summary>
    /// The colour, end to end (I-4d): every seed picture whose product declares
    /// one colour is asked which of the catalogue's colours it shows, and a sure
    /// difference is a doubt about the picture. Structural, as above: every
    /// picture is read and no claim is about a colour. What it writes out is each
    /// colour seen beside the one declared, and the doubts.
    /// </summary>
    [Fact]
    public async Task Seed_pictures_of_one_colour_are_asked_their_colour_and_a_sure_difference_is_a_doubt()
    {
        Assert.SkipUnless(ReadsPictures(), "needs the local model started with its vision projector on port 38085");
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("pictures_colours");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        // The seed as the catalogue would hold it: each product's colour is its own, and its one picture is its cover.
        var definitions = new AttributeDefinitions(await AttributeDefinitionSeedFile.LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"), TimeProvider.System, ct));
        using var products = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json"), ct));
        List<ProductPicture> pictures = [];
        foreach (var product in products.RootElement.EnumerateArray())
        {
            var item = product.GetProperty("item_id").GetString()!;
            if (product.GetProperty("attributes").TryGetProperty("color", out var colour)
                && File.Exists(SeedPicture(item))
                && definitions.ByCode("COLOR")!.Accept(colour.GetString()!)?.OptionCode is { } code)
            {
                pictures.Add(new ProductPicture(Guid.CreateVersion7(), item, item, code));
            }
        }

        Assert.SkipUnless(pictures.Count > 0, "needs the seed's pictures");
        var seed = new Seed(pictures);
        await using var provider = Compose(factory.ConnectionString, seed, new DeclaredPieces(new Dictionary<ProductId, int>()));
        await using (var scope = provider.CreateAsyncScope())
            seed.Colours = await ActivatorUtilities.CreateInstance<ProductPicturesReader>(scope.ServiceProvider).ColoursAsync(ct);

        var read = 0;
        for (var batch = 0; read < pictures.Count && batch < pictures.Count; batch++)
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ReadPicturesCommand(PictureReadingOptions.MaxBatch), ct);
            read += result.Read;
            if (result.Read == 0)
                break;
        }

        await using var reread = factory.Create();
        var documents = await reread.SourceDocuments.Where(document => document.Culture == "en").ToListAsync(ct);
        var doubts = await reread.PictureDoubts.ToListAsync(ct);

        var same = 0;
        foreach (var picture in pictures)
        {
            var reading = documents.SingleOrDefault(document => document.ProductId.Value == picture.ProductId);
            var line = reading?.Text.Split('\n').LastOrDefault(text => text.StartsWith("Colour seen", StringComparison.Ordinal)) ?? "NOT READ";
            var held = seed.Colours.Single(colour => colour.Code == picture.Colour).English;
            var agrees = line.StartsWith($"Colour seen in the picture: {held}", StringComparison.Ordinal);
            same += agrees ? 1 : 0;
            if (!agrees)
                output.WriteLine($"{picture.Code} · held {held} · {line}");
        }

        output.WriteLine($"=== {read} of {pictures.Count} pictures read; {same} show the colour the catalogue holds; {doubts.Count} doubt(s)");
        foreach (var doubt in doubts)
            output.WriteLine($"DOUBT · {pictures.Single(picture => picture.ProductId == doubt.ProductId.Value).Code} · seen {doubt.Seen} at {doubt.Confidence:F2} · held {doubt.Held} · {doubt.Status}");

        Assert.Equal(pictures.Count, read);
        Assert.DoesNotContain(await reread.Claims.ToListAsync(ct), claim => claim.AttributeCode == "COLOR");
    }
}
