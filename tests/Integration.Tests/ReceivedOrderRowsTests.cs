using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// An order a sales channel took, against a real Postgres (phase 16, S3; ADR 0070): where
/// it came from in jsonb, found again by its channel key, its payment already captured,
/// and the oversold reason in a column of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceivedOrderRowsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_received_order_round_trips_with_its_origin_its_payment_and_its_oversold_reason()
    {
        postgres.SkipIfUnavailable();
        var factory = await postgres.CreateDatabaseAsync("ordering_received_order_rows");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(Ct);

        var clock = TimeProvider.System;
        var address = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");
        var origin = new OrderOrigin("shopify", "gid://shopify/Order/7001", "#1042");

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var order = Order.Received(clock, origin, OrderChannel.Shopify, CustomerId.New(),
                [new OrderLine(ProductId.New(), VariantId.New(), "TAZ-001-BLU", "Taza de gres", "Azul", null, new Money(12.5m, "EUR"), 2)],
                address, address, new OrderShipping("shopify:shipping", "Envío estándar", new Money(4.95m, "EUR"), null),
                new OrderTotals(new Money(25m, "EUR"), Money.Zero("EUR"), new Money(4.95m, "EUR"), new Money(5.20m, "EUR"), new Money(29.95m, "EUR")),
                clock.GetUtcNow());
            order.MarkOversold(clock, "TAZ-001-BLU: 2 asked for, 1 available.");

            scope.ServiceProvider.GetRequiredService<IOrderRepository>().Add(order);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using (var services = Services(factory))
        await using (var scope = services.CreateAsyncScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .FindByIdempotencyKeyAsync(Order.ReceivedKey("shopify", "gid://shopify/Order/7001"), Ct);

            Assert.NotNull(read);
            Assert.Equal(origin, read.ReceivedFrom);
            Assert.Null(read.ImportedFrom);
            Assert.Equal((OrderStatus.PaymentAuthorized, OrderChannel.Shopify), (read.Status, read.Channel));
            Assert.Equal("gid://shopify/Order/7001", read.Payment?.CaptureId);
            Assert.Equal("TAZ-001-BLU: 2 asked for, 1 available.", read.Oversold);
        }
    }

    private static ServiceProvider Services(TenderoDbContextFactory factory) =>
        new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
}
