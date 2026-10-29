using ElGuerre.Tendero.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A source's collections as categories (phase 16, P16-11), in the catalogue's
/// schema: the redirects ask by handle alone, since an old address does not say
/// which source it was, hence the index beside the key.
/// </summary>
internal sealed class SourceCollectionConfiguration : IEntityTypeConfiguration<SourceCollection>
{
    public void Configure(EntityTypeBuilder<SourceCollection> builder)
    {
        builder.ToTable("SourceCollections", "catalog");
        builder.HasKey(collection => new { collection.Source, collection.Handle });

        builder.Property(collection => collection.Source).HasMaxLength(50);
        builder.Property(collection => collection.Handle).HasMaxLength(255);
        builder.Property(collection => collection.CategoryCode).HasMaxLength(200).IsRequired();
        builder.Property(collection => collection.UpdatedAt).IsRequired();

        builder.HasIndex(collection => collection.Handle);
    }
}
