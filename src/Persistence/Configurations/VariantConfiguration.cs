using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// La variante es una entidad del agregado Product, no un agregado: se carga y
/// se guarda con su padre (<c>AutoInclude</c> y borrado en cascada), y ninguna
/// operación la modifica sin pasar por él.
///
/// Tiene configuración propia porque necesita <c>ComplexProperty</c> para
/// <see cref="Money"/>, que sólo está disponible sobre un tipo de entidad — ver
/// la nota en <see cref="ProductConfiguration"/>.
/// </summary>
internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.ToTable("Variants", "catalog");
        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id)
            .HasConversion(id => id.Value, value => new VariantId(value))
            .ValueGeneratedNever();

        builder.Property(variant => variant.Sku).HasMaxLength(100).IsRequired();

        // Único en TODO el catálogo, no dentro del producto: es la clave con la
        // que otros contextos hablan de esto sin conocer Catalog.
        builder.HasIndex(variant => variant.Sku).IsUnique();

        builder.ComplexProperty(variant => variant.Price, price =>
        {
            price.Property(money => money.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            price.Property(money => money.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        builder.Property(variant => variant.TaxClass).HasMaxLength(50);
        builder.Property(variant => variant.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(variant => variant.Image)
            .HasConversion(
                id => id!.Value.Value,
                value => new ImageId(value))
            .HasColumnName("ImageId")
            .HasMaxLength(64);

        // Los ejes son un diccionario, que no tiene equivalente complejo en EF:
        // mismo convertidor jsonb que los atributos del producto.
        builder.Property<Dictionary<string, string>>("_axisValues")
            .HasColumnName("AxisValues")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AttributesConverter, Jsonb.AttributesComparer)
            .IsRequired();

        builder.Ignore(variant => variant.AxisValues);
    }
}
