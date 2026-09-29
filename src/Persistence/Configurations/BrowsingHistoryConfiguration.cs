using ElGuerre.Tendero.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A customer's trail (R-8), in Accounts' schema: <c>accounts.browsing_histories</c>,
/// one row per customer, the entries as two jsonb lists — always read and written
/// with their trail, and capped at fifty and twenty.
/// </summary>
internal sealed class BrowsingHistoryConfiguration : IEntityTypeConfiguration<BrowsingHistory>
{
    public void Configure(EntityTypeBuilder<BrowsingHistory> builder)
    {
        builder.ToTable("BrowsingHistories", "accounts");

        // The customer is the key: a trail has no identity of its own.
        builder.HasKey(history => history.CustomerId);
        builder.Property(history => history.CustomerId).ValueGeneratedNever();

        builder.Property(history => history.IsEnabled);
        builder.Property(history => history.CreatedAt);
        builder.Property(history => history.UpdatedAt);

        builder.Property<List<TrailEntry>>("views")
            .HasColumnName("Views")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.TrailConverter, Jsonb.TrailComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property<List<TrailEntry>>("searches")
            .HasColumnName("Searches")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.TrailConverter, Jsonb.TrailComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(history => history.DomainEvents);
    }
}
