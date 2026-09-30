using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The history port takes an instant in any offset (ADR 0046): the series asks it
/// with the start of the shop's day, +02:00 in a Madrid summer, and Npgsql writes
/// only UTC to a timestamptz. Found on 2026-09-30, when the sales series answered
/// 500 with real orders behind it (R-9, D-7).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ShopHistoryInstantsTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Earlier_buyers_are_asked_with_an_instant_in_the_shops_offset()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await Shop.OpenAsync(postgres, nameof(Earlier_buyers_are_asked_with_an_instant_in_the_shops_offset));
        var startOfTheShopsDay = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.FromHours(2));

        var earlier = await shop.ReadAsync<IShopHistory, IReadOnlySet<CustomerId>>(history =>
            history.CustomersWhoOrderedBeforeAsync([CustomerId.New()], startOfTheShopsDay, ct));

        Assert.Empty(earlier);
    }
}
