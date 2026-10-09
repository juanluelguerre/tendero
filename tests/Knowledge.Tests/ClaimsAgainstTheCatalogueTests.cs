using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// A claim against what the catalogue already holds (the phase 13a spec, I-4:
/// *Picture against data*, and for a claim from any source by the author's
/// ruling). Where a typed claim says one value and the product holds another, the
/// queue says so and names both. It corrects neither: which one is wrong is a
/// person's call — the wok's description says carbon steel and its attribute
/// stainless — so such a claim is opened and decided on its own.
/// </summary>
public sealed class ClaimsAgainstTheCatalogueTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private readonly ProductId wok = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("wok-front"), "qwen3.5-4b-q8_0", PictureReadingPrompt.Version);

    private ProductClaim Propose(string? attribute, string? value, double support = 0.95, bool fromPicture = false, ProductId? product = null)
    {
        var of = product ?? this.wok;
        var document = fromPicture
            ? SourceDocument.Depict(of, "es", "Un wok de acero al carbono con mango de madera.", Front, this.clock)
            : SourceDocument.Describe(of, "es", "Wok de acero al carbono con mango de madera.", this.clock);
        if (this.knowledge.Documents.All(existing => existing.Id != document.Id))
            this.knowledge.Add(document);

        var claim = ProductClaim.Propose(
            of,
            LocalizedText.From("es", "Es de acero al carbono.").With("en", "It is carbon steel."),
            attribute, value,
            [new Evidence(document.Id, document.Locate("acero al carbono")!, support)],
            new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e", "claims-2026-09-26", "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    private Held Holds(params (string Code, string Key, string Shown)[] values) =>
        new(new() { [this.wok] = [.. values.Select(value => new HeldValue(value.Code, value.Key, value.Shown))] });

    private async Task<ClaimForReview> Queued(ProductClaim claim, ICatalogueValues catalogue) =>
        (await new ListClaimsForReviewHandler(this.knowledge, catalogue)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken))
        .Items.Single(item => item.ClaimId == claim.Id.ToString());

    private Task<ReviewClaimResult> Decide(ProductClaim claim, ICatalogueValues catalogue, bool inBulk, ReviewDecision decision = ReviewDecision.Approve) =>
        new ReviewClaimHandler(this.knowledge, new Reviewer("Ana Tendera"), this.knowledge, this.clock, catalogue)
            .HandleAsync(new ReviewClaimCommand(claim.Id, decision, inBulk), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_claim_that_says_another_value_than_the_catalogue_holds_names_both()
    {
        var claim = Propose("MATERIAL", "CARBON_STEEL");

        var queued = await Queued(claim, Holds(("MATERIAL", "STAINLESS_STEEL", "acero inoxidable")));

        Assert.Equal(new CatalogueContradiction("MATERIAL", "CARBON_STEEL", "acero inoxidable"), queued.Contradicts);
        Assert.Equal(AloneReason.ContradictsTheCatalogue, queued.DecidedAlone);
    }

    /// <summary>The same value is no contradiction, however each side wrote it: an option's code in another case, a number with or without its decimals.</summary>
    [Theory]
    [InlineData("MATERIAL", "CARBON_STEEL", "carbon_steel")]
    [InlineData("CAPACITY_L", "4", "4.0")]
    [InlineData("CAPACITY_L", "0.50", "0.5")]
    [InlineData("INDUCTION", "true", "True")]
    [InlineData("DIAMETERS_CM", "20, 24, 28", " 20, 24, 28 ")]
    public async Task A_claim_that_says_what_the_catalogue_holds_contradicts_nothing(string attribute, string claimed, string held)
    {
        var queued = await Queued(Propose(attribute, claimed), Holds((attribute, held, held)));

        Assert.Null(queued.Contradicts);
        Assert.Null(queued.DecidedAlone);
    }

    [Fact]
    public async Task Two_numbers_that_differ_are_a_contradiction()
    {
        var queued = await Queued(Propose("CAPACITY_L", "4"), Holds(("CAPACITY_L", "4.5", "4,5 L")));

        Assert.Equal(new CatalogueContradiction("CAPACITY_L", "4", "4,5 L"), queued.Contradicts);
    }

    /// <summary>A value the catalogue does not hold is what a claim is for: it adds, it contradicts nothing. Nor does a sentence that types no attribute, or another attribute's value.</summary>
    [Fact]
    public async Task What_the_catalogue_does_not_hold_cannot_be_contradicted()
    {
        var typed = Propose("MATERIAL", "CARBON_STEEL");
        var untyped = Propose(null, null);

        Assert.Null((await Queued(typed, Holds())).Contradicts);
        Assert.Null((await Queued(typed, Holds(("INDUCTION", "true", "sí")))).Contradicts);
        Assert.Null((await Queued(untyped, Holds(("MATERIAL", "STAINLESS_STEEL", "acero inoxidable")))).Contradicts);
        // A product the catalogue no longer has answers nothing, and that is not a contradiction either.
        Assert.Null((await Queued(typed, new Held([]))).Contradicts);
    }

    /// <summary>
    /// The catalogue is asked once for the page's products, not once a claim: a
    /// queue of twenty claims of one product is one question.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_asked_once_for_the_products_on_the_page()
    {
        Propose("MATERIAL", "CARBON_STEEL");
        Propose("INDUCTION", "true");
        var other = ProductId.New();
        Propose("MATERIAL", "CARBON_STEEL", product: other);
        var catalogue = Holds();

        await new ListClaimsForReviewHandler(this.knowledge, catalogue)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken);

        var asked = Assert.Single(catalogue.Asked);
        Assert.Equal(new[] { this.wok, other }.OrderBy(id => id.Value), asked.OrderBy(id => id.Value));
    }

    /// <summary>Why it is decided alone, one reason: weak support first, then the contradiction, then the picture.</summary>
    [Fact]
    public async Task The_reason_a_claim_is_decided_alone_is_the_first_that_applies()
    {
        var catalogue = Holds(("MATERIAL", "STAINLESS_STEEL", "acero inoxidable"));
        var weak = Propose("MATERIAL", "CARBON_STEEL", support: 0.41, fromPicture: true);

        Assert.Equal(AloneReason.WeakSupport, (await Queued(weak, catalogue)).DecidedAlone);

        this.knowledge.Claims.Clear();
        var read = Propose("MATERIAL", "CARBON_STEEL", fromPicture: true);
        var queued = await Queued(read, catalogue);

        Assert.Equal(AloneReason.ContradictsTheCatalogue, queued.DecidedAlone);
        // Both are still said: the picture is shown, and the two values named.
        Assert.NotNull(queued.Picture);
        Assert.NotNull(queued.Contradicts);
    }

    /// <summary>Approving it replaces what the catalogue holds: never with others, whatever a screen sent.</summary>
    [Fact]
    public async Task A_claim_that_contradicts_the_catalogue_is_never_approved_together_with_others()
    {
        var claim = Propose("MATERIAL", "CARBON_STEEL");
        var catalogue = Holds(("MATERIAL", "STAINLESS_STEEL", "acero inoxidable"));

        var result = await Decide(claim, catalogue, inBulk: true);

        Assert.Equal(ReviewOutcome.ContradictsTheCatalogue, result.Outcome);
        Assert.Equal(ClaimStatus.Proposed, claim.Status);
        Assert.Equal(AuditOutcome.Denied, ((IAuditedResult)result).AuditOutcome);
    }

    [Fact]
    public async Task Opened_on_its_own_it_is_the_persons_to_approve_and_in_bulk_it_can_still_be_rejected()
    {
        var catalogue = Holds(("MATERIAL", "STAINLESS_STEEL", "acero inoxidable"));

        Assert.Equal(ReviewOutcome.Approved, (await Decide(Propose("MATERIAL", "CARBON_STEEL"), catalogue, inBulk: false)).Outcome);
        Assert.Equal(ReviewOutcome.Rejected, (await Decide(Propose("MATERIAL", "CARBON_STEEL"), catalogue, inBulk: true, ReviewDecision.Reject)).Outcome);
    }

    /// <summary>A claim that agrees with the catalogue goes with the others as before, and the catalogue is not asked for a rejection.</summary>
    [Fact]
    public async Task What_agrees_with_the_catalogue_is_approved_together_and_a_rejection_asks_nothing()
    {
        var catalogue = Holds(("MATERIAL", "CARBON_STEEL", "acero al carbono"));

        Assert.Equal(ReviewOutcome.Approved, (await Decide(Propose("MATERIAL", "CARBON_STEEL"), catalogue, inBulk: true)).Outcome);

        var silent = Holds();
        await Decide(Propose("MATERIAL", "CARBON_STEEL"), silent, inBulk: true, ReviewDecision.Reject);
        Assert.Empty(silent.Asked);
    }

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = name.ToLowerInvariant(), DisplayName = name };
    }
}
