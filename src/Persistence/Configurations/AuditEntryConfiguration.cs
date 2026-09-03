using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// The audit log's table. Its own schema because it belongs to no context: every
/// context writes to it and none owns it, which is exactly the shape the outbox
/// already has.
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries", "audit");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.CommandType).HasMaxLength(200).IsRequired();

        // jsonb and not text: the copilot (phase 12) queries these payloads, and
        // a column it has to parse per row is a column it cannot index.
        builder.Property(entry => entry.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(entry => entry.Customer)
            .HasConversion(
                id => id!.Value.Value,
                value => new CustomerId(value));

        builder.Property(entry => entry.Agent)
            .HasConversion(
                id => id!.Value.Value,
                value => new AgentId(value))
            .HasMaxLength(200);

        builder.Property(entry => entry.Subject).HasMaxLength(200);

        // The enum as text, for the reason every other enum here is: a SELECT in
        // production has to be readable, and this is the table a person opens
        // when they are already having a bad day.
        builder.Property(entry => entry.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entry => entry.Reason).HasMaxLength(1000);
        builder.Property(entry => entry.TraceId).HasMaxLength(64);
        builder.Property(entry => entry.At).IsRequired();

        // What the audit screen actually asks: the most recent first.
        builder.HasIndex(entry => entry.At).IsDescending();

        // And what the agent activity panel asks (phase 11): what did THIS agent
        // do. Filtered, because the overwhelming majority of rows have no agent
        // and an index over them would be mostly nulls.
        builder.HasIndex(entry => new { entry.Agent, entry.At })
            .HasFilter("agent IS NOT NULL")
            .IsDescending(false, true);

        // The interesting rows, and the reason Denied is its own outcome. Phase
        // 11's panel opens on the denials, and finding them should not mean
        // scanning every allowed command in the shop's history.
        builder.HasIndex(entry => new { entry.Outcome, entry.At })
            .HasFilter("outcome <> 'Allowed'")
            .IsDescending(false, true);
    }
}
