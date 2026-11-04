using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.SetImageShows;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Knowledge;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.PictureDoubts;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Workers.Channels;
using ElGuerre.Tendero.Workers.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// *It belongs to another variant*, applied (ADR 0057; the author's ruling,
/// 2026-10-08), against a real Postgres: a person decides a doubt, the decision
/// leaves Knowledge through the outbox carrying values, the worker turns it into
/// Catalog's own command, and the picture says the colour that was seen —
/// Knowledge never touching a product (ADR 0018). The other two decisions, and a
/// colour no variant is sold in, change nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecidedDoubtRetagsThePictureTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(49m, "EUR");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_decided_to_be_another_variants_says_that_variants_colour_and_nothing_else_moves()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("doubt_retags_picture");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);
        await using var provider = Compose(factory.ConnectionString);

        // A pannier in olive and black, each in two sizes. Its second picture is the black one, filed under olive
        // in size L; the third is fine as it is; the fourth will be said to show a colour nobody sells.
        var pannier = Product.Create(Clock, LocalizedText.Both("Pannier", "Alforja"), Price);
        pannier.DefineAxes(Clock, ["COLOR", "SIZE"]);
        foreach (var colour in new[] { "OLIVE", "BLACK" })
            foreach (var size in new[] { "M", "L" })
                pannier.AddVariant(Clock, $"PAN-{colour}-{size}", Price, new Dictionary<string, string> { ["COLOR"] = colour, ["SIZE"] = size });
        foreach (var picture in new[] { "olive", "misfiled", "fine", "navy" })
            pannier.AddImage(Clock, new ImageId(picture));
        pannier.Show(Clock, new ImageId("misfiled"), new Dictionary<string, string> { ["COLOR"] = "OLIVE", ["SIZE"] = "L" });
        pannier.Show(Clock, new ImageId("fine"), new Dictionary<string, string> { ["COLOR"] = "OLIVE" });
        pannier.Show(Clock, new ImageId("navy"), new Dictionary<string, string> { ["COLOR"] = "OLIVE" });

        var (misfiled, fine, navy) = (Doubt(pannier.Id, "misfiled", "BLACK"), Doubt(pannier.Id, "fine", "BLACK"), Doubt(pannier.Id, "navy", "NAVY_BLUE"));
        await using (var context = factory.Create())
        {
            context.Products.Add(pannier);
            context.PictureDoubts.AddRange(misfiled, fine, navy);
            await context.SaveChangesAsync(ct);
        }
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        foreach (var (doubt, decision) in new[]
                 {
                     (misfiled, PictureDoubtDecision.BelongsToAnotherVariant),
                     (fine, PictureDoubtDecision.PictureIsFine),
                     // A person may say it; no variant is navy, so there is nowhere to move it.
                     (navy, PictureDoubtDecision.BelongsToAnotherVariant),
                 })
        {
            await using var scope = provider.CreateAsyncScope();
            var decided = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new DecidePictureDoubtCommand(doubt.Id, decision), ct);
            Assert.Equal(DecidePictureDoubtOutcome.Decided, decided.Outcome);
        }
        while (await WorkerOutbox.PassAsync(provider, ct) > 0) { }

        await using var reread = factory.Create();
        var shows = (await reread.Products.SingleAsync(product => product.Id == pannier.Id, ct)).Images
            .ToDictionary(image => image.Id.Value, image => image.Shows);

        // The colour the model saw, in the place of the one it was filed under — and the size it showed, kept.
        Assert.Equal([new AxisValue("COLOR", "BLACK"), new AxisValue("SIZE", "L")], shows["misfiled"]);
        Assert.Equal([new AxisValue("COLOR", "OLIVE")], shows["fine"]);
        Assert.Equal([new AxisValue("COLOR", "OLIVE")], shows["navy"]);
        Assert.Empty(shows["olive"]);

        // Catalog was asked twice, by its own command, and said no once: both are in the audit log.
        var audited = await reread.AuditEntries.Where(entry => entry.CommandType == nameof(SetImageShowsCommand)).ToListAsync(ct);
        Assert.Equal(2, audited.Count);
        Assert.Single(audited, entry => entry.Outcome == AuditOutcome.Denied);
    }

    private static PictureDoubt Doubt(ProductId product, string picture, string seen) =>
        PictureDoubt.Raise(
            product,
            new PictureReading(new ImageId(picture), "qwen3.5-4b-q8_0", "picture-reading-test"),
            new ColourSeen("COLOR", seen, LocalizedText.Both(seen, seen), "OLIVE", LocalizedText.Both("olive", "oliva"), 0.97, Related: false, OfAnotherVariant: true),
            Clock)!;

    /// <summary>The worker's container, as <c>AddTenderoWorker</c> composes it: Catalog's slices by name, not its assembly.</summary>
    private static ServiceProvider Compose(string connectionString)
    {
        // The channel bridges tell stock by warehouse (P16-28): the warehouses come with Inventory.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:Warehouses:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "warehouses.sample.json"),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(
            typeof(ProductIndexProjection).Assembly,
            typeof(RecordDescriptionHandler).Assembly,
            typeof(ApplyApprovedClaimToCatalog).Assembly);
        services.AddTenderoSlices(typeof(SetImageShowsCommand));
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddInventory(configuration);
        services.AddChannels(configuration);
        services.AddChannelBridges();
        services.AddKnowledge(configuration);
        services.AddHybridSearch(configuration);
        services.AddSingleton<IProductIndexer>(new NoIndex());
        services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class NoIndex : IProductIndexer
    {
        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task IndexAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(ProductId productId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
