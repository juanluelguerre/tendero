using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// A day inside a sentence the server writes for a reader (phase 13c): "1 de
/// diciembre de 2026", "1 December 2026". By hand, because the API runs with
/// InvariantGlobalization and no culture's month names are there to ask; the
/// day itself is the caller's, counted in the shop's zone (ADR 0046).
/// </summary>
public sealed class DayTextTests
{
    [Theory]
    [InlineData(2026, 12, 1, "es", "1 de diciembre de 2026")]
    [InlineData(2026, 12, 1, "en", "1 December 2026")]
    [InlineData(2027, 1, 15, "es", "15 de enero de 2027")]
    [InlineData(2027, 5, 31, "en", "31 May 2027")]
    public void A_day_is_written_as_its_language_writes_it(int year, int month, int day, string culture, string written) =>
        Assert.Equal(written, DayText.In(year, month, day, culture));

    [Fact]
    public void A_language_the_shop_does_not_speak_is_written_as_the_default() =>
        Assert.Equal(DayText.In(2026, 3, 9, "es"), DayText.In(2026, 3, 9, "fr"));
}
