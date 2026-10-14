using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// What a refusal hands the host to write (ADR 0059): its code always, and the
/// rest only when there is something to say. Written at phase 13b's close,
/// from what Stryker left alive: nothing held that a blank reason is no
/// reason, or that a detail in one language is not a detail.
/// </summary>
public sealed class RefusalSaidTests
{
    private static readonly Refusal Short = new("InsufficientStock", 409, "No queda suficiente.", "There is not enough left.");

    [Fact]
    public void A_refusal_always_says_its_code_and_nothing_it_was_not_given()
    {
        var said = Short.Said();

        Assert.Equal(["code"], said.Keys);
        Assert.Equal("InsufficientStock", said["code"]);
    }

    /// <summary>The domain's own sentence travels when there is one — and a blank one is not one.</summary>
    [Theory]
    [InlineData("PAN-26: 2 asked for, 1 available", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void A_reason_is_written_only_when_it_says_something(string? reason, bool written)
    {
        var said = Short.Said(reason: reason);

        Assert.Equal(written, said.ContainsKey("reason"));
        if (written)
            Assert.Equal(reason, said["reason"]);
    }

    /// <summary>
    /// A detail is said in both languages or not at all (invariant 6): one
    /// language alone would be answered to whoever asked in the other.
    /// </summary>
    [Fact]
    public void A_detail_needs_both_languages()
    {
        Assert.False(Short.Said(es: "Solo queda una.").ContainsKey(Refusal.DetailMember));
        Assert.False(Short.Said(en: "Only one left.").ContainsKey(Refusal.DetailMember));

        var both = Assert.IsType<LocalizedText>(Short.Said(es: "Solo queda una.", en: "Only one left.")[Refusal.DetailMember]);
        Assert.Equal(("Solo queda una.", "Only one left."), (both.In("es"), both.In("en")));
    }
}
