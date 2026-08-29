using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tendero.Catalog.Domain;
using Tendero.SharedKernel;

namespace Tendero.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", "catalog");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProductId(value))
            .ValueGeneratedNever();

        // Nada de cara al usuario en columnas planas (invariante 6): el texto
        // localizado se guarda como {"es": "...", "en": "..."} y se lee en psql.
        builder.Property(p => p.Name)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(p => p.Slug)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter!, Jsonb.LocalizedTextComparer!);

        builder.Property(p => p.Brand).HasMaxLength(200);
        builder.Property(p => p.Category).HasMaxLength(200);

        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        // Enum como texto: un SELECT en producción debe poder leerse.
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        // Las imágenes no se consultan por separado: colección compleja en una
        // columna JSON. EF conoce la forma, así que sigue siendo un modelo, no un blob.
        // Se mapea el campo, no la propiedad: EF exige IList<T> para una colección
        // compleja y el agregado expone IReadOnlyList<T>, que no se toca.
        builder.ComplexCollection<List<ProductImage>, ProductImage>("_images", image =>
        {
            image.Property(i => i.Url).HasConversion(url => url.ToString(), value => new Uri(value));
            image.ToJson("images");
        });

        builder.Ignore(p => p.Images);

        // Los atributos son un diccionario, que no tiene equivalente en tipos
        // complejos: conversor a jsonb, preservando el comparador OrdinalIgnoreCase.
        builder.Property<Dictionary<string, string>>("_attributes")
            .HasColumnName("Attributes")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AttributesConverter, Jsonb.AttributesComparer)
            .IsRequired();

        builder.Ignore(p => p.Attributes);
        builder.Ignore(p => p.DomainEvents);

        // Las referencias externas SÍ se consultan: son la clave de idempotencia
        // de la importación, así que van a tabla con índice único (source, externalId).
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
    }
}
