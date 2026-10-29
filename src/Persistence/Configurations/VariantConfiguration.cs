using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A variant is an entity of the Product aggregate, not an aggregate: it is
/// loaded and saved with its parent (<c>AutoInclude</c> and cascade delete), and
/// no operation modifies it without going through it.
///
/// It has its own configuration because it needs <c>ComplexProperty</c> for
/// <see cref="Money"/>, which is only available on an entity type — see the note
/// in <see cref="ProductConfiguration"/>.
/// </summary>
internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.ToTable("Variants", "catalog");
        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id).ValueGeneratedNever();

        builder.Property(variant => variant.Sku).HasMaxLength(100).IsRequired();

        // Unique across the WHOLE catalogue, not within the product: it is the
        // key other contexts talk about this by, without knowing Catalog.
        builder.HasIndex(variant => variant.Sku).IsUnique();

        builder.ComplexProperty(variant => variant.Price, price =>
        {
            price.Property(money => money.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            price.Property(money => money.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        builder.Property(variant => variant.TaxClass).HasMaxLength(50);
        builder.Property(variant => variant.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(variant => variant.Barcode).HasMaxLength(64);

        // Which variant each source knows this as (phase 16): queried on every
        // re-import, so a table with a unique (source, external id), as the
        // product's own references are. P1-2 deferred it to the first source
        // with ids per variant.
        builder.OwnsMany(variant => variant.ExternalReferences, reference =>
        {
            reference.ToTable("VariantExternalReferences", "catalog");
            reference.WithOwner().HasForeignKey("VariantId");
            reference.Property(r => r.Source).HasMaxLength(50).IsRequired();
            reference.Property(r => r.ExternalId).HasMaxLength(400).IsRequired();
            reference.Ignore(r => r.Handle);
            reference.HasKey("VariantId", nameof(ExternalReference.Source), nameof(ExternalReference.ExternalId));
            reference.HasIndex(r => new { r.Source, r.ExternalId }).IsUnique();
        });

        builder.Navigation(variant => variant.ExternalReferences).AutoInclude();

        // The axes are a dictionary, which has no complex equivalent in EF: the
        // same jsonb converter as the product's attributes.
        builder.Property<Dictionary<string, string>>("axisValues")
            .HasColumnName("AxisValues")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AttributesConverter, Jsonb.AttributesComparer)
            .IsRequired();

        builder.Ignore(variant => variant.AxisValues);
    }
}
