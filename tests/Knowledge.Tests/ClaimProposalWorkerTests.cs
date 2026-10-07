using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ProposeClaims;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The queue's passes, one product at a time (ADR 0018; the phase 13a spec, I-4):
/// what a pass does when a source brings the extractor down — the sources
/// proposed before it stay proposed, the one that failed is retried until its
/// limit and then left with its reason, and the queue empties instead of looping.
/// </summary>
public sealed class ClaimProposalWorkerTests
{
    private static readonly ProductId Pan = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("front"), "Qwen/Qwen3.5-4B", PictureReadingPrompt.Version);

    /// <summary>Quotes the first word of a description about a saucepan, and is not there for any other.</summary>
    private sealed class ReadsOnlySaucepans : IClaimExtractor
    {
        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken) =>
            text.Spanish.StartsWith("Cazo", StringComparison.Ordinal)
                ? Task.FromResult<IReadOnlyList<ProposedClaim>>([new ProposedClaim("Cazo", "", "Es un cazo.", "It is a saucepan.", null, null, 0.9)])
                : throw new HttpRequestException("The extractor is not there.");
    }

    private sealed class Supports : ISupportScorer
    {
        public Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken) => Task.FromResult(0.9);
    }

    /// <summary>Only what a picture can show: how many.</summary>
    private sealed class OnlyPieces : IAttributeVocabulary
    {
        public Task<IReadOnlyList<AttributeSpec>> CurrentAsync(ProductId product, SourceKind readFrom, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttributeSpec>>([new AttributeSpec("PIECES", "Number", null, "pieces", [], "piezas")]);
    }

    private sealed class Nobody : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, "worker", "worker", []);
    }

    private static (ClaimProposalWorker Worker, InMemoryKnowledge Knowledge, ServiceProvider Provider) Compose(TestClock clock)
    {
        var knowledge = new InMemoryKnowledge();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ProposeClaimsCommand).Assembly);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPrincipalAccessor>(new Nobody());
        services.AddSingleton<IKnowledgeRepository>(knowledge);
        services.AddSingleton<IUnitOfWork>(knowledge);
        services.AddSingleton<IAttributeVocabulary>(new OnlyPieces());
        services.AddSingleton<ICatalogueValues>(Held.Nothing);
        services.AddKnowledge(new ConfigurationBuilder().Build());
        services.AddKeyedSingleton<IClaimExtractor>(ClaimExtractors.Chat, new ReadsOnlySaucepans());
        services.AddSingleton<ISupportScorer>(new Supports());
        var provider = services.BuildServiceProvider();

        return (
            new ClaimProposalWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ClaimProposalWorker>.Instance, clock),
            knowledge,
            provider);
    }

    [Fact]
    public async Task A_description_that_keeps_failing_is_given_up_with_its_reason_and_the_reading_proposed_before_it_stays_proposed()
    {
        var clock = new TestClock();
        var (worker, knowledge, provider) = Compose(clock);
        await using var _ = provider;
        var ct = TestContext.Current.CancellationToken;
        knowledge.Add(SourceDocument.Describe(Pan, "es", "Sartenes de hierro.", clock));
        knowledge.Add(SourceDocument.Depict(Pan, "es", PictureAnswers.Under("Sartenes negras.", "es", 3), Front, clock));
        knowledge.Add(SourceDocument.Depict(Pan, "en", PictureAnswers.Under("Black pans.", "en", 3), Front, clock));

        // Three passes fail on the description; the first of them proposed from the reading, which asks no model.
        for (var attempt = 1; attempt <= SourceDocument.MaxAttempts; attempt++)
            Assert.Equal(ClaimProposalWorker.Pass.Failed, await worker.ProposeNextAsync(ct));

        Assert.Equal(ClaimProposalWorker.Pass.Idle, await worker.ProposeNextAsync(ct));

        var claim = Assert.Single(knowledge.Claims);
        Assert.Equal(("PIECES", "3"), (claim.AttributeCode, claim.Value));
        Assert.All(knowledge.Documents.Where(document => document.Kind == SourceKind.ProductImage), reading =>
            Assert.Equal((0, true), (reading.FailedAttempts, reading.ProposedAt is not null)));
        var description = knowledge.Documents.Single(document => document.Kind == SourceKind.ProductDescription);
        Assert.Equal(SourceDocument.MaxAttempts, description.FailedAttempts);
        Assert.Contains("The extractor is not there.", description.LastFailure);
        Assert.False(description.IsWaiting);
    }

    [Fact]
    public async Task With_nothing_waiting_a_pass_is_idle()
    {
        var (worker, _, provider) = Compose(new TestClock());
        await using var disposal = provider;

        Assert.Equal(ClaimProposalWorker.Pass.Idle, await worker.ProposeNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pass_proposes_from_everything_that_waits_of_the_oldest_product()
    {
        var clock = new TestClock();
        var (worker, knowledge, provider) = Compose(clock);
        await using var disposal = provider;
        knowledge.Add(SourceDocument.Describe(Pan, "es", "Cazo pequeño.", clock));

        Assert.Equal(ClaimProposalWorker.Pass.Proposed, await worker.ProposeNextAsync(TestContext.Current.CancellationToken));

        Assert.Single(knowledge.Claims);
        Assert.Empty(await knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }
}
