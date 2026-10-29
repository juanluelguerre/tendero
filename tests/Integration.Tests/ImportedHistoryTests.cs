using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Imported history against a real Postgres (phase 16, S2b, P16-18; ADR 0066): an
/// order born delivered keeps where it came from and its source's dates, a refund
/// born refunded keeps its amount and reference, and the same order imported twice
/// is refused by the idempotency key's unique index rather than stored twice.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportedHistoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset PlacedThen = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly Address Ana = Address.Create("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES");

    private static Order Imported(string externalId) =>
        Order.Imported(
            new OrderOrigin("shopify", externalId, "#1002"), OrderStatus.Delivered, CustomerId.New(),
            [new OrderLine(ProductId.New(), VariantId.New(), "SHIRT-M", "Camiseta", "M", null, new Money(20m, "EUR"), 2)],
            Ana, Ana, new OrderShipping("shopify:standard", "Envío", Money.Zero("EUR"), null),
            new OrderTotals(new Money(40m, "EUR"), Money.Zero("EUR"), Money.Zero("EUR"), new Money(6.94m, "EUR"), new Money(40m, "EUR")),
            PlacedThen, shippedAt: PlacedThen.AddDays(1));

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_imported_order_and_its_refund_round_trip_and_the_same_order_is_stored_once()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("imported_history");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var order = Imported("5002");
        var refund = ReturnRequest.Imported(
            order, [new ReturnLine(order.Lines[0].VariantId, "SHIRT-M", 1, ReturnReason.ChangedMind, null)], [],
            new Money(20m, "EUR"), "shopify:refund:7001", PlacedThen.AddDays(10));

        await using (var context = factory.Create())
        {
            context.Orders.Add(order);
            context.ReturnRequests.Add(refund);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = factory.Create())
        {
            var read = await context.Orders.SingleAsync(each => each.Id == order.Id, ct);
            Assert.Equal((OrderStatus.Delivered, new OrderOrigin("shopify", "5002", "#1002"), PlacedThen), (read.Status, read.ImportedFrom, read.CreatedAt));

            var returned = await context.ReturnRequests.SingleAsync(each => each.Id == refund.Id, ct);
            Assert.Equal((ReturnStatus.Refunded, new Money(20m, "EUR"), "shopify:refund:7001"), (returned.Status, returned.RefundAmount, returned.RefundReference));
        }

        await using (var context = factory.Create())
        {
            context.Orders.Add(Imported("5002"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(ct));
        }
    }
}
