using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

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

        // Retrying checkout with the same key must not create a second order,
        // and with agents retries are the normal case, not the rare one.
        builder.Property(o => o.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        builder.Property(o => o.Culture).HasMaxLength(5).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.CreatedAt);
        builder.Property(o => o.UpdatedAt);

        // A snapshot, not a live FK to the product: the name already resolved in
        // the buyer's culture and the price frozen (ADR 0002). They are never
        // queried on their own from the domain, so they travel with the order in
        // a JSON column.
        builder.ComplexCollection<List<OrderLine>, OrderLine>("_lines", line =>
        {
            line.Property(l => l.ProductId).HasConversion(id => id.Value, value => new ProductId(value));
            // The variant travels on the line because what gets bought is a
            // variant (ADR 0015): without it the order does not know which size
            // was shipped, and inventory, which decrements by SKU, has nothing
            // to work with.
            line.Property(l => l.VariantId).HasConversion(id => id.Value, value => new VariantId(value));
            line.Property(l => l.UnitPrice).HasConversion(Jsonb.MoneyAsTextConverter);
            line.Ignore(l => l.Total);
            line.ToJson("lines");
        });

        builder.Ignore(o => o.Lines);

        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}
