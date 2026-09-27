using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// What a mandate covers (P11-5): one total, in one currency, until it ends — and a
/// smaller total is not "within" a closed mandate, it is another purchase.
/// </summary>
public sealed class MandateScopeTests
{
    private static readonly DateTimeOffset Ends = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);

    private static MandateScope Scope(DateTimeOffset? ends) =>
        new("mandate.checkout.1", "platform-key", "hash", new Money(148.85m, "EUR"), ends);

    [Theory]
    [InlineData("148.85", "EUR", true)]
    [InlineData("148.85", "eur", true)]
    [InlineData("148.86", "EUR", false)]
    [InlineData("148.84", "EUR", false)]
    [InlineData("148.85", "USD", false)]
    // Amounts as text: an attribute's 148.85 is a double, and 148.8499… is exactly
    // the kind of total a closed mandate must not cover.
    public void It_covers_the_approved_total_and_nothing_else(string amount, string currency, bool covered) =>
        Assert.Equal(covered, Scope(Ends).Covers(new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency), Ends.AddMinutes(-1)));

    [Fact]
    public void It_ends_when_it_says_and_one_without_an_end_does_not()
    {
        var total = new Money(148.85m, "EUR");

        Assert.True(Scope(Ends).Covers(total, Ends.AddTicks(-1)));
        Assert.False(Scope(Ends).Covers(total, Ends));
        Assert.True(Scope(null).Covers(total, Ends.AddYears(1)));
    }
}
