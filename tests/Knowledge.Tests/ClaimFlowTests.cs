using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ProposeClaims;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The two halves of proposing claims, over in-memory ports: recording what a
/// description says now (on the outbox, fast), and proposing from it (off the
/// outbox, with the model when there is one and the rules when there is not).
/// </summary>
public sealed class ClaimFlowTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private static readonly ProductId Pan = ProductId.New();

    private Task<RecordDescriptionResult> Record(string culture, string text) =>
        new RecordDescriptionHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordDescriptionCommand(Pan, culture, text), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_new_text_is_recorded_and_the_same_text_again_is_nothing()
    {
        Assert.Equal(RecordDescriptionOutcome.Recorded, (await Record("es", "Sartén de hierro fundido.")).Outcome);
        Assert.Equal(RecordDescriptionOutcome.Unchanged, (await Record("es", "Sartén de hierro fundido.")).Outcome);
        Assert.Single(this.knowledge.Documents);
    }

    [Fact]
    public async Task A_changed_text_supersedes_the_claims_resting_on_the_old_one()
    {
        await Record("es", "Sartén de hierro fundido, apta para inducción.");
        await Propose(rulesOnly: true);
        var standing = this.knowledge.Claims.Count(claim => claim.Status == ClaimStatus.Proposed);
        Assert.True(standing > 0);

        this.clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Record("es", "Sartén de hierro fundido, apta para vitrocerámica.");

        Assert.Equal(standing, result.Superseded);
        Assert.All(this.knowledge.Claims, claim => Assert.Equal(ClaimStatus.Superseded, claim.Status));
    }

    /// <summary>A claim resting on both languages falls when either changes: it rests on the old words too.</summary>
    [Fact]
    public async Task A_claim_resting_on_both_languages_is_superseded_when_only_one_changes()
    {
        await Record("es", "Cazo pequeño con asa fría.");
        await Record("en", "Small pan with a cool handle.");
        await Propose(rulesOnly: false,
            new FixedExtractor(new ProposedClaim("asa fría", "cool handle", "El asa no se calienta.", "The handle stays cool.", null, null, 0.9)),
            new FixedScorer(0.9));
        Assert.Equal(2, Assert.Single(this.knowledge.Claims).Evidence.Count);
        var saves = this.knowledge.SaveCount;

        this.clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Record("es", "Cazo pequeño con asa de madera.");

        Assert.Equal(1, result.Superseded);
        Assert.Equal(ClaimStatus.Superseded, this.knowledge.Claims.Single().Status);
        Assert.Equal(saves + 1, this.knowledge.SaveCount);
    }

    [Fact]
    public async Task A_text_that_comes_back_is_reinstated_and_waits_again()
    {
        await Record("es", "Texto A.");
        await Propose(rulesOnly: true);
        this.clock.Advance(TimeSpan.FromMinutes(1));
        await Record("es", "Texto B.");
        this.clock.Advance(TimeSpan.FromMinutes(1));

        await Record("es", "Texto A.");

        Assert.Equal(2, this.knowledge.Documents.Count);
        Assert.Equal([Pan], await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    /// <summary>With no model the rules read the text, and a rule's claim is the text's own words: supported by construction.</summary>
    [Fact]
    public async Task Without_a_model_the_rules_propose_and_their_claims_are_fully_supported()
    {
        await Record("es", "Hervidor de 1,7 litros, apto para inducción.");

        var result = await Propose(rulesOnly: true);

        Assert.Equal(ClaimExtractors.Rules, result.Extractor);
        Assert.NotEmpty(this.knowledge.Claims);
        Assert.All(this.knowledge.Claims, claim => Assert.Equal(1.0, claim.Support));
        Assert.All(this.knowledge.Claims, claim => Assert.Equal(("rules", (string?)null, (string?)null, (string?)null),
            (claim.Provenance.Extractor, claim.Provenance.ModelId, claim.Provenance.ModelRevision, claim.Provenance.PromptVersion)));
        Assert.Empty(await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task With_the_model_its_claims_are_vetted_scored_and_proposed_with_their_provenance()
    {
        await Record("es", "Cazo pequeño con asa fría.");
        await Record("en", "Small pan with a cool handle.");
        var model = new FixedExtractor(
            new ProposedClaim("asa fría", "cool handle", "El asa no se calienta.", "The handle stays cool.", null, null, 0.9),
            new ProposedClaim("es impermeable", "", "Es impermeable.", "It is waterproof.", null, null, 0.9));

        using var metrics = new InstrumentRecorder("tendero.knowledge.claims.dropped");
        var result = await Propose(rulesOnly: false, model, new FixedScorer(0.83));

        Assert.Equal((1, ClaimExtractors.Chat), (result.Proposed, result.Extractor));
        Assert.Equal([DropReason.NotInTheText], result.Dropped);
        Assert.Contains(metrics.Of("tendero.knowledge.claims.dropped"), tags => Equals(tags["reason"], "not_in_the_text"));
        var claim = Assert.Single(this.knowledge.Claims);
        Assert.Equal(0.83, claim.Support);
        Assert.Equal(2, claim.Evidence.Count);
        Assert.Equal("851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a", claim.Provenance.ModelRevision);
        Assert.Equal(ChatClaimExtractor.PromptVersion, claim.Provenance.PromptVersion);
    }

    /// <summary>A model needs its scorer: registered without one, the rules read the text — an unscored claim is not proposed as the model's.</summary>
    [Fact]
    public async Task A_model_without_its_support_scorer_is_not_used()
    {
        await Record("es", "Hervidor de 1,7 litros.");
        var model = new FixedExtractor(new ProposedClaim("Hervidor", "", "Es un hervidor.", "It is a kettle.", null, null, 0.9));

        var result = await Propose(rulesOnly: false, model, scorer: null);

        Assert.Equal(ClaimExtractors.Rules, result.Extractor);
        Assert.Null(model.Read);
    }

    [Fact]
    public async Task The_model_reads_both_texts_the_scorer_hears_the_spanish_statement_and_both_documents_are_done()
    {
        await Record("es", "Cazo pequeño con asa fría.");
        await Record("en", "Small pan with a cool handle.");
        var model = new FixedExtractor(new ProposedClaim("asa fría", "cool handle", "El asa no se calienta.", "The handle stays cool.", null, null, 0.9));
        var scorer = new FixedScorer(0.9);
        using var metrics = new InstrumentRecorder("tendero.knowledge.claims.proposed");
        var saves = this.knowledge.SaveCount;

        await Propose(rulesOnly: false, model, scorer);

        Assert.Equal(("Cazo pequeño con asa fría.", "Small pan with a cool handle."), (model.Read!.Spanish, model.Read.English));
        Assert.Equal(["El asa no se calienta."], scorer.Heard);
        Assert.All(this.knowledge.Documents, document => Assert.NotNull(document.ProposedAt));
        Assert.Equal(saves + 1, this.knowledge.SaveCount);
        Assert.Contains(metrics.Of("tendero.knowledge.claims.proposed"), tags => Equals(tags["extractor"], ClaimExtractors.Chat));
    }

    [Fact]
    public async Task With_no_english_text_the_model_is_given_an_empty_one()
    {
        await Record("es", "Cazo pequeño con asa fría.");
        var model = new FixedExtractor();

        await Propose(rulesOnly: false, model, new FixedScorer(0.9));

        Assert.Equal("", model.Read!.English);
    }

    /// <summary>Nothing to read: no Spanish text at all, or one already read. Neither is an error, and neither calls an extractor.</summary>
    [Fact]
    public async Task A_product_with_no_spanish_text_waiting_proposes_nothing()
    {
        await Record("en", "Small pan.");
        Assert.Equal("none", (await Propose(rulesOnly: true)).Extractor);

        await Record("es", "Cazo pequeño.");
        await Propose(rulesOnly: true);
        var again = await Propose(rulesOnly: true);

        Assert.Equal((0, "none"), (again.Proposed, again.Extractor));
    }

    private Task<ProposeClaimsResult> Propose(bool rulesOnly, IClaimExtractor? model = null, ISupportScorer? scorer = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IClaimExtractor, RulesClaimExtractor>(ClaimExtractors.Rules);
        if (!rulesOnly)
        {
            services.AddKeyedSingleton(ClaimExtractors.Chat, model!);
            if (scorer is not null)
                services.AddSingleton(scorer);
        }

        using var provider = services.BuildServiceProvider();
        return new ProposeClaimsHandler(this.knowledge, new Vocabulary(), provider, KnowledgeServiceCollectionExtensions.Extractor, this.knowledge, this.clock)
            .HandleAsync(new ProposeClaimsCommand(Pan), TestContext.Current.CancellationToken);
    }

    private sealed class FixedExtractor(params ProposedClaim[] claims) : IClaimExtractor
    {
        /// <summary>What it was given to read, or null when nobody asked it.</summary>
        public ProductText? Read { get; private set; }

        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
        {
            Read = text;
            return Task.FromResult<IReadOnlyList<ProposedClaim>>(claims);
        }
    }

    private sealed class FixedScorer(double support) : ISupportScorer
    {
        public List<string> Heard { get; } = [];

        public Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
        {
            Heard.Add(claim);
            return Task.FromResult(support);
        }
    }

    private sealed class Vocabulary : IAttributeVocabulary
    {
        public Task<IReadOnlyList<AttributeSpec>> CurrentAsync(SourceKind readFrom, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttributeSpec>>(
            [
                new("INDUCTION", "Boolean", null, "induction", [], "inducción"),
                new("CAPACITY_L", "Number", "L", "capacity", [], "capacidad"),
            ]);
    }
}
