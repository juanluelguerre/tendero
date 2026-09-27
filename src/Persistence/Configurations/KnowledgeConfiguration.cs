using ElGuerre.Tendero.Knowledge.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// The text a claim rests on. Its key is its content hash (ADR 0018), so a row is
/// written once and never updated: a description that changes is a new row.
/// </summary>
internal sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable("SourceDocuments", "knowledge");

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).HasMaxLength(64).ValueGeneratedNever();

        builder.Property(document => document.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(document => document.Culture).HasMaxLength(10);
        builder.Property(document => document.Text);
        builder.Property(document => document.RetrievedAt);
        builder.Property(document => document.ProposedAt);
        builder.Property(document => document.FailedAttempts);
        builder.Property(document => document.LastFailure).HasMaxLength(500);
        builder.Ignore(document => document.IsWaiting);

        // "The documents of this product" — to find the current text and supersede the claims of the old one.
        builder.HasIndex(document => new { document.ProductId, document.Culture });
    }
}

/// <summary>
/// A claim. Its evidence and provenance go to jsonb by ADR 0008's criterion: they
/// are only ever read with the claim that owns them. What is queried — the
/// product, the status, the attribute — is a column.
/// </summary>
internal sealed class ProductClaimConfiguration : IEntityTypeConfiguration<ProductClaim>
{
    public void Configure(EntityTypeBuilder<ProductClaim> builder)
    {
        builder.ToTable("Claims", "knowledge");

        builder.HasKey(claim => claim.Id);
        builder.Property(claim => claim.Id).ValueGeneratedNever();

        builder.Property(claim => claim.Statement)
            .HasConversion(Jsonb.LocalizedTextConverter)
            .HasColumnType("jsonb");

        builder.Property(claim => claim.AttributeCode).HasMaxLength(64);
        builder.Property(claim => claim.Value).HasMaxLength(200);
        builder.Property(claim => claim.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(claim => claim.ReviewedBy).HasMaxLength(200);
        builder.Property(claim => claim.ReviewedAt);
        builder.Property(claim => claim.CreatedAt);
        builder.Property(claim => claim.AppliedAs).HasMaxLength(200);
        builder.Property(claim => claim.ApplicationRefused).HasMaxLength(40);

        builder.ComplexProperty(claim => claim.Provenance, provenance =>
        {
            provenance.ToJson("provenance");
        });

        builder.ComplexCollection<List<Evidence>, Evidence>("evidence", evidence =>
        {
            evidence.ComplexProperty(item => item.Quote).HasJsonPropertyName("quote");
            evidence.ToJson("evidence");
        });

        // The review queue: a product's claims by status.
        builder.HasIndex(claim => new { claim.ProductId, claim.Status });

        builder.Ignore(claim => claim.Evidence);
        builder.Ignore(claim => claim.Support);
        builder.Ignore(claim => claim.DomainEvents);
    }
}
