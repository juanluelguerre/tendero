using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The rule every closed set in a query string is read by (ADR 0043): a member's
/// name, in any case, and nothing else. It was written out by hand for the
/// dashboard's period before it was shared, and what that copy's test proved is
/// proved here once, for every endpoint that binds one.
///
/// The period is the set on purpose: Day is 0, so a number, a position and a
/// combination can all land on a member that exists, which is exactly when a
/// weaker rule would let them through.
/// </summary>
public sealed class WireEnumTests
{
    [Theory]
    [InlineData("Day", DashboardPeriod.Day)]
    [InlineData("week", DashboardPeriod.Week)]
    [InlineData("MONTH", DashboardPeriod.Month)]
    [InlineData("mOnTh", DashboardPeriod.Month)]
    public void A_member_is_read_by_its_name_in_any_case(string text, DashboardPeriod expected)
    {
        Assert.True(WireEnum<DashboardPeriod>.TryParse(text, out var wire));
        Assert.Equal(expected, wire.Value);
    }

    /// <summary>
    /// "1" is Week by position and "Day, Week" is Week by bitwise OR (0 | 1):
    /// both would come back as a real member if the rule only asked whether the
    /// value is defined.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("year")]
    [InlineData("Day, Week")]
    [InlineData("Week, Month")]
    [InlineData(" Week")]
    [InlineData("Week ")]
    [InlineData("")]
    [InlineData(null)]
    public void A_number_a_combination_or_a_name_the_set_does_not_have_is_refused(string? text) =>
        Assert.False(WireEnum<DashboardPeriod>.TryParse(text, out _));
}
