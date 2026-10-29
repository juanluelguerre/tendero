using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A catalogue import's runs (phase 16, P16-12). The products a run created are
/// a column of ids and not a table: they are read whole, by the run, and never
/// queried one by one.
/// </summary>
internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.ToTable("ImportRuns", "catalog");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.Source).HasMaxLength(50).IsRequired();
        builder.Property(run => run.StartedAt).IsRequired();

        // A uuid[] in Postgres. The id's conversion is said again for the element:
        // the type-wide one does not reach inside a primitive collection.
        builder.PrimitiveCollection<List<ProductId>>("products")
            .HasColumnName("products")
            .ElementType(element => element.HasConversion(
                new ValueConverter<ProductId, Guid>(id => id.Value, value => new ProductId(value))))
            .IsRequired();
        builder.Ignore(run => run.Products);
    }
}
