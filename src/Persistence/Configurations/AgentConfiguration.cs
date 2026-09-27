using ElGuerre.Tendero.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents", "accounts");

        // The token's agent_id is the key: the principal accessor looks an agent
        // up by it on every request an agent makes.
        builder.HasKey(agent => agent.Id);
        builder.Property(agent => agent.Id).HasMaxLength(100).ValueGeneratedNever();

        builder.Property(agent => agent.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(agent => agent.Operator).HasMaxLength(100).IsRequired();
        builder.Property(agent => agent.Trust).HasConversion<string>().HasMaxLength(20);
        builder.Property(agent => agent.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(agent => agent.SuspensionReason).HasMaxLength(300);
        builder.Property(agent => agent.RegisteredAt);
        builder.Property(agent => agent.UpdatedAt);

        builder.Ignore(agent => agent.Standing);
        builder.Ignore(agent => agent.DomainEvents);
    }
}
