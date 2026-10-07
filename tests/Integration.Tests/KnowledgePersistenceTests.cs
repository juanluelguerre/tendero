using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Knowledge's first writer, against a real Postgres (ADR 0018). What only a
/// round trip proves: the statement's LocalizedText, the evidence and its nested
/// quote, and the provenance survive their jsonb columns; a document is keyed by
/// its content hash; and a claim's events reach the outbox in the same save.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class KnowledgePersistenceTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_claim_comes_back_with_its_evidence_provenance_and_review()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("knowledge_round_trip");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var pan = ProductId.New();
        var document = SourceDocument.Describe(pan, "es", "Sartén de hierro fundido precurada, apta para inducción, horno y fuego directo.", Clock);
        var claim = ProductClaim.Propose(
            pan,
            LocalizedText.From("es", "Es apta para inducción.").With("en", "It works on induction."),
            "INDUCTION", "true",
            [new Evidence(document.Id, document.Locate("apta para inducción")!, 0.97)],
            new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a", "claims-2026-09-26", "run-1", Clock.GetUtcNow()),
            Clock);
        claim.Approve("shopkeeper@tendero", Clock);

        await using (var context = factory.Create())
        {
            context.SourceDocuments.Add(document);
            context.Claims.Add(claim);
            await context.SaveChangesAsync(ct);
        }

        await using var reread = factory.Create();
        var stored = await reread.Claims.SingleAsync(c => c.Id == claim.Id, ct);
        var storedDocument = await reread.SourceDocuments.SingleAsync(d => d.Id == document.Id, ct);

        Assert.Equal("It works on induction.", stored.Statement.In("en"));
        Assert.Equal(("INDUCTION", "true", ClaimStatus.Approved), (stored.AttributeCode, stored.Value, stored.Status));
        Assert.Equal("shopkeeper@tendero", stored.ReviewedBy);
        var evidence = Assert.Single(stored.Evidence);
        Assert.Equal(document.Id, evidence.Document);
        Assert.Equal("apta para inducción", evidence.Quote.Text);
        Assert.Equal(storedDocument.Text[evidence.Quote.Start..evidence.Quote.End], evidence.Quote.Text);
        Assert.Equal(0.97, stored.Support);
        Assert.Equal("851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a", stored.Provenance.ModelRevision);
        Assert.Equal(SourceDocumentId.Of(pan, "es", storedDocument.Text), storedDocument.Id);

        // Proposed and approved in one save: both events leave through the outbox.
        var types = await reread.OutboxMessages.Select(message => message.Type).ToListAsync(ct);
        Assert.Contains(types, type => type.Contains(nameof(ClaimProposed), StringComparison.Ordinal));
        Assert.Contains(types, type => type.Contains(nameof(ClaimApproved), StringComparison.Ordinal));
    }

    /// <summary>
    /// Only a product's current Spanish text can wait: an older one replaced before
    /// the model reached it would otherwise be offered for ever, and the proposer
    /// would loop on a queue that never empties.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Only_the_current_text_of_a_product_waits_for_claims()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("knowledge_waiting");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var clock = new TestClock();
        var replaced = ProductId.New();
        var older = SourceDocument.Describe(replaced, "es", "Texto antiguo.", clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var newer = SourceDocument.Describe(replaced, "es", "Texto nuevo.", clock);
        newer.MarkProposed(clock);
        var waiting = ProductId.New();
        var fresh = SourceDocument.Describe(waiting, "es", "Texto por leer.", clock);
        var english = SourceDocument.Describe(ProductId.New(), "en", "English only.", clock);

        await using (var context = factory.Create())
        {
            context.SourceDocuments.AddRange(older, newer, fresh, english);
            await context.SaveChangesAsync(ct);
        }

        var repository = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IKnowledgeRepository>();

        Assert.Equal([waiting], await repository.WaitingAsync(10, ct));
    }

    /// <summary>
    /// What a model read in a picture is the product's document and never its
    /// description (phase 13a, I-4), said in SQL: a reading newer than the
    /// description does not become "what the description says now", and comes back
    /// with the picture, the model and the prompt it was stored with — each
    /// picture's newest, by culture. And it is a source of its own in the queue: a
    /// product waits while one of its pictures' current readings does, a reading
    /// replaced by a newer one of the same picture is history, and a product is in
    /// the queue once however many of its sources wait.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_pictures_reading_is_kept_apart_from_the_description_and_comes_back_with_who_read_it()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("knowledge_readings");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var clock = new TestClock();
        var pan = ProductId.New();
        var front = new PictureReading(new ImageId("front"), "Qwen/Qwen3.5-4B", "picture-2026-10-07");
        var side = front with { Picture = new ImageId("side") };

        // One picture read and still waiting, and a later one already proposed from:
        // the later reading is another picture's, and does not make this one history.
        var two = ProductId.New();
        var early = SourceDocument.Depict(two, "es", "Una olla.", front, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var later = SourceDocument.Depict(two, "es", "Una tapa.", side, clock);
        later.MarkProposed(clock);
        clock.Advance(TimeSpan.FromMinutes(1));

        var description = SourceDocument.Describe(pan, "es", "Sartén de hierro fundido.", clock);
        description.MarkProposed(clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var old = SourceDocument.Depict(pan, "es", "Una sartén.", front, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var spanish = SourceDocument.Depict(pan, "es", "Una sartén de hierro con mango largo.", front, clock);
        var english = SourceDocument.Depict(pan, "en", "An iron pan with a long handle.", front, clock);
        var other = SourceDocument.Depict(pan, "es", "Una sartén vista de lado.", side, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var onlyRead = ProductId.New();
        var alone = SourceDocument.Depict(onlyRead, "es", "Un cazo.", front, clock);
        var done = ProductId.New();
        var proposed = SourceDocument.Depict(done, "es", "Una jarra.", front, clock);
        proposed.MarkProposed(clock);

        await using (var context = factory.Create())
        {
            context.SourceDocuments.AddRange(early, later, description, old, spanish, english, other, alone, proposed);
            await context.SaveChangesAsync(ct);
        }

        var repository = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IKnowledgeRepository>();

        Assert.Equal([description.Id], (await repository.CurrentDocumentsAsync(pan, ct)).Select(document => document.Id));
        // The pan once, for its two pictures; the product with only a reading; not the one already proposed from.
        Assert.Equal([two, pan, onlyRead], await repository.WaitingAsync(10, ct));
        Assert.Equal([two], await repository.WaitingAsync(1, ct));
        Assert.Equal(
            [english.Id, other.Id, spanish.Id],
            (await repository.CurrentReadingsAsync(pan, ct)).Select(document => document.Id).OrderBy(id => id.Value == english.Id.Value ? 0 : id.Value == other.Id.Value ? 1 : 2));

        var readings = await repository.CurrentReadingsAsync(pan, front.Picture, ct);
        Assert.Equal([english.Id, spanish.Id], readings.OrderBy(document => document.Culture).Select(document => document.Id));
        Assert.All(readings, document => Assert.Equal(front, document.Reading));
        Assert.Equal([other.Id], (await repository.CurrentReadingsAsync(pan, side.Picture, ct)).Select(document => document.Id));
    }

    /// <summary>
    /// The review queue pages and counts in Postgres (R-7.2), over support that
    /// lives in the evidence's jsonb: a claim's support is its weakest quote, weak
    /// is under 0.8 and 0.8 itself is not, four claims tie on support and time and
    /// the id parts them, and an approved claim is not waiting at all.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_review_queue_pages_weakest_first_and_counts_the_weak_over_the_whole_queue()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("knowledge_queue_paging");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var lamp = ProductId.New();
        var document = SourceDocument.Describe(lamp, "es", "Flexo LED con tres temperaturas de color y brazo articulado.", Clock);
        ProductClaim Claim(params double[] supports) => ProductClaim.Propose(
            lamp,
            LocalizedText.From("es", "Tiene brazo articulado.").With("en", "It has an articulated arm."),
            null, null,
            [.. supports.Select(support => new Evidence(document.Id, document.Locate("brazo articulado")!, support))],
            new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e", "claims-2026-09-26", "run", Clock.GetUtcNow()),
            Clock);

        var strong = Claim(0.9);
        var tied = Enumerable.Range(0, 4).Select(_ => Claim(0.5)).ToList();
        var atTheLine = Claim(ProductClaim.WeakSupport);
        var underTheLine = Claim(0.79);
        var thinQuote = Claim(0.95, 0.6);
        var decided = Claim(0.1);
        decided.Approve("shopkeeper@tendero", Clock);

        await using (var context = factory.Create())
        {
            context.SourceDocuments.Add(document);
            context.Claims.AddRange([strong, .. tied, atTheLine, underTheLine, thinQuote, decided]);
            await context.SaveChangesAsync(ct);
        }

        var repository = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IKnowledgeRepository>();

        var pages = new List<ClaimPage>();
        for (var page = 1; page <= 4; page++)
            pages.Add(await repository.ClaimsAsync(ClaimStatus.Proposed, ClaimSupportFilter.All, page, 2, cancellationToken: ct));

        // Postgres orders a uuid by its bytes, which is the order of its lower-case text.
        Assert.Equal(
            [.. tied.Select(claim => claim.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal),
             thinQuote.Id, underTheLine.Id, atTheLine.Id, strong.Id],
            pages.SelectMany(page => page.Claims).Select(claim => claim.Id));
        Assert.All(pages, page => Assert.Equal(8, page.Total));

        var weak = await repository.ClaimsAsync(ClaimStatus.Proposed, ClaimSupportFilter.Weak, 2, 4, cancellationToken: ct);
        Assert.Equal([thinQuote.Id, underTheLine.Id], weak.Claims.Select(claim => claim.Id));
        Assert.Equal(6, weak.Total);
        Assert.Equal((6, 8), (weak.CountBySupport[ClaimSupportFilter.Weak], weak.CountBySupport[ClaimSupportFilter.All]));
        Assert.Equal(weak.CountBySupport, pages[0].CountBySupport);

        // One product's claims, for its bulk review (S-5): the filter and the counts in SQL.
        var lamps = await repository.ClaimsAsync(ClaimStatus.Proposed, ClaimSupportFilter.All, 1, 20, lamp, ct);
        var none = await repository.ClaimsAsync(ClaimStatus.Proposed, ClaimSupportFilter.All, 1, 20, ProductId.New(), ct);
        Assert.Equal((8, 0, 0), (lamps.Total, none.Total, none.CountBySupport[ClaimSupportFilter.Weak]));
    }
}
