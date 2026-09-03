using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A cart and its lines.
///
/// **The lines are jsonb, and the roadmap said a table.** Its reason was that
/// "UCP mutates single lines and the agent activity panel queries them", and
/// neither survives contact: mutating one line goes through the aggregate like
/// every other change, and the activity panel reads the audit table, not the
/// cart. ADR 0008's actual criterion is whether a value is queried on its own,
/// and a cart line never is — it is only ever read with the cart that owns it,
/// exactly like an order line. Building the table first is what phase 2 already
/// did and undid.
///
/// What IS a real index is the token: it is the only way an anonymous visitor
/// finds their cart, so it is on the hot path of every storefront request.
/// </summary>
internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts", "ordering");

        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id)
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();

        // Nullable: guests are first class, and a cart exists before anybody has
        // said who they are.
        builder.Property(cart => cart.CustomerId)
            .HasConversion(
                id => id!.Value.Value,
                value => new CustomerId(value));

        // 32 bytes base64url. Unique because it is a credential: two carts with
        // the same token would be a lookup that returns somebody else's basket.
        builder.Property(cart => cart.Token).HasMaxLength(64).IsRequired();
        builder.HasIndex(cart => cart.Token).IsUnique();

        builder.Property(cart => cart.Culture).HasMaxLength(5).IsRequired();
        builder.Property(cart => cart.Currency).HasMaxLength(3).IsRequired();

        builder.Property(cart => cart.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(cart => cart.CreatedAt);
        builder.Property(cart => cart.UpdatedAt);
        builder.Property(cart => cart.ExpiresAt);

        // The sweep that reclaims stock from abandoned carts reads exactly this:
        // open, and past its expiry. It does not exist yet — the standing
        // backlog says so — and the index is one line now rather than a
        // migration later.
        builder.HasIndex(cart => new { cart.Status, cart.ExpiresAt });

        builder.ComplexCollection<List<CartLine>, CartLine>("_lines", line =>
        {
            line.Property(l => l.ProductId).HasConversion(id => id.Value, value => new ProductId(value));
            line.Property(l => l.VariantId).HasConversion(id => id.Value, value => new VariantId(value));
            line.ToJson("lines");
        });

        builder.Ignore(cart => cart.Lines);
        builder.Ignore(cart => cart.ItemCount);
        builder.Ignore(cart => cart.IsEmpty);
        builder.Ignore(cart => cart.DomainEvents);
    }
}
