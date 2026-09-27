using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> builder)
    {
        builder.ToTable("checkout_sessions", "ordering");

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        // Read by its owner only, and listed by owner when the activity panel asks.
        builder.Property(session => session.Owner).HasMaxLength(100).IsRequired();
        builder.HasIndex(session => session.Owner);

        // One session per cart: a person paying for a handed-off cart finds its session by it.
        builder.Property(session => session.CartId);
        builder.HasIndex(session => session.CartId).IsUnique();
        builder.Property(session => session.OrderId);
        builder.Property(session => session.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(session => session.Culture).HasMaxLength(5).IsRequired();
        builder.Property(session => session.ShippingOptionCode).HasMaxLength(50);
        builder.Property(session => session.ShownQuoteHash).HasMaxLength(128);
        builder.Property(session => session.CreatedAt);
        builder.Property(session => session.UpdatedAt);
        builder.Property(session => session.ExpiresAt);

        // Values the agent said, snapshotted with the session.
        builder.ComplexProperty(session => session.Buyer, buyer => buyer.ToJson("buyer"));
        builder.ComplexProperty(session => session.Destination, destination => destination.ToJson("destination"));
        builder.PrimitiveCollection<List<string>>("coupons").HasColumnType("jsonb");

        builder.Ignore(session => session.Coupons);
        builder.Ignore(session => session.DomainEvents);
    }
}
