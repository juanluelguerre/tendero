using System.Text.Json;
using ElGuerre.Tendero.Channels.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// What each channel has been told (phase 16, S3; ADR 0070), in Channels' own schema —
/// one schema per context (ADR 0014) — so the tables read <c>channels.listings</c>.
/// </summary>
internal sealed class ChannelListingConfiguration : IEntityTypeConfiguration<ChannelListing>
{
    // SKUs are compared as written, unlike attribute codes: the comparer travels with the value.
    private static readonly ValueComparer<Dictionary<string, string>> VariantIdsComparer = new(
        (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
        dictionary => dictionary.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
        dictionary => new Dictionary<string, string>(dictionary, StringComparer.Ordinal));

    public void Configure(EntityTypeBuilder<ChannelListing> builder)
    {
        builder.ToTable("Listings", "channels");
        builder.HasKey(listing => new { listing.Channel, listing.Code });
        builder.Property(listing => listing.Channel).HasMaxLength(40);
        builder.Property(listing => listing.Code).HasMaxLength(60);
        builder.Property(listing => listing.ExternalProductId).HasMaxLength(200);
        builder.Property(listing => listing.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(listing => listing.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(listing => listing.Refusal).HasMaxLength(2000);
        builder.Property(listing => listing.PublishedAt);
        builder.Property(listing => listing.RefusedAt);

        // Always read with the listing, never queried apart but for the one lookup a
        // stock publication makes by SKU, which reads the jsonb's keys.
        builder.Property<Dictionary<string, string>>("variantIds")
            .HasColumnName("VariantIds")
            .HasColumnType("jsonb")
            .HasConversion(
                ids => JsonSerializer.Serialize(ids, Jsonb.Options),
                json => new Dictionary<string, string>(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(json, Jsonb.Options) ?? new(), StringComparer.Ordinal),
                VariantIdsComparer)
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Ignore(listing => listing.VariantIds);
        builder.Ignore(listing => listing.DomainEvents);
    }
}

internal sealed class ChannelStockLevelConfiguration : IEntityTypeConfiguration<ChannelStockLevel>
{
    public void Configure(EntityTypeBuilder<ChannelStockLevel> builder)
    {
        builder.ToTable("StockLevels", "channels");
        builder.HasKey(level => new { level.Channel, level.Sku, level.Location });
        builder.Property(level => level.Channel).HasMaxLength(40);
        builder.Property(level => level.Sku).HasMaxLength(80);
        builder.Property(level => level.Location).HasMaxLength(200);
        builder.Property(level => level.Expected);
        builder.Property(level => level.PublishedAt);
        builder.Property(level => level.StaleSince);
        builder.Ignore(level => level.DomainEvents);
    }
}

internal sealed class ChannelCursorConfiguration : IEntityTypeConfiguration<ChannelCursor>
{
    public void Configure(EntityTypeBuilder<ChannelCursor> builder)
    {
        builder.ToTable("Cursors", "channels");
        builder.HasKey(cursor => cursor.Channel);
        builder.Property(cursor => cursor.Channel).HasMaxLength(40);
        builder.Property(cursor => cursor.OrdersReadUntil);
        builder.Ignore(cursor => cursor.DomainEvents);
    }
}
