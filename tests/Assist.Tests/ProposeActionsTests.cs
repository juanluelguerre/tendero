using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>Code builds every proposal: what the registry refuses goes back as an
/// error and is not stored.</summary>
[Collection(nameof(FakeEntry))]
public sealed class ProposeActionsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_valid_action_is_stored_with_its_snapshot_and_whoever_asked()
    {
        var (handler, proposals, unitOfWork) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(Command(("MUG-1", "new")), TestContext.Current.CancellationToken);

        var stored = Assert.Single(proposals.All);
        Assert.Equal(stored.Id, Assert.Single(result.Rows).Id);
        Assert.Equal(("MUG-1", """{"value":"old","subject":"MUG-1"}""", "Lucía"), (stored.Subject, stored.Before, stored.Provenance.AskedBy));
        Assert.Equal("qwen3.5-4b", stored.Provenance.Model);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>What the card, the review and the list show is written when it is
    /// proposed, by the context that owns the command: Assist reads no other context.</summary>
    [Fact]
    public async Task A_stored_proposal_keeps_what_a_person_reads_of_it()
    {
        var (handler, proposals, _) = Handler(new() { ["MUG-1"] = "old" });

        await handler.HandleAsync(Command(("MUG-1", "new")), TestContext.Current.CancellationToken);

        var display = ProposalDisplay.FromJson(Assert.Single(proposals.All).Display)!;
        Assert.Equal(("MUG-1", "old", "new"), (display.Title.In("en"), display.Changes[0].Now, display.Changes[0].Proposed));
    }

    [Fact]
    public async Task An_invalid_action_comes_back_as_an_error_and_is_not_stored()
    {
        var (handler, proposals, _) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(Command(("MUG-1", "new"), ("GONE", "x"), ("MUG-1", " ")), TestContext.Current.CancellationToken);

        Assert.Single(proposals.All);
        Assert.Equal([0], result.Rows.Where(row => row.Id is not null).Select(row => row.Index));
        Assert.Equal("No GONE.", Assert.Single(result.Rows[1].Errors));
        Assert.Equal("Value must not be blank.", Assert.Single(result.Rows[2].Errors));
    }

    [Fact]
    public async Task An_action_no_entry_is_named_for_is_refused()
    {
        var (handler, proposals, _) = Handler(new() { ["MUG-1"] = "old" });

        var result = await handler.HandleAsync(new ProposeActionsCommand(
            [new ProposedAction("ApproveProposals", FakeEntry.Payload("MUG-1", "x"), "because")], Source()), TestContext.Current.CancellationToken);

        Assert.Empty(proposals.All);
        Assert.Equal("No proposable command is named ApproveProposals.", Assert.Single(Assert.Single(result.Rows).Errors));
    }

    [Fact]
    public void The_validator_bounds_a_batch_and_wants_a_reason()
    {
        var validator = new ProposeActionsValidator();

        Assert.False(validator.Validate(new ProposeActionsCommand([], Source())).IsValid);
        Assert.False(validator.Validate(new ProposeActionsCommand(
            [.. Enumerable.Repeat(new ProposedAction("SetValue", FakeEntry.Payload("a", "b"), "r"), ProposalLimits.MaxBatch + 1)], Source())).IsValid);
        Assert.False(validator.Validate(new ProposeActionsCommand(
            [new ProposedAction("SetValue", FakeEntry.Payload("a", "b"), " ")], Source())).IsValid);
    }

    /// <summary>A runaway model could fill a jsonb row: a payload and its source
    /// strings are bounded like everything else a proposal stores (M-8).</summary>
    [Fact]
    public void The_validator_bounds_a_payload_and_where_it_came_from()
    {
        var validator = new ProposeActionsValidator();
        var huge = FakeEntry.Payload("a", new string('x', ProposeActionsValidator.PayloadMaxLength));

        Assert.False(validator.Validate(new ProposeActionsCommand([new ProposedAction("SetValue", huge, "r")], Source())).IsValid);
        Assert.False(validator.Validate(new ProposeActionsCommand(
            [new ProposedAction("SetValue", FakeEntry.Payload("a", "b"), "r")], Source() with { Model = new string('m', 201) })).IsValid);
    }

    private static ProposeActionsCommand Command(params (string Subject, string Value)[] actions) => new(
        [.. actions.Select(action => new ProposedAction(FakeEntry.CommandName, FakeEntry.Payload(action.Subject, action.Value), "It has no English name."))],
        Source());

    private static ProposalSource Source() => new("local", "qwen3.5-4b", "steward-v1", "conv-1");

    private static (ProposeActionsHandler, InMemoryProposals, CountingUnitOfWork) Handler(Dictionary<string, string> world)
    {
        var proposals = new InMemoryProposals();
        var unitOfWork = new CountingUnitOfWork();
        var handler = new ProposeActionsHandler(
            new ProposableCommands([new FakeEntry(world, new ProposalScope())]), proposals, unitOfWork, new FixedPrincipal("Lucía"), Clock);
        return (handler, proposals, unitOfWork);
    }
}
