using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence.Configurations;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// One DbContext, three schemas (catalog, inventory, ordering) and the outbox.
/// Bounded contexts share no entities — they share a connection, which is what
/// puts an event and its state change in the same transaction.
/// </summary>
public sealed class TenderoDbContext(DbContextOptions<TenderoDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new VariantConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        SnakeCaseNames.Apply(modelBuilder);
    }

    /// <summary>
    /// Invariant 7 lives here: nobody publishes an event by hand. Everything the
    /// aggregates raised during the unit of work becomes outbox rows and is saved
    /// with the same commit.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DrainDomainEventsToOutbox();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        DrainDomainEventsToOutbox();
        return base.SaveChanges();
    }

    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(OutboxMessage.For(
                    DomainEventSerializer.TypeNameOf(domainEvent),
                    DomainEventSerializer.Serialize(domainEvent),
                    domainEvent.OccurredAt));
            }

            aggregate.ClearDomainEvents();
        }
    }
}
