using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A customer's saved ways to pay (P15-4), in Ordering's schema: <c>ordering.wallets</c>,
/// one row per customer, the methods as one jsonb list and the tokens waiting to be
/// forgotten as another, the default as a column.
///
/// The tokens are the provider's, stored as the provider handed them back. A
/// provider's token charges nothing without the shop's own credentials with that
/// provider, which never live in this database (ADR 0064).
/// </summary>
internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets", "ordering");

        builder.HasKey(wallet => wallet.CustomerId);
        builder.Property(wallet => wallet.CustomerId).ValueGeneratedNever();

        // "default" is a word SQL keeps for itself.
        builder.Property(wallet => wallet.Default).HasColumnName("DefaultMethod");
        builder.Property(wallet => wallet.CreatedAt);
        builder.Property(wallet => wallet.UpdatedAt);

        builder.Property<List<SavedMethod>>("methods")
            .HasColumnName("Methods")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.SavedMethodsConverter, Jsonb.SavedMethodsComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property<List<TokenToForget>>("toForget")
            .HasColumnName("ToForget")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.TokensToForgetConverter, Jsonb.TokensToForgetComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(wallet => wallet.Methods);
        builder.Ignore(wallet => wallet.ToForget);
        builder.Ignore(wallet => wallet.IsFull);
        builder.Ignore(wallet => wallet.DomainEvents);
    }
}
