using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// Stock, keyed on <c>(sku, warehouse_code)</c> — two strings and no foreign key
/// to anything.
///
/// That composite key IS the architectural decision made physical. A surrogate
/// id with a foreign key to a variant would work, read better in a diagram, and
/// give Inventory a reference to Catalog's table; this way the only thing shared
/// between the two contexts is a SKU, which is the vocabulary they already
/// speak. There is deliberately no FK: reference integrity across a context
/// boundary is the coupling that ADR 0014 exists to prevent, and a stock row for
/// a SKU the catalogue has not imported yet is a warehouse being ahead of the
/// paperwork, not a broken database.
/// </summary>
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems", "inventory");

        // The natural key, and no surrogate id: one SKU is in one warehouse
        // exactly once, and an id would let a second row exist for the same
        // pair — which is how a shop ends up with two counts of the same shelf.
        builder.HasKey(item => new { item.Sku, item.WarehouseCode });

        builder.Property(item => item.Sku).HasMaxLength(100).IsRequired();
        builder.Property(item => item.WarehouseCode).HasMaxLength(20).IsRequired();

        builder.Property(item => item.OnHand).IsRequired();
        builder.Property(item => item.Reserved).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();

        // Available is OnHand - Reserved, computed in the domain. Persisting it
        // would be a third number that can disagree with the other two.
        builder.Ignore(item => item.Available);

        // The projection and the storefront both ask "how much of these SKUs is
        // there", across warehouses.
        builder.HasIndex(item => item.Sku);
    }
}

/// <summary>
/// A reservation and its lines. The lines go in a JSON column by ADR 0008's
/// criterion — they are never queried on their own, only ever read with the
/// reservation that owns them — while the reservation itself is a table because
/// the backoffice lists them and the ledger looks one up by order.
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", "inventory");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();

        // An OrderId from the SharedKernel, stored as the bare GUID. It is not a
        // foreign key and there is no navigation: Inventory knows an order has
        // this id and nothing else about it.
        builder.Property(reservation => reservation.OrderId).IsRequired();

        // One live hold per order. The ledger relies on it to make a retried
        // checkout return the existing reservation instead of holding the stock
        // twice — with agents, a retry is the normal case.
        builder.HasIndex(reservation => reservation.OrderId).IsUnique();

        // The enum as text: a SELECT in production has to be readable.
        builder.Property(reservation => reservation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reservation => reservation.Reason).HasMaxLength(500);
        builder.Property(reservation => reservation.CreatedAt).IsRequired();
        builder.Property(reservation => reservation.UpdatedAt).IsRequired();
        builder.Property(reservation => reservation.ExpiresAt).IsRequired();

        // No HasMaxLength inside a complex collection: the values live in a JSON
        // column, so there is no column to size. Same shape as OrderLine.
        builder.ComplexCollection<List<ReservationLine>, ReservationLine>("_lines", line =>
        {
            line.Property(l => l.Sku);
            line.Property(l => l.WarehouseCode);
            line.Property(l => l.Quantity);
            line.ToJson("lines");
        });

        builder.Ignore(reservation => reservation.Lines);
    }
}
