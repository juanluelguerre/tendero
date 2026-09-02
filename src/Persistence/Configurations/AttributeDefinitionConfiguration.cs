using ElGuerre.Tendero.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// Las definiciones van a TABLA y no a jsonb: se consultan de verdad. El
/// backoffice las lista, el importador resuelve claves contra ellas y la
/// proyección al índice las lee entera en cada reindexado.
///
/// Las OPCIONES sí van en jsonb dentro de su definición: no se consultan
/// sueltas, sólo se leen con ella. Es el criterio de ADR 0008 aplicado dos veces
/// en el mismo agregado, y da respuestas distintas porque las preguntas lo son.
/// </summary>
internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.ToTable("AttributeDefinitions", "catalog");

        // El código ES la identidad: estable, en mayúsculas y con significado.
        // No hay un GUID detrás porque no hay nada que corregir sin que sea
        // otra definición.
        builder.HasKey(definition => definition.Code);
        builder.Property(definition => definition.Code).HasMaxLength(64);

        builder.Property(definition => definition.Label)
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.LocalizedTextConverter, Jsonb.LocalizedTextComparer)
            .IsRequired();

        builder.Property(definition => definition.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(definition => definition.Unit).HasMaxLength(20);

        builder.Property<List<AttributeOption>>("_options")
            .HasColumnName("Options")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AttributeOptionsConverter, Jsonb.AttributeOptionsComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property<List<string>>("_aliases")
            .HasColumnName("Aliases")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.StringListConverter, Jsonb.StringListComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(definition => definition.Options);
        builder.Ignore(definition => definition.Aliases);
        builder.Ignore(definition => definition.DomainEvents);
    }
}
