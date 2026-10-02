using ElGuerre.Tendero.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>One row per model call (phase 12), in a schema of its own like the
/// audit's: every context's model calls land here and none owns it.</summary>
internal sealed class AIInvocationConfiguration : IEntityTypeConfiguration<AIInvocation>
{
    public void Configure(EntityTypeBuilder<AIInvocation> builder)
    {
        builder.ToTable("AIInvocations", "telemetry");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();

        builder.Property(row => row.Operation).HasMaxLength(100).IsRequired();
        builder.Property(row => row.Profile).HasMaxLength(100).IsRequired();
        builder.Property(row => row.Model).HasMaxLength(200).IsRequired();
        builder.Property(row => row.PromptVersion).HasMaxLength(100);
        builder.Property(row => row.ConversationId).HasMaxLength(100);
        builder.Property(row => row.TraceId).HasMaxLength(64);

        // As text, as every enum here: a SELECT has to be readable.
        builder.Property(row => row.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();

        // What the cost line and the panel ask: a conversation's rows, and a day's.
        builder.HasIndex(row => row.ConversationId).HasFilter("conversation_id IS NOT NULL");
        builder.HasIndex(row => row.At).IsDescending();
    }
}
