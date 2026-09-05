using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A return and its lines.
///
/// A table of its own and not a column on the order, because a return is not a
/// state of the order (see <c>ReturnRequest</c>) — and because an order can have
/// more than one, which is exactly what "returns are per line" means in practice.
///
/// The lines go to jsonb by ADR 0008's criterion: they are only ever read with
/// the request that owns them. There is deliberately no foreign key to the
/// order's own lines; the SKU is the vocabulary, as it is everywhere else.
/// </summary>
internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("ReturnRequests", "ordering");

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(request => request.Resolution).HasMaxLength(500);
        builder.Property(request => request.RefundReference).HasMaxLength(200);

        builder.Property(request => request.RefundAmount)
            .HasConversion(Jsonb.NullableMoneyAsTextConverter);

        builder.Property(request => request.CreatedAt);
        builder.Property(request => request.UpdatedAt);

        // The order page lists the returns against it, and the backoffice queue
        // filters by status.
        builder.HasIndex(request => request.OrderId);
        builder.HasIndex(request => request.Status);

        builder.ComplexCollection<List<ReturnLine>, ReturnLine>("_lines", line =>
        {
            line.Property(l => l.Reason).HasConversion<string>();
            line.ToJson("lines");
        });

        builder.Ignore(request => request.Lines);
        builder.Ignore(request => request.DomainEvents);
    }
}
