using ElGuerre.Tendero.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// Pricing's first table (ADR 0044), in Pricing's own schema — one schema per
/// context (ADR 0014), so the table reads <c>pricing.campaigns</c>.
/// </summary>
internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns", "pricing");

        // The code is the key: it is what a listing's ?campaign= carries, and it never changes.
        builder.HasKey(campaign => campaign.Code);
        builder.Property(campaign => campaign.Code).HasMaxLength(CampaignCode.MaxLength).ValueGeneratedNever();

        // Nothing user-facing in flat columns (invariant 6).
        builder.Property(campaign => campaign.Name)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(campaign => campaign.Line)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(campaign => campaign.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(campaign => campaign.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(campaign => campaign.CategoryCode).HasMaxLength(60);
        builder.Property(campaign => campaign.StartsAt);
        builder.Property(campaign => campaign.EndsAt);
        builder.Property(campaign => campaign.Priority);
        builder.Property(campaign => campaign.EndedAt);
        builder.Property(campaign => campaign.CreatedAt);
        builder.Property(campaign => campaign.UpdatedAt);

        // The grouped promotions are always read with their campaign and never
        // queried on their own: a list of codes in jsonb, as a product's variant
        // axes are. The field is mapped; the aggregate exposes a read-only list.
        builder.Property<List<string>>("promotionCodes")
            .HasColumnName("PromotionCodes")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.StringListConverter, Jsonb.StringListComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(campaign => campaign.ImageId).HasMaxLength(128);
        builder.Property(campaign => campaign.ImageAlt)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.NullableLocalizedTextConverter, Jsonb.NullableLocalizedTextComparer);

        // The shop's one question — which scheduled campaigns have not closed —
        // is a status and a range on the window's end.
        builder.HasIndex(campaign => new { campaign.Status, campaign.EndsAt })
            .HasDatabaseName("ix_campaigns_status_ends_at");

        builder.Ignore(campaign => campaign.PromotionCodes);
        builder.Ignore(campaign => campaign.Scope);
        builder.Ignore(campaign => campaign.Image);
        builder.Ignore(campaign => campaign.IsEditable);
        builder.Ignore(campaign => campaign.DomainEvents);
    }
}
