using System.Text.Json;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// A closed set in a body (ADR 0043), held to what <see cref="WireEnum{TEnum}"/>
/// holds a query string to: a member's name, and nothing that merely lands on one.
/// </summary>
public sealed class WireJsonTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { WireJson.Enums() } };

    [Fact]
    public void A_member_travels_by_its_name() =>
        Assert.Equal("\"Week\"", JsonSerializer.Serialize(DashboardPeriod.Week, Options));

    [Theory]
    [InlineData("\"Week\"", DashboardPeriod.Week)]
    [InlineData("\"month\"", DashboardPeriod.Month)]
    public void A_name_is_read_in_any_case(string json, DashboardPeriod expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<DashboardPeriod>(json, Options));

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("\"Day, Week\"")]
    [InlineData("\"year\"")]
    public void A_number_or_a_combination_is_refused(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DashboardPeriod>(json, Options));
}
