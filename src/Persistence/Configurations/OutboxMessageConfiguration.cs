using ElGuerre.Tendero.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Messages", "outbox");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(500).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredAt);
        builder.Property(m => m.ProcessedAt);
        builder.Property(m => m.Attempts);
        builder.Property(m => m.Error).HasMaxLength(4000);

        // The worker only asks for what is pending, in arrival order; the
        // filtered index keeps that query cheap however large the table grows.
        builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt })
            .HasFilter("processed_at IS NULL");
    }
}
