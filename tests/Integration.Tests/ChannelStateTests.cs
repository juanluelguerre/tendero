using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// What each channel has been told, against a real Postgres (phase 16, S3; ADR 0070):
/// a listing with its variants' ids in jsonb and found by one of them, the stock
/// last published, and the reconciliation's mark — each through its port.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChannelStateTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_channels_state_round_trips_and_a_listing_is_found_by_a_variants_sku()
    {
        var factory = await MigratedAsync("channels_state_round_trip");

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var listing = ChannelListing.For("shopify", "TAZ-001");
            listing.Published(
                "gid://shopify/Product/1",
                new Dictionary<string, string> { ["TAZ-001-BLU"] = "v11", ["TAZ-001-RED"] = "v12" },
                ChannelProductStatus.OnSale,
                At);
            var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "gid://shopify/Location/1");
            level.Set(3, At);

            scope.ServiceProvider.GetRequiredService<IChannelListings>().Add(listing);
            scope.ServiceProvider.GetRequiredService<IChannelStockLevels>().Add(level);
            scope.ServiceProvider.GetRequiredService<IChannelCursors>().Add(ChannelCursor.For("shopify", At));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var listings = scope.ServiceProvider.GetRequiredService<IChannelListings>();

            var read = await listings.FindAsync("shopify", "TAZ-001", Ct);
            Assert.NotNull(read);
            Assert.Equal(("gid://shopify/Product/1", ChannelProductStatus.OnSale), (read.ExternalProductId, read.Status));
            Assert.Equal("v12", read.VariantIdOf("TAZ-001-RED"));

            Assert.Equal("TAZ-001", (await listings.HoldingAsync("shopify", "TAZ-001-BLU", Ct))?.Code);
            Assert.Null(await listings.HoldingAsync("shopify", "TAZ-002-BLU", Ct));
            Assert.Null(await listings.HoldingAsync("another", "TAZ-001-BLU", Ct));

            var level = await scope.ServiceProvider.GetRequiredService<IChannelStockLevels>()
                .FindAsync("shopify", "TAZ-001-BLU", "gid://shopify/Location/1", Ct);
            Assert.Equal(3, level?.Expected);

            // Changed and saved through the same ports: the channel sold, and the mark moved.
            level!.WentStale(2, At.AddMinutes(1));
            var cursor = await scope.ServiceProvider.GetRequiredService<IChannelCursors>().FindAsync("shopify", Ct);
            cursor!.ReadUntil(At.AddMinutes(5));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var level = await scope.ServiceProvider.GetRequiredService<IChannelStockLevels>()
                .FindAsync("shopify", "TAZ-001-BLU", "gid://shopify/Location/1", Ct);
            var cursor = await scope.ServiceProvider.GetRequiredService<IChannelCursors>().FindAsync("shopify", Ct);

            Assert.Equal((2, At.AddMinutes(1)), (level?.Expected, level?.StaleSince));
            Assert.Equal(At.AddMinutes(5), cursor?.OrdersReadUntil);
        }
    }

    private async Task<TenderoDbContextFactory> MigratedAsync(string name)
    {
        postgres.SkipIfUnavailable();
        var factory = await postgres.CreateDatabaseAsync(name);
        await using var migrate = factory.Create();
        await migrate.Database.MigrateAsync(Ct);
        return factory;
    }

    private static ServiceProvider Services(TenderoDbContextFactory factory) =>
        new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
}
