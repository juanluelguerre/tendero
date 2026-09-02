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
            image.Property(i => i.Id).HasConversion(id => id.Value, value => new ImageId(value));
            // El texto alternativo es LocalizedText: dentro del JSON viaja como
            // el mismo diccionario cultura -> texto que el resto del catálogo.
            image.Property(i => i.Alt)
                .HasConversion(Jsonb.NullableLocalizedTextConverter, Jsonb.NullableLocalizedTextComparer);
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

        // Las variantes van a TABLA y no a una columna JSON como las imágenes,
        // con el criterio de ADR 0008: se consultan de verdad — por SKU, que es
        // como inventario, carrito y UCP hablan de ellas.
        //
        // Y como ENTIDAD, no como colección propietaria, por una razón concreta:
        // Money es un record struct, EF no admite structs como tipos
        // propietarios, y OwnedNavigationBuilder no expone ComplexProperty. La
        // salida habría sido guardar el precio como texto ("29.90 EUR"), que es
        // lo que hace OrderLine dentro de su columna JSON. Ahí es consistente;
        // aquí sería una tabla que existe para consultarse con la columna que
        // más se consulta convertida en cadena.
        builder.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Variants).AutoInclude();

        // El orden de los ejes ES un dato (ADR 0015): "azul marino · 38" y no al
        // revés. Como lista de cadenas cabe en jsonb sin ceremonia.
        builder.Property<List<string>>("_variantAxes")
            .HasColumnName("VariantAxes")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.StringListConverter, Jsonb.StringListComparer)
            // El default se declara AQUÍ y es un array, no un objeto. EF, al
            // añadir una columna requerida a una tabla con filas, inventa uno
            // por su cuenta — y eligió `'{}'`, un objeto JSON vacío, donde va
            // una lista. Todo producto ya importado habría dejado de
            // deserializar. Lo cazó el test que compara el esquema de la
            // migración con el del modelo, que es exactamente para lo que está.
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(p => p.VariantAxes);
        builder.Ignore(p => p.PriceRange);
    }
}
