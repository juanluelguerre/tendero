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

        // A picture's reading (phase 13a, I-4): which picture, and who read it.
        builder.Property(document => document.PictureId).HasMaxLength(128);
        builder.Property(document => document.ReadByModel).HasMaxLength(200);
        builder.Property(document => document.ReadWithPrompt).HasMaxLength(100);
        builder.Ignore(document => document.Reading);

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
        builder.Ignore(claim => claim.IsWeaklySupported);
        builder.Ignore(claim => claim.DomainEvents);
    }
}

/// <summary>
/// A doubt about a picture (phase 13a, I-4d). Columns, all of it: it is listed
/// by status and found by its picture, and nothing in it is only ever read with
/// something else.
/// </summary>
internal sealed class PictureDoubtConfiguration : IEntityTypeConfiguration<PictureDoubt>
{
    public void Configure(EntityTypeBuilder<PictureDoubt> builder)
    {
        builder.ToTable("PictureDoubts", "knowledge");

        builder.HasKey(doubt => doubt.Id);
        builder.Property(doubt => doubt.Id).ValueGeneratedNever();

        builder.Property(doubt => doubt.PictureId).HasMaxLength(128);
        builder.Property(doubt => doubt.AttributeCode).HasMaxLength(64);
        builder.Property(doubt => doubt.Seen).HasMaxLength(64);
        builder.Property(doubt => doubt.Held).HasMaxLength(64);
        builder.Property(doubt => doubt.SeenLabel).HasConversion(Jsonb.LocalizedTextConverter).HasColumnType("jsonb");
        builder.Property(doubt => doubt.HeldLabel).HasConversion(Jsonb.LocalizedTextConverter).HasColumnType("jsonb");
        builder.Property(doubt => doubt.Confidence);
        builder.Property(doubt => doubt.SeenOnAnotherVariant);
        builder.Property(doubt => doubt.ReadByModel).HasMaxLength(200);
        builder.Property(doubt => doubt.ReadWithPrompt).HasMaxLength(100);
        builder.Property(doubt => doubt.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(doubt => doubt.Decision).HasConversion<string>().HasMaxLength(40);
        builder.Property(doubt => doubt.DecidedBy).HasMaxLength(200);
        builder.Property(doubt => doubt.DecidedAt);
        builder.Property(doubt => doubt.RaisedAt);

        // The queue: the open ones, oldest first.
        builder.HasIndex(doubt => new { doubt.Status, doubt.RaisedAt });

        // "What was ever doubted of this picture" — asked each time it is read again.
        builder.HasIndex(doubt => new { doubt.ProductId, doubt.PictureId });

        builder.Ignore(doubt => doubt.DomainEvents);
    }
}
