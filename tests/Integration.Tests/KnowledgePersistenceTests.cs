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
}
