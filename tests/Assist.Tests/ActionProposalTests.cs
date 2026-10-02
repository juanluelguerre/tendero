using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>A proposal's life is a table (invariant 5): every move it allows, and the ones it refuses.</summary>
public sealed class ActionProposalTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void A_new_proposal_waits_for_review_with_its_provenance()
    {
        var proposal = AProposal();

        Assert.Equal(ProposalStatus.Proposed, proposal.Status);
        Assert.Equal("lucia", proposal.Provenance.AskedBy);
        Assert.Equal(Clock.GetUtcNow(), proposal.ProposedAt);
        Assert.Null(proposal.DecidedBy);
    }

    [Fact]
    public void Approving_then_applying_records_who_and_when()
    {
        var proposal = AProposal();

        proposal.Approve("Lucía", Clock);
        proposal.RecordApplied();

        Assert.Equal(ProposalStatus.Applied, proposal.Status);
        Assert.Equal("Lucía", proposal.DecidedBy);
        Assert.Equal(Clock.GetUtcNow(), proposal.DecidedAt);
        Assert.Null(proposal.Result);
    }

    [Fact]
    public void A_failure_keeps_its_reason()
    {
        var proposal = AProposal();
        proposal.Approve("Lucía", Clock);

        proposal.RecordFailed("Archived");

        Assert.Equal((ProposalStatus.Failed, "Archived"), (proposal.Status, proposal.Result));
    }

    [Fact]
    public void A_rejection_keeps_the_reason_a_prompt_change_is_judged_against()
    {
        var proposal = AProposal();

        proposal.Reject("Lucía", "Too literal", Clock);

        Assert.Equal((ProposalStatus.Rejected, "Too literal", "Lucía"), (proposal.Status, proposal.Result, proposal.DecidedBy));
    }

    [Fact]
    public void A_stale_proposal_says_who_found_it_stale()
    {
        var proposal = AProposal();

        proposal.MarkStale("Lucía", Clock);

        Assert.Equal((ProposalStatus.Stale, "Lucía"), (proposal.Status, proposal.DecidedBy));
    }

    /// <summary>A reason comes from whatever the command threw or returned; the
    /// column holds a thousand characters, and a longer one must not take the row's
    /// result down with it (final review, I-1).</summary>
    [Fact]
    public void A_failure_reason_longer_than_its_column_is_cut_to_it()
    {
        var proposal = AProposal();
        proposal.Approve("Lucía", Clock);

        proposal.RecordFailed(new string('x', 5000));

        Assert.Equal(ActionProposal.ResultMaxLength, proposal.Result!.Length);
    }

    [Fact]
    public void A_failure_without_a_reason_still_says_it_failed()
    {
        var proposal = AProposal();
        proposal.Approve("Lucía", Clock);

        proposal.RecordFailed("  ");

        Assert.Equal(ProposalStatus.Failed, proposal.Status);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Result));
    }

    /// <summary>An approval moments old may still be running in another request:
    /// only one old enough to have died can be finished again (final review, I-2).</summary>
    [Fact]
    public void A_just_approved_proposal_is_not_retried_until_it_could_have_died()
    {
        var clock = new TestClock();
        var proposal = ActionProposal.Propose("SetProductTranslation", "{}", "MOKA-1", null, "because", Provenance(), clock);
        proposal.Approve("Lucía", clock);

        Assert.False(proposal.MayRetry(clock));
        clock.Advance(ActionProposal.StillRunning + TimeSpan.FromSeconds(1));
        Assert.True(proposal.MayRetry(clock));

        proposal.Retry("Marta", clock);
        Assert.Equal(("Marta", clock.GetUtcNow()), (proposal.DecidedBy, proposal.DecidedAt));
        Assert.False(proposal.MayRetry(clock));
    }

    [Fact]
    public void Nothing_is_applied_without_an_approval()
    {
        Assert.Throws<InvalidOperationException>(() => AProposal().RecordApplied());
    }

    [Fact]
    public void A_decided_proposal_is_not_decided_again()
    {
        var rejected = AProposal();
        rejected.Reject("Lucía", "No", Clock);

        Assert.Throws<InvalidOperationException>(() => rejected.Approve("Lucía", Clock));
    }

    [Theory]
    [InlineData("", "{}", "MOKA-1", "reason")]
    [InlineData("SetProductTranslation", " ", "MOKA-1", "reason")]
    [InlineData("SetProductTranslation", "{}", "", "reason")]
    [InlineData("SetProductTranslation", "{}", "MOKA-1", " ")]
    public void A_proposal_has_a_command_a_payload_a_subject_and_a_reason(string command, string payload, string subject, string reason)
    {
        Assert.ThrowsAny<ArgumentException>(() => ActionProposal.Propose(command, payload, subject, null, reason, Provenance(), Clock));
    }

    private static ActionProposal AProposal() => ActionProposal.Propose(
        "SetProductTranslation",
        """{"productId":"00000000-0000-0000-0000-000000000001","culture":"en","name":"Moka pot"}""",
        "MOKA-1",
        """{"name":null,"description":null}""",
        "It has no English name.",
        Provenance(),
        Clock);

    private static ProposalProvenance Provenance() => new("local", "qwen3.5-4b", "steward-v1", "conv-1", "trace-1", "lucia");
}
