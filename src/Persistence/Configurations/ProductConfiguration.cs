using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", "catalog");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Nothing user-facing in flat columns (invariant 6): localized text is
        // stored as {"es": "…", "en": "…"} and reads back in psql.
        builder.Property(p => p.Name)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(p => p.Slug)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        // The public identifier, and the only one a URL ever carries (ADR 0026).
        // Unique because it IS the key — which is the whole argument for its
        // existing: the slug could never carry a unique constraint, because
        // uniqueness across the values of a jsonb object is not expressible as
        // one, and the id could not be published because a GUID v7 leaks the
        // creation time it sorts by.
        //
        // The index is what makes the guarantee real rather than statistical.
        // Fifty bits of randomness make a collision vanishingly unlikely; this
        // makes one impossible.
        builder.Property(p => p.Code)
            .HasMaxLength(ProductCode.Length)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("ux_products_code");

        builder.Property(p => p.Description)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter!, Jsonb.LocalizedTextComparer!);

        builder.Property(p => p.Brand).HasMaxLength(200);

        // A column and not jsonb: "the newest twenty" is an ORDER BY, and this
        // is the criterion for it — which is ADR 0008's own test for what earns
        // a column.
        builder.Property(p => p.AvailableFrom);
        builder.Property(p => p.Category).HasMaxLength(200);

        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        // The enum as text: a SELECT in production has to be readable.
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        // Images are not queried separately: a complex collection in a JSON
        // column. EF knows the shape, so it is still a model and not a blob.
        // The field is mapped, not the property: EF requires IList<T> for a
        // complex collection and the aggregate exposes IReadOnlyList<T>, which
        // stays untouched.
        builder.ComplexCollection<List<ProductImage>, ProductImage>("_images", image =>
        {
            // The alternative text is LocalizedText: inside the JSON it travels
            // as the same culture -> text dictionary as the rest of the catalogue.
            image.Property(i => i.Alt)
                .HasConversion(Jsonb.NullableLocalizedTextConverter, Jsonb.NullableLocalizedTextComparer);
            image.ToJson("images");
        });

        builder.Ignore(p => p.Images);

        // Attribute values went from Dictionary<string,string> to a typed list.
        // They stay in jsonb because they are always read with their product;
        // what does get queried are the DEFINITIONS, and those go to a table.
        builder.Property<List<AttributeValue>>("_attributes")
            .HasColumnName("Attributes")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AttributeValuesConverter, Jsonb.AttributeValuesComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(p => p.Attributes);
        builder.Ignore(p => p.DomainEvents);

        // External references ARE queried: they are the import's idempotency
        // key, so they go to a table with a unique index on (source, externalId).
        builder.OwnsMany(p => p.ExternalReferences, reference =>
        {
            reference.ToTable("ProductExternalReferences", "catalog");
            reference.WithOwner().HasForeignKey("ProductId");
            reference.Property(r => r.Source).HasMaxLength(50).IsRequired();
            reference.Property(r => r.ExternalId).HasMaxLength(400).IsRequired();
            reference.HasKey("ProductId", nameof(ExternalReference.Source), nameof(ExternalReference.ExternalId));
            reference.HasIndex(r => new { r.Source, r.ExternalId }).IsUnique();
        });

        builder.Navigation(p => p.ExternalReferences).AutoInclude();

        // Variants go to a TABLE and not to a JSON column like the images, by
        // ADR 0008's criterion: they are genuinely queried — by SKU, which is how
        // inventory, the cart and UCP talk about them.
        //
        // And as an ENTITY rather than an owned collection, for a concrete
        // reason: Money is a record struct, EF does not accept structs as owned
        // types, and OwnedNavigationBuilder exposes no ComplexProperty. The way
        // out would have been storing the price as text ("29.90 EUR"), which is
        // what OrderLine does inside its JSON column. There it is consistent;
        // here it would be a table that exists to be queried, with the column
        // that gets queried most turned into a string.
        builder.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Variants).AutoInclude();

        // The order of the axes IS data (ADR 0015): "azul marino · 38" and not
        // the other way round. As a list of strings it fits in jsonb without
        // ceremony.
        builder.Property<List<string>>("_variantAxes")
            .HasColumnName("VariantAxes")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.StringListConverter, Jsonb.StringListComparer)
            // The default is declared HERE and it is an array, not an object. EF,
            // on adding a required column to a table with rows, invents one of
            // its own — and it chose `'{}'`, an empty JSON object, where a list
            // belongs. Every already-imported product would have stopped
            // deserialising. The test that compares the migration's schema with
            // the model's caught it, which is exactly what it is there for.
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(p => p.VariantAxes);
        builder.Ignore(p => p.PriceRange);
    }
}
