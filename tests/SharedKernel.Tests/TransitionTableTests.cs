using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The guard four aggregates share. Each of them still walks its own matrix in
/// its own tests; this covers the two things the shared type adds — one wording
/// for a refusal, and a named error for a state nobody declared.
/// </summary>
public sealed class TransitionTableTests
{
    public enum Light { Red, Amber, Green, Off }

    private static readonly TransitionTable<Light> Table = new()
    {
        [Light.Red] = [Light.Green],
        [Light.Green] = [Light.Amber],
        [Light.Amber] = [Light.Red],
        [Light.Off] = []
    };

    [Theory]
    [InlineData(Light.Red, Light.Green, true)]
    [InlineData(Light.Red, Light.Amber, false)]
    [InlineData(Light.Off, Light.Red, false)]
    public void It_allows_exactly_the_declared_edges(Light from, Light to, bool allowed) =>
        Assert.Equal(allowed, Table.Allows(from, to));

    [Fact]
    public void A_terminal_state_has_a_row_and_no_targets() =>
        Assert.Empty(Table[Light.Off]);

    [Fact]
    public void The_refusal_names_the_edge_and_the_subject()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Table.EnsureAllowed(Light.Green, Light.Red, "light 7"));

        Assert.Equal("Illegal transition Green -> Red for light 7.", exception.Message);
    }

    [Fact]
    public void An_allowed_edge_passes_silently() =>
        Table.EnsureAllowed(Light.Red, Light.Green, "light 7");

    [Fact]
    public void A_state_nobody_declared_is_named_rather_than_a_missing_key()
    {
        var partial = new TransitionTable<Light> { [Light.Red] = [Light.Green] };

        var exception = Assert.Throws<InvalidOperationException>(() => partial.Allows(Light.Amber, Light.Red));

        Assert.Contains("Light.Amber", exception.Message);
    }

    /// <summary>
    /// Where an aggregate may be born is declared beside its edges (ADR 0066): a
    /// light is switched on at red, or found already off, and nowhere else.
    /// </summary>
    [Theory]
    [InlineData(Light.Red, true)]
    [InlineData(Light.Off, true)]
    [InlineData(Light.Green, false)]
    public void A_table_declares_where_its_aggregate_may_be_born(Light state, bool entry)
    {
        var table = new TransitionTable<Light>
        {
            Entries = [Light.Red, Light.Off],
            [Light.Red] = [Light.Green],
            [Light.Green] = [Light.Amber],
            [Light.Amber] = [Light.Red],
            [Light.Off] = [],
        };

        Assert.Equal(entry, table.IsEntry(state));
        if (entry)
            table.EnsureEntry(state, "light 7");
        else
            Assert.Equal("Illegal entry Green for light 7.", Assert.Throws<InvalidOperationException>(() => table.EnsureEntry(state, "light 7")).Message);
    }

    /// <summary>A table that declares no entries refuses none: the idiom predates them.</summary>
    [Fact]
    public void A_table_without_entries_refuses_none() =>
        Table.EnsureEntry(Light.Green, "light 7");
}
