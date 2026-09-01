using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence.Configurations;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Un DbContext, dos esquemas (catalog, ordering) y la outbox.
/// Los contextos acotados no comparten entidades — comparten conexión, que es
/// lo que hace que evento y cambio de estado entren en la misma transacción.
/// </summary>
public sealed class TenderoDbContext(DbContextOptions<TenderoDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        SnakeCaseNames.Apply(modelBuilder);
    }

    /// <summary>
    /// Aquí vive la invariante 7: nadie publica un evento a mano. Todo lo que
    /// los agregados hayan levantado durante la unidad de trabajo se convierte
    /// en filas de outbox y se guarda con el mismo commit.
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
