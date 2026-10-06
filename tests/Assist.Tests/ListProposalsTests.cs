using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.ListProposals;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// The Proposals list (S-4): what Steward proposed, by what became of it, counted
/// on the server; and the proposals a turn returned, by their ids, for its card
/// and its review — each as a person reads it, in their language.
/// </summary>
public sealed class ListProposalsTests
{
    private static readonly TestClock Clock = new();

    private static readonly ProposalProvenance Provenance = new("local", "qwen", "steward-1", "conv-1", "trace-1", "Lucía");

    private static ActionProposal Proposal(string subject, string? display = null) =>
        ActionProposal.Propose(FakeEntry.CommandName, "{}", subject, "{}", "Because.", Provenance, Clock, display);

    /// <summary>The picture a proposal is about travels with its view, as an id the backoffice asks the shop's image endpoint for.</summary>
    [Fact]
    public async Task A_proposal_about_a_picture_says_which_and_one_about_words_says_none()
    {
        var (handler, proposals) = Handler();
        var words = FakeEntry.Display("Taza", "(empty)", "Mug");
        proposals.Add(Proposal("MUG-1", (words with { Image = "9f2c" }).ToJson()));
        proposals.Add(Proposal("MUG-2", words.ToJson()));

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.ToReview, null, 1, 20, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(["9f2c", null], page.Items.OrderBy(item => item.Subject).Select(item => item.Image));
    }

    private static (ListProposalsHandler Handler, InMemoryProposals Proposals) Handler()
    {
        var proposals = new InMemoryProposals();
        var registry = new ProposableCommands([new FakeEntry([], new ProposalScope())]);
        return (new ListProposalsHandler(proposals, registry), proposals);
    }

    /// <summary>One of each fate: waiting, approved with no result, applied, failed,
    /// rejected, stale.</summary>
    private static InMemoryProposals EveryFate(InMemoryProposals proposals)
    {
        var waiting = Proposal("A");
        var unknown = Proposal("B");
        unknown.Approve("lucia", Clock);
        var applied = Proposal("C");
        applied.Approve("lucia", Clock);
        applied.RecordApplied();
        var failed = Proposal("D");
        failed.Approve("lucia", Clock);
        failed.RecordFailed("Archived");
        var rejected = Proposal("E");
        rejected.Reject("lucia", "Not now.", Clock);
        var stale = Proposal("F");
        stale.MarkStale("lucia", Clock);
        foreach (var proposal in new[] { waiting, unknown, applied, failed, rejected, stale })
            proposals.Add(proposal);
        return proposals;
    }

    [Fact]
    public async Task Each_filter_is_counted_over_every_proposal_whichever_is_shown()
    {
        var (handler, proposals) = Handler();
        EveryFate(proposals);

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.Applied, null, 1, 20, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [(ProposalFilter.ToReview, 2), (ProposalFilter.Applied, 1), (ProposalFilter.Rejected, 1), (ProposalFilter.Failed, 2), (ProposalFilter.All, 6)],
            page.Counts.Select(count => (count.Filter, count.Count)));
        Assert.Equal("C", Assert.Single(page.Items).Subject);
    }

    /// <summary>An approval whose process died is still somebody's work: it waits
    /// under To review, where it can be finished.</summary>
    [Fact]
    public async Task To_review_holds_the_waiting_and_the_approved_with_no_result()
    {
        var (handler, proposals) = Handler();
        EveryFate(proposals);

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.ToReview, null, 1, 20, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], page.Items.Select(item => item.Subject).Order());
    }

    [Fact]
    public async Task Failed_holds_the_stale_too_since_neither_changed_the_shop()
    {
        var (handler, proposals) = Handler();
        EveryFate(proposals);

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.Failed, null, 1, 20, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(["D", "F"], page.Items.Select(item => item.Subject).Order());
        Assert.Equal("Archived", page.Items.Single(item => item.Subject == "D").Result);
    }

    /// <summary>A turn's card asks for its own proposals: in the order the turn
    /// made them, whatever became of each since.</summary>
    [Fact]
    public async Task Ids_are_answered_in_the_order_asked_whatever_their_filter()
    {
        var (handler, proposals) = Handler();
        EveryFate(proposals);
        var ids = new[] { proposals.All[4].Id, proposals.All[0].Id };

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.ToReview, ids, 1, 20, "en"), TestContext.Current.CancellationToken);

        Assert.Equal([("E", ProposalStatus.Rejected), ("A", ProposalStatus.Proposed)], page.Items.Select(item => (item.Subject, item.Status)));
    }

    [Fact]
    public async Task A_proposal_reads_in_the_persons_language_with_its_provenance()
    {
        var (handler, proposals) = Handler();
        proposals.Add(Proposal("MUG-1", FakeEntry.Display("Taza", "(empty)", "Mug").ToJson()));

        var item = Assert.Single((await handler.HandleAsync(
            new ListProposalsQuery(ProposalFilter.All, null, 1, 20, "es"), TestContext.Current.CancellationToken)).Items);

        Assert.Equal(("Poner un valor", "Taza", "Value", "Because."), (item.Action, item.Title, item.Topic, item.Reason));
        Assert.Equal(new ProposalChangeView("Value", "(empty)", "Mug"), Assert.Single(item.Changes));
        Assert.Equal(("qwen", "steward-1", "Lucía", "trace-1"), (item.Provenance.Model, item.Provenance.PromptVersion, item.Provenance.AskedBy, item.Provenance.TraceId));
    }

    /// <summary>Made before S-4, or by a command no longer registered: shown by its
    /// code and its command, never an error in the list.</summary>
    [Fact]
    public async Task A_proposal_with_no_display_and_no_entry_still_reads()
    {
        var (handler, proposals) = Handler();
        proposals.Add(ActionProposal.Propose("Retired", "{}", "MUG-9", null, "Because.", Provenance, Clock));

        var item = Assert.Single((await handler.HandleAsync(
            new ListProposalsQuery(ProposalFilter.All, null, 1, 20, "en"), TestContext.Current.CancellationToken)).Items);

        Assert.Equal(("Retired", "MUG-9", "Retired"), (item.Action, item.Title, item.Topic));
        Assert.Empty(item.Changes);
    }

    [Fact]
    public async Task The_newest_come_first_a_page_at_a_time()
    {
        var (handler, proposals) = Handler();
        foreach (var subject in new[] { "A", "B", "C" })
        {
            proposals.Add(Proposal(subject));
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page = await handler.HandleAsync(new ListProposalsQuery(ProposalFilter.All, null, 2, 2, "en"), TestContext.Current.CancellationToken);

        Assert.Equal((3, "A"), (page.Total, Assert.Single(page.Items).Subject));
    }

    [Theory]
    [InlineData(0, 20, 0, "en", false)]
    [InlineData(1, 101, 0, "en", false)]
    [InlineData(1, 20, 101, "en", false)]
    [InlineData(1, 20, 0, "fr", false)]
    [InlineData(1, 20, 2, "es", true)]
    public void A_page_a_batch_of_ids_and_a_language_the_shop_speaks(int page, int pageSize, int ids, string culture, bool valid) =>
        Assert.Equal(valid, new ListProposalsValidator().Validate(new ListProposalsQuery(
            ProposalFilter.All, [.. Enumerable.Range(0, ids).Select(_ => ProposalId.New(Clock.GetUtcNow()))], page, pageSize, culture)).IsValid);
}
