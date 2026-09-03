using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.StockSaga;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The saga, end to end, over a real Postgres and the real outbox.
///
/// The unit tests call each handler directly, which proves each arm is right and
/// proves nothing about the claim the phase is actually making: that the outbox
/// **is** the process manager. This is where that gets checked — an order is
/// placed, nobody calls inventory, the drain does, and stock moves.
///
/// It also checks the part that only a real database can answer: that the
/// <c>OrderPlaced</c> event and the order itself were written in one transaction,
/// that the unique index on <c>order_id</c> makes a redelivery a no-op rather
/// than a second hold, and that a refusal is a row somebody can read.
///
/// Elasticsearch takes no part. The projection handlers are not registered here;
/// what is under test is the process, and the SearchEval gate already covers the
/// index against a real engine.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StockSagaDrainTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Placing_an_order_holds_its_stock_without_anybody_calling_inventory()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await SagaScope.CreateAsync(postgres, nameof(
            Placing_an_order_holds_its_stock_without_anybody_calling_inventory));

        await scope.ReceiveAsync("SHOES", "MAD", 4, ct);
        var order = await scope.PlaceAsync([("SHOES", 3)], ct);

        // Nothing has touched inventory yet: placing an order writes an order and
        // an outbox row, and that is all it does.
        Assert.Equal(0, await scope.ReservedAsync("SHOES", "MAD", ct));

        await scope.DrainAsync(ct);

        Assert.Equal(3, await scope.ReservedAsync("SHOES", "MAD", ct));
        Assert.Equal(1, await scope.AvailableAsync("SHOES", ct));

        var reservation = await scope.ReservationAsync(order.Id, ct);
        Assert.Equal(ReservationStatus.Held, reservation!.Status);
    }

    /// <summary>
    /// The board's demo, and the row that makes it worth looking at: both
    /// warehouses at zero, the order cancels itself, and the reservation is there
    /// in <c>Released</c> saying exactly why.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_order_that_cannot_be_filled_cancels_itself_and_leaves_the_reason_behind()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await SagaScope.CreateAsync(postgres, nameof(
            An_order_that_cannot_be_filled_cancels_itself_and_leaves_the_reason_behind));

        await scope.ReceiveAsync("PANS", "MAD", 1, ct);
        var order = await scope.PlaceAsync([("PANS", 2)], ct);

        await scope.DrainAsync(ct);

        var cancelled = await scope.OrderAsync(order.Id, ct);
        Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);

        var reservation = await scope.ReservationAsync(order.Id, ct);
        Assert.Equal(ReservationStatus.Released, reservation!.Status);
        Assert.Equal("PANS: 2 asked for, 1 available.", reservation.Reason);

        // Nothing was held on the way to refusing.
        Assert.Equal(0, await scope.ReservedAsync("PANS", "MAD", ct));
    }

    /// <summary>
    /// Compensation, all the way round: the cancellation the saga itself caused
    /// raises <c>OrderCancelled</c>, which the release arm picks up on the NEXT
    /// drain. Two passes, because that is how many the outbox actually takes —
    /// and a test that drained once would have looked green while the release
    /// never ran.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancelling_an_order_that_held_stock_gives_it_back_on_the_next_drain()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await SagaScope.CreateAsync(postgres, nameof(
            Cancelling_an_order_that_held_stock_gives_it_back_on_the_next_drain));

        await scope.ReceiveAsync("SHOES", "MAD", 5, ct);
        var order = await scope.PlaceAsync([("SHOES", 2)], ct);

        await scope.DrainAsync(ct);
        Assert.Equal(2, await scope.ReservedAsync("SHOES", "MAD", ct));

        await scope.CancelAsync(order.Id, "The customer changed their mind.", ct);
        await scope.DrainAsync(ct);

        Assert.Equal(0, await scope.ReservedAsync("SHOES", "MAD", ct));
        Assert.Equal(5, await scope.AvailableAsync("SHOES", ct));

        var reservation = await scope.ReservationAsync(order.Id, ct);
        Assert.Equal(ReservationStatus.Released, reservation!.Status);
        Assert.Equal("The customer changed their mind.", reservation.Reason);
    }

    /// <summary>
    /// The outbox delivers at least once, so draining the same message twice has
    /// to be free. Only a real database can answer this one: the unique index on
    /// <c>order_id</c> is what makes the second attempt find the existing hold.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_redelivered_order_does_not_hold_the_stock_twice()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await SagaScope.CreateAsync(postgres, nameof(
            A_redelivered_order_does_not_hold_the_stock_twice));

        await scope.ReceiveAsync("SHOES", "MAD", 5, ct);
        var order = await scope.PlaceAsync([("SHOES", 2)], ct);

        await scope.DrainAsync(ct);
        await scope.RedeliverAsync<OrderPlaced>(ct);

        Assert.Equal(2, await scope.ReservedAsync("SHOES", "MAD", ct));
    }

    // ---------- The scope ----------

    /// <summary>
    /// The application's own container, with nothing swapped out: the real
    /// ledger, the real repositories, the real handlers, discovered by assembly
    /// the way the worker discovers them. A saga assembled by the test would be
    /// a saga only the test has.
    /// </summary>
    private sealed class SagaScope : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly TenderoDbContextFactory _factory;

        private SagaScope(ServiceProvider provider, TenderoDbContextFactory factory)
        {
            _provider = provider;
            _factory = factory;
        }

        public static async Task<SagaScope> CreateAsync(PostgresFixture postgres, string name)
        {
            var factory = await postgres.CreateDatabaseAsync(name.ToLowerInvariant());

            await using (var context = factory.Create())
                await context.Database.MigrateAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Inventory:Warehouses:FilePath"] =
                        Path.Combine(AppContext.BaseDirectory, "TestData", "warehouses.sample.json")
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoCqrs(typeof(ReserveStockOnOrderPlaced).Assembly);
            services.AddTenderoPersistence(factory.ConnectionString);
            services.AddInventory(configuration);

            return new SagaScope(services.BuildServiceProvider(validateScopes: true), factory);
        }

        public async Task ReceiveAsync(string sku, string warehouse, int quantity, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStockLedger>()
                .ReceiveAsync(sku, warehouse, quantity, ct);
        }

        public async Task<Order> PlaceAsync(
            IReadOnlyList<(string Sku, int Quantity)> lines, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var order = Order.Place(
                TimeProvider.System,
                CustomerId.New(),
                idempotencyKey: Guid.NewGuid().ToString(),
                culture: "es",
                lines:
                [
                    .. lines.Select(line => new OrderLine(
                        ProductId.New(), VariantId.New(), line.Sku, "A product", null,
                        new Money(10m, "EUR"), line.Quantity))
                ]);

            orders.Add(order);
            await unitOfWork.SaveChangesAsync(ct);

            return order;
        }

        public async Task CancelAsync(OrderId id, string reason, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var order = await orders.FindByIdAsync(id, ct);
            order!.Cancel(TimeProvider.System, reason);

            await unitOfWork.SaveChangesAsync(ct);
        }

        /// <summary>The same loop as <c>OutboxProcessor</c>, without the timer.</summary>
        public async Task<int> DrainAsync(CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

            var pending = await context.OutboxMessages
                .Where(message => message.ProcessedAt == null)
                .OrderBy(message => message.OccurredAt)
                .ToListAsync(ct);

            foreach (var message in pending)
            {
                await dispatcher.PublishAsync(
                    DomainEventSerializer.Deserialize(message.Type, message.Payload), ct);
                message.MarkProcessed();
            }

            await context.SaveChangesAsync(ct);
            return pending.Count;
        }

        /// <summary>Delivers an already-processed message again, which is what
        /// at-least-once actually means in practice.</summary>
        public async Task RedeliverAsync<TEvent>(CancellationToken ct) where TEvent : IDomainEvent
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

            var message = await context.OutboxMessages
                .Where(message => message.Type.Contains(typeof(TEvent).Name))
                .OrderBy(message => message.OccurredAt)
                .FirstAsync(ct);

            await dispatcher.PublishAsync(
                DomainEventSerializer.Deserialize(message.Type, message.Payload), ct);
        }

        public async Task<int> ReservedAsync(string sku, string warehouse, CancellationToken ct)
        {
            await using var context = _factory.Create();
            var row = await context.StockItems.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Sku == sku && item.WarehouseCode == warehouse, ct);

            return row?.Reserved ?? 0;
        }

        public async Task<int> AvailableAsync(string sku, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var available = await scope.ServiceProvider
                .GetRequiredService<IAvailabilityReader>().AvailableAsync([sku], ct);

            return available.GetValueOrDefault(sku, 0);
        }

        public async Task<Reservation?> ReservationAsync(OrderId orderId, CancellationToken ct)
        {
            await using var context = _factory.Create();
            return await context.Reservations.AsNoTracking()
                .FirstOrDefaultAsync(reservation => reservation.OrderId == orderId, ct);
        }

        public async Task<Order?> OrderAsync(OrderId id, CancellationToken ct)
        {
            await using var context = _factory.Create();
            return await context.Orders.AsNoTracking().FirstOrDefaultAsync(order => order.Id == id, ct);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
