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
/// The job that has each picture read, over a scripted model (the phase 13a spec,
/// I-4): a batch of pictures nobody has read under today's instructions, each
/// described by the profile that reads pictures and stored as a document in each
/// language — and every way a picture ends without one, counted. Nothing here
/// proposes a claim: that is the next step's, from the documents this leaves.
/// </summary>
public sealed class ReadPicturesTests
{
    private static readonly TestClock Clock = new();

    /// <summary>A picture as a store keeps one: the smallest PNG there is, one white pixel.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private sealed class Catalogue(IReadOnlyList<ProductPicture> pictures) : IProductPictures
    {
        public Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(pictures);
    }

    private sealed class Store(HashSet<string> held) : IImageStore
    {
        public List<string> Opened { get; } = [];

        public Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default)
        {
            Opened.Add(id.Value);
            return Task.FromResult(held.Contains(id.Value) ? new StoredImage(new MemoryStream(Png), "image/png") : null);
        }
    }

    private sealed class Asker(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, name.ToLowerInvariant(), name, ["shopkeeper"]);
    }

    private sealed class Shop(ServiceProvider provider, InMemoryKnowledge knowledge, Store store) : IAsyncDisposable
    {
        public InMemoryKnowledge Knowledge { get; } = knowledge;

        public Store Store { get; } = store;

        public IReadOnlyList<AIInvocation> Metered()
        {
            var log = provider.GetRequiredService<AIInvocationLog>();
            var rows = new List<AIInvocation>();
            while (log.Reader.TryRead(out var row))
                rows.Add(row);
            return rows;
        }

        public async Task<ReadPicturesResult> Ask(int count = PictureReadingOptions.MaxBatch)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ReadPicturesCommand(count), TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private static ProductPicture Picture(string name, string image) =>
        new(Guid.CreateVersion7(), name.ToUpperInvariant(), image);

    private static Shop Compose(
        IReadOnlyList<ProductPicture> pictures, bool readsPictures = true, string? model = "scripted", IEnumerable<string>? missingBytes = null,
        string script = "read-pictures.json")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PictureReading:Model"] = model,
            ["Steward:Models:scripted:Kind"] = "Scripted",
            ["Steward:Models:scripted:Script"] = Path.Combine(AppContext.BaseDirectory, "scripts", script),
            ["Steward:Models:scripted:Model"] = "scripted-model",
            ["Steward:Models:scripted:ReadsPictures"] = readsPictures.ToString(),
        }).Build();

        var gone = (missingBytes ?? []).ToHashSet();
        var store = new Store([.. pictures.Select(picture => picture.ImageId).Where(image => !gone.Contains(image))]);
        var knowledge = new InMemoryKnowledge();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ReadPicturesCommand).Assembly);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IPrincipalAccessor>(new Asker("Lucía"));
        services.AddSingleton<IKnowledgeRepository>(knowledge);
        services.AddSingleton<IUnitOfWork>(knowledge);
        services.AddSingleton<IProductPictures>(new Catalogue(pictures));
        services.AddSingleton<IImageStore>(store);
        services.AddKnowledge(configuration);
        services.AddModelProfiles(configuration);
        return new Shop(services.BuildServiceProvider(), knowledge, store);
    }

    // ---------- a batch ----------

    [Fact]
    public async Task A_batch_reads_each_picture_and_keeps_what_was_read_in_both_languages()
    {
        var pot = Picture("Cafetera", "pot-cover");
        var mug = Picture("Taza", "mug-cover");
        await using var shop = Compose([pot, mug]);

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.Ran, 2, 0, 0), (result.Status, result.Read, result.Skipped, result.Failed));
        Assert.Equal(4, shop.Knowledge.Documents.Count);

        var spanish = shop.Knowledge.Documents.Single(document => document.ProductId.Value == pot.ProductId && document.Culture == "es");
        Assert.Equal("Una cafetera italiana de metal plateado. Tiene un mango negro y una tapa con pomo.", spanish.Text);
        Assert.Equal(SourceKind.ProductImage, spanish.Kind);
        Assert.Equal(new PictureReading(new ImageId("pot-cover"), "scripted-model", PictureReadingPrompt.Version), spanish.Reading);
    }

    /// <summary>
    /// **The model is not told what the picture is of.** Told the product's name,
    /// the shop's own 4B read one slipper as "a pair of slippers" and a moka pot as
    /// "a six-cup coffee maker": the name came back as something seen, and a claim
    /// from that sentence would cite a picture for a fact out of the catalogue. The
    /// script answers the product's code with prose, so a job that sends it fails.
    /// </summary>
    [Fact]
    public async Task The_model_is_shown_the_picture_and_told_nothing_of_the_product()
    {
        await using var shop = Compose([Picture("Cafetera", "pot-cover")]);

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.Ran, 1, 0), (result.Status, result.Read, result.Failed));
    }

    /// <summary>What was read under today's instructions by this model is not read again: the batch after the last finds nothing, and opens nothing.</summary>
    [Fact]
    public async Task A_picture_already_read_under_these_instructions_is_not_read_again()
    {
        await using var shop = Compose([Picture("Cafetera", "pot-cover")]);
        await shop.Ask();
        shop.Store.Opened.Clear();

        var again = await shop.Ask();

        Assert.Equal(PictureReadingStatus.NothingToRead, again.Status);
        Assert.Empty(shop.Store.Opened);
        Assert.Single(shop.Metered());
    }

    /// <summary>
    /// A reading is only comparable with another of the same instructions: one
    /// made under older ones is read again, and the new words replace the old.
    /// </summary>
    [Fact]
    public async Task A_picture_read_under_older_instructions_is_read_again()
    {
        var pot = Picture("Cafetera", "pot-cover");
        await using var shop = Compose([pot]);
        var old = new PictureReading(new ImageId("pot-cover"), "scripted-model", "picture-reading-2026-10-07");
        shop.Knowledge.Add(SourceDocument.Depict(new ProductId(pot.ProductId), "es", "Una cafetera.", old, Clock));
        shop.Knowledge.Add(SourceDocument.Depict(new ProductId(pot.ProductId), "en", "A coffee maker.", old, Clock));

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.Ran, 1), (result.Status, result.Read));
        Assert.Contains(shop.Knowledge.Documents, document => document.Reading?.PromptVersion == PictureReadingPrompt.Version);
    }

    [Fact]
    public async Task A_batch_takes_no_more_pictures_than_it_was_asked_for()
    {
        await using var shop = Compose([Picture("Cafetera", "a"), Picture("Taza", "b")]);

        var result = await shop.Ask(count: 1);

        Assert.Equal(1, result.Read);
        Assert.Equal(["a"], shop.Store.Opened);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PictureReadingOptions.MaxBatch + 1)]
    public async Task A_batch_is_between_one_picture_and_the_most_a_person_reviews_at_once(int count)
    {
        await using var shop = Compose([Picture("Cafetera", "a")]);

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => shop.Ask(count));
    }

    // ---------- every way a picture ends without a reading ----------

    /// <summary>
    /// The degraded path (invariant 8): with no profile that says it reads
    /// pictures, nothing is opened, nothing is sent and nothing is stored — the
    /// shop and its claims from descriptions are as they were.
    /// </summary>
    [Theory]
    [InlineData(false, "scripted")]
    [InlineData(true, null)]
    [InlineData(true, "nobody")]
    public async Task With_no_profile_that_reads_pictures_nothing_is_read_and_the_answer_says_so(bool readsPictures, string? model)
    {
        await using var shop = Compose([Picture("Cafetera", "pot-cover")], readsPictures, model);

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.NoModel, 0, 0, 0), (result.Status, result.Read, result.Skipped, result.Failed));
        Assert.Empty(shop.Store.Opened);
        Assert.Empty(shop.Knowledge.Documents);
        Assert.Empty(shop.Metered());
    }

    [Fact]
    public async Task A_picture_the_store_does_not_hold_is_skipped_and_the_rest_are_read()
    {
        await using var shop = Compose([Picture("Cafetera", "lost"), Picture("Taza", "mug-cover")], missingBytes: ["lost"]);

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.Ran, 1, 1, 0), (result.Status, result.Read, result.Skipped, result.Failed));
    }

    /// <summary>
    /// What a model wrote is data: prose where a shape was asked for, a language
    /// missing, or English filed as Spanish is a failed picture — told by code,
    /// and never stored as a document a claim could rest on.
    /// </summary>
    [Theory]
    [InlineData("read-pictures-prose.json")]
    [InlineData("read-pictures-language.json")]
    [InlineData("read-pictures-empty.json")]
    public async Task What_the_model_wrote_that_cannot_be_kept_is_a_failed_picture(string script)
    {
        await using var shop = Compose([Picture("Taza", "x")], script: script);

        var result = await shop.Ask();

        Assert.Equal((PictureReadingStatus.Ran, 0, 0, 1), (result.Status, result.Read, result.Skipped, result.Failed));
        Assert.Empty(shop.Knowledge.Documents);
    }

    [Fact]
    public async Task Every_call_is_metered_as_this_jobs_under_its_instructions_and_the_person_who_asked()
    {
        await using var shop = Compose([Picture("Cafetera", "a"), Picture("Taza", "b")]);

        await shop.Ask();

        var rows = shop.Metered();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(
            (ReadPicturesHandler.Operation, PictureReadingPrompt.Version, "scripted", "lucía"),
            (row.Operation, row.PromptVersion, row.Profile, row.Subject)));
    }
}
