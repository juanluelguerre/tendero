using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The facts the dashboard reads survive a round trip through Postgres (R-9,
/// D-7): the channel the order came through, each line's category taken from
/// the catalogue, and the moments it was confirmed and shipped.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderSnapshotTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_order_keeps_its_channel_its_lines_category_and_its_moments()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(
            postgres, nameof(An_order_keeps_its_channel_its_lines_category_and_its_moments),
            actor: new FixedActor(new Actor(ActorKinds.Agent, Surfaces.Mcp)));

        await shop.StockAsync(Sku, 5, ct);
        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var chosen = (await shop.ShippingOptionsAsync(cart.Token, ct)).Options[0];
        var quote = await shop.QuoteAsync(cart.Token, chosen.Amount, ct);
        var placed = await shop.PlaceOrderAsync(cart.Token, chosen.Code, quote.InputHash, ct);
        Assert.Equal(PlaceOrderOutcome.Placed, placed.Outcome);

        await shop.MoveAsync(placed.Order!.Id, order =>
        {
            order.Confirm(TimeProvider.System);
            order.Ship(TimeProvider.System);
        }, ct);

        var stored = await shop.OrderAsync(placed.Order.Id, ct);

        Assert.Equal(
            (OrderChannel.Mcp, "COOKWARE", "HOME/KITCHEN/COOKWARE", true, true),
            (stored!.Channel, stored.Lines[0].CategoryCode, stored.Lines[0].CategoryPath, stored.ConfirmedAt is not null, stored.ShippedAt is not null));
    }

    private sealed class FixedActor(Actor actor) : IActorAccessor
    {
        public Actor Current => actor;
    }
}
