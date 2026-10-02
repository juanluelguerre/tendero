using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

[Collection(nameof(FakeEntry))]
public sealed class ProposableCommandsTests
{
    [Fact]
    public void Finds_an_entry_by_its_stable_name()
    {
        var entry = new FakeEntry([], new ProposalScope());

        Assert.Same(entry, new ProposableCommands([entry]).Find("SetValue"));
        Assert.Null(new ProposableCommands([entry]).Find("DropTable"));
    }

    /// <summary>Two entries with one name would let a proposal run whichever DI
    /// listed last: refused when the registry is built, not when a row is approved.</summary>
    [Fact]
    public void Two_entries_with_one_name_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ProposableCommands([new FakeEntry([], new ProposalScope()), new FakeEntry([], new ProposalScope())]));
    }
}
