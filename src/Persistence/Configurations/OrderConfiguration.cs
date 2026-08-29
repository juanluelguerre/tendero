using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tendero.Ordering.Domain;
using Tendero.SharedKernel;

namespace Tendero.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", "ordering");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value));

        // Reintentar el checkout con la misma clave no puede crear un segundo
        // pedido, y con agentes los reintentos son el caso normal, no el raro.
        builder.Property(o => o.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        builder.Property(o => o.Culture).HasMaxLength(5).IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.CreatedAt);
        builder.Property(o => o.UpdatedAt);

        // Snapshot, no FK viva al producto: nombre ya resuelto en la cultura del
        // comprador y precio congelado (ADR 0002). Nunca se consultan sueltas
        // desde el dominio, así que viajan con el pedido en una columna JSON.
        builder.ComplexCollection<List<OrderLine>, OrderLine>("_lines", line =>
        {
            line.Property(l => l.ProductId).HasConversion(id => id.Value, value => new ProductId(value));
            line.Property(l => l.UnitPrice).HasConversion(Jsonb.MoneyAsTextConverter);
            line.Ignore(l => l.Total);
            line.ToJson("lines");
        });

        builder.Ignore(o => o.Lines);

        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}
