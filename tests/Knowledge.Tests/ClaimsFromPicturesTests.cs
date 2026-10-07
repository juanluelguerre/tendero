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
/// Claims from what a model read in a picture (the phase 13a spec, I-4). **The
/// model reads, code builds the claim**: a reading carries what the picture
/// answered to a closed question — how many — and that answer becomes a typed
/// claim without any model reading the reading's prose. A reading is a source of
/// its own: it is proposed from once, by itself, and what the description says
/// is neither mixed into it nor held back by it.
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

    private Task Read(PictureReading reading, string spanish, string english, int count = 1, double? confidence = null) =>
        new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordPictureReadingCommand(Pan, reading, spanish, english, count, confidence), TestContext.Current.CancellationToken);

    private Task<ProposeClaimsResult> Propose(
        IClaimExtractor? model = null, ISupportScorer? scorer = null, Vocabulary? vocabulary = null, Held? catalogue = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IClaimExtractor, RulesClaimExtractor>(ClaimExtractors.Rules);
        services.AddKeyedSingleton<IClaimExtractor, PictureAnswersExtractor>(ClaimExtractors.Picture);
        if (model is not null)
            services.AddKeyedSingleton(ClaimExtractors.Chat, model);
        if (scorer is not null)
            services.AddSingleton(scorer);

        using var provider = services.BuildServiceProvider();
        return new ProposeClaimsHandler(
                this.knowledge, vocabulary ?? new Vocabulary(), catalogue ?? Held.Nothing, provider,
                KnowledgeServiceCollectionExtensions.Extractor, this.knowledge, this.clock)
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

        public Task<IReadOnlyList<AttributeSpec>> CurrentAsync(ProductId product, SourceKind readFrom, CancellationToken cancellationToken = default)
        {
            Asked.Add(readFrom);
            return Task.FromResult<IReadOnlyList<AttributeSpec>>(readFrom == SourceKind.ProductImage ? [Pieces] : [Pieces, Light]);
        }
    }

    /// <summary>Says what it was offered, and proposes nothing.</summary>
    private sealed class SaysWhatItWasOffered : IClaimExtractor
    {
        public List<IReadOnlyList<string>> Offered { get; } = [];

        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
        {
            Offered.Add([.. attributes.Select(attribute => attribute.Code)]);
            return Task.FromResult<IReadOnlyList<ProposedClaim>>([]);
        }
    }

    /// <summary>The extractor is not there, whatever it is handed.</summary>
    private sealed class IsDown : IClaimExtractor
    {
        public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken) =>
            throw new HttpRequestException("The extractor is not there.");
    }

    private static Held Holding(string pieces) =>
        new(new() { [Pan] = [new HeldValue("PIECES", pieces, pieces)] });

    [Fact]
    public async Task A_picture_that_was_read_waits_for_claims_as_a_description_does()
    {
        await Read(Front, "Sartén negra con mango largo.", "Black pan with a long handle.");

        Assert.Equal([Pan], await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The claim is typed by code from the number the picture answered, and rests
    /// on the reading — the line that says it, in both of its documents. No model
    /// read the reading and none scored the claim: **its support is how sure the
    /// model was of the number it wrote**, which the reading carries.
    /// </summary>
    [Fact]
    public async Task What_a_picture_counted_is_a_typed_claim_built_by_code_that_rests_on_its_reading()
    {
        await Read(Front, "Sartenes negras apiladas.", "Stacked black pans.", count: 3, confidence: 0.97);
        var (model, scorer) = (new QuotesTheFirstWord(), new Hears(0.4));

        var result = await Propose(model, scorer);

        Assert.Equal(1, result.Proposed);
        var claim = Assert.Single(this.knowledge.Claims);
        Assert.Equal(("PIECES", "3"), (claim.AttributeCode, claim.Value));
        Assert.Equal(("piezas: 3", "pieces: 3"), (claim.Statement.In("es"), claim.Statement.In("en")));
        Assert.Equal([Document(Front, "es").Id, Document(Front, "en").Id], claim.Evidence.Select(evidence => evidence.Document));
        Assert.Equal(
            ["Elementos contados en la imagen: 3 (confianza 97 %).", "Items counted in the picture: 3 (confidence 97 %)."],
            claim.Evidence.Select(evidence => evidence.Quote.Text));
        Assert.Equal(0.97, claim.Support);
        Assert.Empty(model.Read);
        Assert.Empty(scorer.Heard);
        // Who answered: the model that read the picture, under the instructions it was read with.
        Assert.Equal(
            (ClaimExtractors.Picture, Front.Model, Front.PromptVersion),
            (claim.Provenance.Extractor, claim.Provenance.ModelId, claim.Provenance.PromptVersion));
        Assert.Empty(await this.knowledge.WaitingAsync(5, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// **No sentence is proposed from a reading's prose.** The same weights that
    /// wrote it typed a strap's grey as a lamp's colour temperature when asked to
    /// extract from it, and left some sixty untyped sentences for twelve pictures.
    /// The prose stays as the document a reviewer reads; with or without a model,
    /// nothing reads it for claims — not the rules either, which would take its
    /// "2 L" for a capacity.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_sentence_is_proposed_from_a_readings_prose(bool withAModel)
    {
        await Read(Front, "Tres ollas de 2 L con correa gris.", "Three 2 L pots with a gray strap.");
        var model = new QuotesTheFirstWord();

        var result = withAModel ? await Propose(model, new Hears(0.9)) : await Propose();

        Assert.Equal(0, result.Proposed);
        Assert.Empty(this.knowledge.Claims);
        Assert.Empty(model.Read);
        Assert.False(Document(Front, "es").IsWaiting);
    }

    /// <summary>
    /// A single item says nothing of a product that does not say how many it is:
    /// every picture of one thing would otherwise propose "pieces: 1". Of a
    /// product that does say, one is an answer — the catalogue's, or against it,
    /// and the review queue puts the two side by side.
    /// </summary>
    [Theory]
    [InlineData(null, 0)]
    [InlineData("3", 1)]
    [InlineData("1", 1)]
    public async Task A_single_item_is_a_claim_only_of_a_product_that_says_how_many_it_is(string? held, int proposed)
    {
        await Read(Front, "Una sartén negra.", "A black pan.", count: 1);

        var result = await Propose(catalogue: held is null ? Held.Nothing : Holding(held));

        Assert.Equal(proposed, result.Proposed);
        Assert.Equal(proposed, this.knowledge.Claims.Count(claim => claim is { AttributeCode: "PIECES", Value: "1" }));
    }

    /// <summary>
    /// An answer that came with no probability is neither weak nor sure: its
    /// support sits on the line between, so it is not flagged as thin and is not
    /// approved with others either.
    /// </summary>
    [Fact]
    public async Task An_answer_with_no_probability_is_neither_weak_nor_sure()
    {
        await Read(Front, "Sartenes negras.", "Black pans.", count: 3);

        await Propose();

        var claim = Assert.Single(this.knowledge.Claims);
        Assert.Equal(ProductClaim.WeakSupport, claim.Support);
        Assert.False(claim.IsWeaklySupported);
        Assert.True(claim.Support < ProductClaim.SureAnswer);
    }

    /// <summary>More than one is said whatever the catalogue holds: that is how a set nobody typed gets its pieces.</summary>
    [Fact]
    public async Task More_than_one_is_proposed_of_a_product_that_holds_nothing()
    {
        await Read(Front, "Sartenes negras.", "Black pans.", count: 2);

        Assert.Equal(1, (await Propose()).Proposed);
    }

    /// <summary>
    /// The description and two pictures: three sources, each by itself. The model
    /// is handed the description and never a reading; each picture's claim rests
    /// on that picture's reading.
    /// </summary>
    [Fact]
    public async Task The_description_and_each_picture_are_read_apart_and_each_claim_rests_on_its_own_source()
    {
        await Describe("es", "Cazo pequeño.");
        await Describe("en", "Small saucepan.");
        await Read(Front, "Sartenes negras.", "Black pans.", count: 3);
        await Read(Side, "Mangos de madera.", "Wooden handles.", count: 2);
        var model = new QuotesTheFirstWord();

        var result = await Propose(model, new Hears(0.9));

        Assert.Equal(3, result.Proposed);
        Assert.Equal([("Cazo pequeño.", "Small saucepan.")], model.Read.Select(text => (text.Spanish, text.English)));
        var counted = this.knowledge.Claims.Where(claim => claim.AttributeCode == "PIECES").ToDictionary(claim => claim.Value!, claim => claim.Evidence[0].Document);
        Assert.Equal(Document(Front, "es").Id, counted["3"]);
        Assert.Equal(Document(Side, "es").Id, counted["2"]);
        var written = this.knowledge.Claims.Single(claim => claim.AttributeCode is null);
        Assert.Equal(SourceKind.ProductDescription, this.knowledge.Documents.Single(document => document.Id == written.Evidence[0].Document).Kind);
        Assert.All(this.knowledge.Documents, document => Assert.False(document.IsWaiting));
    }

    /// <summary>
    /// Each kind of source has its vocabulary — a picture's is what a picture can
    /// show — and the model is offered the description's alone.
    /// </summary>
    [Fact]
    public async Task Each_source_is_read_with_the_vocabulary_of_its_kind()
    {
        await Describe("es", "Flexo blanco.");
        await Describe("en", "White lamp.");
        await Read(Front, "Correas grises.", "Gray straps.", count: 2);
        var model = new SaysWhatItWasOffered();
        var vocabulary = new Vocabulary();

        await Propose(model, new Hears(0.9), vocabulary);

        Assert.Equal([SourceKind.ProductImage, SourceKind.ProductDescription], vocabulary.Asked);
        Assert.Equal<IEnumerable<string>>([["PIECES", "COLOUR_TEMPERATURE"]], model.Offered);
    }

    /// <summary>What was proposed from is not proposed from again: a second pass finds only the picture read since.</summary>
    [Fact]
    public async Task Only_what_is_waiting_is_read_again()
    {
        await Describe("es", "Cazo pequeño.");
        await Propose(new QuotesTheFirstWord(), new Hears(0.9));
        await Read(Front, "Sartenes negras.", "Black pans.", count: 3);
        var model = new QuotesTheFirstWord();

        var result = await Propose(model, new Hears(0.9));

        Assert.Equal(1, result.Proposed);
        Assert.Empty(model.Read);
    }

    /// <summary>A picture read again to another answer: the old reading is history, and only the new one is proposed from.</summary>
    [Fact]
    public async Task A_reading_that_was_replaced_is_not_proposed_from()
    {
        await Read(Front, "Sartenes negras.", "Black pans.", count: 2);
        this.clock.Advance(TimeSpan.FromMinutes(1));
        await Read(Front, "Ollas negras.", "Black pots.", count: 3);

        await Propose();

        Assert.Equal("3", Assert.Single(this.knowledge.Claims).Value);
    }

    /// <summary>
    /// **The readings first, and each saved when it is done.** A reading asks no
    /// model, so the extractor being down for the description neither holds a
    /// picture's claim back nor takes it down: it is saved before the failure,
    /// and only the description is left waiting, to be tried again.
    /// </summary>
    [Fact]
    public async Task A_description_that_fails_does_not_take_down_the_reading_proposed_before_it()
    {
        await Describe("es", "Cazo pequeño.");
        await Read(Front, "Sartenes negras.", "Black pans.", count: 3);
        var saves = this.knowledge.SaveCount;

        await Assert.ThrowsAsync<HttpRequestException>(() => Propose(new IsDown(), new Hears(0.9)));

        // Saved before the failure, not only added: in memory an unsaved claim looks the same.
        Assert.Equal(saves + 1, this.knowledge.SaveCount);

        Assert.Equal("PIECES", Assert.Single(this.knowledge.Claims).AttributeCode);
        Assert.True(this.knowledge.Documents.Single(document => document.Kind == SourceKind.ProductDescription).IsWaiting);
        Assert.False(Document(Front, "es").IsWaiting);
    }
}
