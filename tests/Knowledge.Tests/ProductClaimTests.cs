using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The claim's life (ADR 0018): proposed with evidence, approved or rejected by a
/// person, superseded when its text changes — and nothing else.
/// </summary>
public sealed class ProductClaimTests
{
    private static readonly TestClock Clock = new();
    private static readonly ProductId Pan = ProductId.New();

    private static readonly SourceDocument Description = SourceDocument.Describe(
        Pan, "es", "Sartén de hierro fundido precurada, apta para inducción, horno y fuego directo.", Clock);

    private static Evidence Quoted(string quote, double support = 0.9) =>
        new(Description.Id, Description.Locate(quote)!, support);

    private static ProductClaim Induction(params Evidence[] evidence) => ProductClaim.Propose(
        Pan,
        LocalizedText.From("es", "Es apta para inducción.").With("en", "It works on induction."),
        "INDUCTION", "true",
        evidence.Length > 0 ? evidence : [Quoted("apta para inducción")],
        new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e", "claims-2026-09-26", "run-1", Clock.GetUtcNow()),
        Clock);

    [Fact]
    public void A_proposed_claim_is_waiting_for_a_person_and_says_so()
    {
        var claim = Induction();

        Assert.Equal(ClaimStatus.Proposed, claim.Status);
        Assert.Null(claim.ReviewedBy);
        Assert.IsType<ClaimProposed>(Assert.Single(claim.DomainEvents));
    }

    [Fact]
    public void Approval_names_the_person_and_carries_the_values_to_apply()
    {
        var claim = Induction();

        claim.Approve("shopkeeper@tendero", Clock);

        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal("shopkeeper@tendero", claim.ReviewedBy);
        var approved = Assert.IsType<ClaimApproved>(claim.DomainEvents[^1]);
        Assert.Equal(("INDUCTION", "true", Pan), (approved.AttributeCode, approved.Value, approved.ProductId));
    }

    [Fact]
    public void A_claim_shows_its_weakest_evidence()
    {
        var claim = Induction(Quoted("apta para inducción", 0.97), Quoted("hierro fundido", 0.31));

        Assert.Equal(0.31, claim.Support);
    }

    [Fact]
    public void Superseding_an_approved_claim_says_what_it_had_applied()
    {
        var claim = Induction();
        claim.Approve("shopkeeper@tendero", Clock);

        claim.Supersede(Clock);

        Assert.True(Assert.IsType<ClaimSuperseded>(claim.DomainEvents[^1]).WasApproved);
    }

    /// <summary>The whole matrix, walked: every listed edge succeeds, every other one throws.</summary>
    [Theory]
    [InlineData(ClaimStatus.Proposed, ClaimStatus.Approved, true)]
    [InlineData(ClaimStatus.Proposed, ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.Proposed, ClaimStatus.Superseded, true)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Superseded, true)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Rejected, false)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Superseded, false)]
    [InlineData(ClaimStatus.Superseded, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Superseded, ClaimStatus.Superseded, false)]
    public void Only_the_listed_transitions_are_allowed(ClaimStatus from, ClaimStatus to, bool allowed)
    {
        var claim = Induction();
        Move(claim, from);

        var attempt = Record.Exception(() => Move(claim, to));

        if (allowed)
            Assert.Null(attempt);
        else
            Assert.IsType<InvalidOperationException>(attempt);
    }

    [Fact]
    public void A_claim_needs_a_quote_a_probability_and_its_value_with_its_attribute()
    {
        var provenance = new Provenance("rules", null, null, null, "run-1", Clock.GetUtcNow());
        var statement = LocalizedText.From("es", "x");

        Assert.Throws<ArgumentException>(() => ProductClaim.Propose(Pan, statement, null, null, [], provenance, Clock));
        Assert.Throws<ArgumentException>(() => ProductClaim.Propose(Pan, statement, null, null, [Quoted("horno", 1.4)], provenance, Clock));
        Assert.Throws<ArgumentException>(() => ProductClaim.Propose(Pan, statement, "INDUCTION", null, [Quoted("horno")], provenance, Clock));
    }

    /// <summary>
    /// Catalog's answer is recorded on an approved, typed claim, and is one of the
    /// two: the value as held, or the refusal. Anything else is a caller's bug.
    /// </summary>
    [Fact]
    public void Only_an_approved_typed_claim_records_what_catalog_answered_and_only_one_answer()
    {
        var proposed = Induction();
        Assert.Throws<InvalidOperationException>(() => proposed.RecordApplication("true", null));

        var untyped = ProductClaim.Propose(
            Pan, LocalizedText.From("es", "Va al horno."), null, null, [Quoted("horno")],
            new Provenance("rules", null, null, null, "run-1", Clock.GetUtcNow()), Clock);
        untyped.Approve("shopkeeper@tendero", Clock);
        Assert.Throws<InvalidOperationException>(() => untyped.RecordApplication("true", null));

        var approved = Induction();
        approved.Approve("shopkeeper@tendero", Clock);
        Assert.Throws<ArgumentException>(() => approved.RecordApplication(null, null));
        Assert.Throws<ArgumentException>(() => approved.RecordApplication("true", "value_rejected"));

        approved.RecordApplication(null, "value_rejected");
        Assert.Equal((null, "value_rejected"), (approved.AppliedAs, approved.ApplicationRefused));
        approved.RecordApplication("true", null);
        Assert.Equal(("true", (string?)null), (approved.AppliedAs, approved.ApplicationRefused));
    }

    private static void Move(ProductClaim claim, ClaimStatus to)
    {
        switch (to)
        {
            case ClaimStatus.Approved: claim.Approve("shopkeeper@tendero", Clock); break;
            case ClaimStatus.Rejected: claim.Reject("shopkeeper@tendero", Clock); break;
            case ClaimStatus.Superseded: claim.Supersede(Clock); break;
            case ClaimStatus.Proposed: break;
        }
    }
}
