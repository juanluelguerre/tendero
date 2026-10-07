using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ProposeClaims;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// Claims from what a model read in a picture (the phase 13a spec, I-4): the same
/// slow half that reads a description reads a picture's reading — the extractor,
/// the vetting, the scorer — and each claim rests on the documents it was taken
/// from. A reading is a source of its own: it is proposed from once, by itself,
/// and what the description says is neither mixed into it nor held back by it.
/// </summary>
public sealed class ClaimsFromPicturesTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private static readonly ProductId Pan = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("front"), "Qwen/Qwen3.5-4B", PictureReadingPrompt.Version);
    private static readonly PictureReading Side = Front with { Picture = new ImageId("side") };

    private Task Describe(string culture, string text) =>
        new RecordDescriptionHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordDescriptionCommand(Pan, culture, text), TestContext.Current.CancellationToken);

    private Task Read(PictureReading reading, string spanish, string english) =>
        new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordPictureReadingCommand(Pan, reading, spanish, english), TestContext.Current.CancellationToken);

    private Task<ProposeClaimsResult> Propose(IClaimExtractor? model = null, ISupportScorer? scorer = null, Vocabulary? vocabulary = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IClaimExtractor, RulesClaimExtractor>(ClaimExtractors.Rules);
        if (model is not null)
            services.AddKeyedSingleton(ClaimExtractors.Chat, model);
        if (scorer is not null)
            services.AddSingleton(scorer);

        using var provider = services.BuildServiceProvider();
        return new ProposeClaimsHandler(this.knowledge, vocabulary ?? new Vocabulary(), provider, KnowledgeServiceCollectionExtensions.Extractor, this.knowledge, this.clock)
            .HandleAsync(new ProposeClaimsCommand(Pan), TestContext.Current.CancellationToken);
    }

    private SourceDocument Document(PictureReading reading, string culture) =>
        this.knowledge.Documents.Single(document => document.Reading?.Picture == reading.Picture && document.Culture == culture);

    /// <summary>Quotes whatever it is given, so each claim can be traced to the text it was read from.</summary>
    private sealed class QuotesTheFirstWord : IClaimExtractor
    {
        public List<ProductText> Read { get; } = [];

        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
        {
            Read.Add(text);
            var (spanish, english) = (text.Spanish.Split(' ')[0], text.English.Split(' ')[0]);
            return Task.FromResult<IReadOnlyList<ProposedClaim>>(
                [new ProposedClaim(spanish, english, $"Dice «{spanish}».", $"It says \"{english}\".", null, null, 0.9)]);
        }
    }

    private sealed class Hears(double support) : ISupportScorer
    {
        public List<(string Text, string Claim)> Heard { get; } = [];

        public Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
        {
            Heard.Add((text, claim));
            return Task.FromResult(support);
        }
    }

    /// <summary>What a description may type, and the little of it a picture can show.</summary>
    private sealed class Vocabulary : IAttributeVocabulary
    {
        private static readonly AttributeSpec Pieces = new("PIECES", "Number", null, "pieces", [], "piezas");
        private static readonly AttributeSpec Light = new("COLOUR_TEMPERATURE", "Text", null, "colour temperature", [], "temperatura de color");

        public List<SourceKind> Asked { get; } = [];

        public Task<IReadOnlyList<AttributeSpec>> CurrentAsync(SourceKind readFrom, CancellationToken cancellationToken = default)
        {
            Asked.Add(readFrom);
            return Task.FromResult<IReadOnlyList<AttributeSpec>>(readFrom == SourceKind.ProductImage ? [Pieces] : [Pieces, Light]);
        }
    }

    /// <summary>Types whatever it reads as a lamp's colour temperature, and says what it was offered.</summary>
    private sealed class TypesEverythingAsLight : IClaimExtractor
    {
        public List<IReadOnlyList<string>> Offered { get; } = [];

        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
        {
            Offered.Add([.. attributes.Select(attribute => attribute.Code)]);
            var (spanish, english) = (text.Spanish.Split(' ')[0], text.English.Split(' ')[0]);
            return Task.FromResult<IReadOnlyList<ProposedClaim>>(
                [new ProposedClaim(spanish, english, $"Dice «{spanish}».", $"It says \"{english}\".", "COLOUR_TEMPERATURE", "gray", 0.9)]);
        }
    }

    [Fact]
    public async Task A_picture_that_was_read_waits_for_claims_as_a_description_does()
    {
        await Read(Front, "Sartén negra con mango largo.", "Black pan with a long handle.");

        Assert.Equal([Pan], await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    /// <summary>The claim rests on the reading — both of its documents — and on nothing else, and the model that scored it heard the reading's own words.</summary>
    [Fact]
    public async Task A_claim_read_from_a_picture_rests_on_that_pictures_reading_and_is_scored_against_it()
    {
        await Read(Front, "Sartén negra con mango largo.", "Black pan with a long handle.");
        var scorer = new Hears(0.91);

        var result = await Propose(new QuotesTheFirstWord(), scorer);

        Assert.Equal((1, ClaimExtractors.Chat), (result.Proposed, result.Extractor));
        var claim = Assert.Single(this.knowledge.Claims);
        Assert.Equal([Document(Front, "es").Id, Document(Front, "en").Id], claim.Evidence.Select(evidence => evidence.Document));
        Assert.Equal(["Sartén", "Black"], claim.Evidence.Select(evidence => evidence.Quote.Text));
        Assert.Equal(0.91, claim.Support);
        Assert.Equal([("Sartén negra con mango largo.", "Dice «Sartén».")], scorer.Heard);
        Assert.Empty(await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The description and two pictures: three sources, each read by itself. A
    /// reading's claim never quotes the description, and the model is never handed
    /// the two as one text — a claim would then rest on a document it was not in.
    /// </summary>
    [Fact]
    public async Task The_description_and_each_picture_are_read_apart_and_each_claim_rests_on_its_own_source()
    {
        await Describe("es", "Cazo pequeño.");
        await Describe("en", "Small saucepan.");
        await Read(Front, "Sartén negra.", "Black pan.");
        await Read(Side, "Mango de madera.", "Wooden handle.");
        var model = new QuotesTheFirstWord();

        var result = await Propose(model, new Hears(0.9));

        Assert.Equal(3, result.Proposed);
        Assert.Equal(
            [("Cazo pequeño.", "Small saucepan."), ("Sartén negra.", "Black pan."), ("Mango de madera.", "Wooden handle.")],
            model.Read.Select(text => (text.Spanish, text.English)));
        var quoted = this.knowledge.Claims.ToDictionary(claim => claim.Evidence[0].Quote.Text, claim => claim.Evidence[0].Document);
        Assert.Equal(Document(Front, "es").Id, quoted["Sartén"]);
        Assert.Equal(Document(Side, "es").Id, quoted["Mango"]);
        Assert.Equal(SourceKind.ProductDescription, this.knowledge.Documents.Single(document => document.Id == quoted["Cazo"]).Kind);
        Assert.All(this.knowledge.Documents, document => Assert.False(document.IsWaiting));
    }

    /// <summary>
    /// **A picture is offered only what a picture can show.** Handed the whole
    /// vocabulary, the extractor typed a strap's grey as a lamp's colour
    /// temperature: it fits what it reads into what it is given. Each source is
    /// read with the vocabulary of its kind.
    /// </summary>
    [Fact]
    public async Task Each_source_is_read_with_the_vocabulary_of_its_kind()
    {
        await Describe("es", "Flexo blanco.");
        await Describe("en", "White lamp.");
        await Read(Front, "Correa gris.", "Gray strap.");
        var model = new TypesEverythingAsLight();
        var vocabulary = new Vocabulary();

        await Propose(model, new Hears(0.9), vocabulary);

        Assert.Equal([SourceKind.ProductDescription, SourceKind.ProductImage], vocabulary.Asked);
        Assert.Equal<IEnumerable<string>>([["PIECES", "COLOUR_TEMPERATURE"], ["PIECES"]], model.Offered);
    }

    /// <summary>
    /// And when the model types it anyway, code refuses the typing and keeps the
    /// sentence: what it saw is still true, it is the drawer that was wrong.
    /// </summary>
    [Fact]
    public async Task A_typing_a_picture_cannot_show_is_refused_and_the_sentence_is_kept()
    {
        await Describe("es", "Flexo blanco.");
        await Read(Front, "Correa gris.", "Gray strap.");

        await Propose(new TypesEverythingAsLight(), new Hears(0.9));

        var written = this.knowledge.Claims.Single(claim => claim.Evidence[0].Quote.Text == "Flexo");
        var read = this.knowledge.Claims.Single(claim => claim.Evidence[0].Quote.Text == "Correa");
        Assert.Equal(("COLOUR_TEMPERATURE", "gray"), (written.AttributeCode, written.Value));
        Assert.Equal(((string?)null, (string?)null), (read.AttributeCode, read.Value));
        Assert.Equal("Dice «Correa».", read.Statement.In("es"));
    }

    /// <summary>What was proposed from is not proposed from again: a second pass finds only the picture read since.</summary>
    [Fact]
    public async Task Only_what_is_waiting_is_read_again()
    {
        await Describe("es", "Cazo pequeño.");
        await Propose(new QuotesTheFirstWord(), new Hears(0.9));
        await Read(Front, "Sartén negra.", "Black pan.");
        var model = new QuotesTheFirstWord();

        var result = await Propose(model, new Hears(0.9));

        Assert.Equal(1, result.Proposed);
        Assert.Equal("Sartén negra.", Assert.Single(model.Read).Spanish);
    }

    /// <summary>A picture read again to other words: the old reading is history, and only the new one is proposed from.</summary>
    [Fact]
    public async Task A_reading_that_was_replaced_is_not_proposed_from()
    {
        await Read(Front, "Sartén negra.", "Black pan.");
        this.clock.Advance(TimeSpan.FromMinutes(1));
        await Read(Front, "Olla negra.", "Black pot.");
        var model = new QuotesTheFirstWord();

        await Propose(model, new Hears(0.9));

        Assert.Equal("Olla negra.", Assert.Single(model.Read).Spanish);
    }

    /// <summary>
    /// With no model the rules read a reading as they read a description
    /// (invariant 8): what a rule proposes is the text's own words, and here the
    /// text is a model's — the claim says which picture and which model, and a
    /// person reviews it as one read from a picture.
    /// </summary>
    [Fact]
    public async Task Without_a_model_the_rules_read_a_reading_too()
    {
        await Read(Front, "Tres sartenes apiladas.", "Three stacked pans.");

        var result = await Propose();

        Assert.Equal(ClaimExtractors.Rules, result.Extractor);
        var claim = Assert.Single(this.knowledge.Claims);
        Assert.Equal(("PIECES", "3"), (claim.AttributeCode, claim.Value));
        Assert.Equal(Front, this.knowledge.Documents.Single(document => document.Id == claim.Evidence[0].Document).Reading);
    }

    /// <summary>
    /// What was read before the failure is kept: the description's claim is saved
    /// when the picture's reading brings the extractor down, and only the reading
    /// is left waiting, to be tried again.
    /// </summary>
    [Fact]
    public async Task A_source_that_fails_does_not_take_down_the_one_read_before_it()
    {
        await Describe("es", "Cazo pequeño.");
        await Read(Front, "Sartén negra.", "Black pan.");
        var saves = this.knowledge.SaveCount;

        await Assert.ThrowsAsync<HttpRequestException>(() => Propose(new FailsOnPictures(), new Hears(0.9)));

        // Saved before the failure, not only added: in memory an unsaved claim looks the same.
        Assert.Equal(saves + 1, this.knowledge.SaveCount);

        Assert.Equal("Cazo", Assert.Single(this.knowledge.Claims).Evidence[0].Quote.Text);
        Assert.False(this.knowledge.Documents.Single(document => document.Kind == SourceKind.ProductDescription).IsWaiting);
        Assert.True(Document(Front, "es").IsWaiting);
    }

    /// <summary>Reads a description and fails on a picture's reading.</summary>
    private sealed class FailsOnPictures : IClaimExtractor
    {
        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken) =>
            text.Spanish.StartsWith("Cazo", StringComparison.Ordinal)
                ? new QuotesTheFirstWord().ExtractAsync(text, attributes, cancellationToken)
                : throw new HttpRequestException("The extractor is not there.");
    }
}
