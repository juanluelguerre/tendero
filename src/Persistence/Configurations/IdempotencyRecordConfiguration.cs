using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// The idempotency keys' table. Its own schema, like the audit log's and for
/// the same reason: every surface that writes may keep a key here and no
/// context owns it.
/// </summary>
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("Keys", "idempotency");

        // The claim itself: of two inserts of one caller's key, one fails.
        builder.HasKey(record => new { record.Scope, record.Key });

        builder.Property(record => record.Scope).HasMaxLength(200);
        builder.Property(record => record.Key).HasMaxLength(Idempotency.MaximumKeyLength);
        builder.Property(record => record.Fingerprint).HasMaxLength(64).IsRequired();

        // Text and not jsonb: an answer is given back as the bytes it was, and
        // a signed checkout is JSON whose member order somebody signed.
        builder.Property(record => record.Body).HasColumnType("text");

        builder.Property(record => record.At).IsRequired();
    }
}
