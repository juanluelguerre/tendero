using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", "ordering");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        // Retrying checkout with the same key must not create a second order,
        // and with agents retries are the normal case, not the rare one.
        builder.Property(o => o.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        builder.Property(o => o.Culture).HasMaxLength(5).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.CreatedAt);
        builder.Property(o => o.UpdatedAt);

        // A snapshot, not a live FK to the product: the name already resolved in
        // the buyer's culture and the price frozen (ADR 0002). They are never
        // queried on their own from the domain, so they travel with the order in
        // a JSON column.
        builder.ComplexCollection<List<OrderLine>, OrderLine>("lines", line =>
        {
            // The variant travels on the line because what gets bought is a
            // variant (ADR 0015): without it the order does not know which size
            // was shipped, and inventory, which decrements by SKU, has nothing
            // to work with.
            line.Property(l => l.UnitPrice).HasConversion(Jsonb.MoneyAsTextConverter);
            line.Ignore(l => l.Total);
            line.ToJson("lines");
        });

        builder.Ignore(o => o.Lines);

        // Addresses are VALUES and each order keeps its own copy (ADR 0002).
        // Two JSON columns and not a shared address table: a table would let
        // editing your address next year rewrite where last month's parcel went,
        // which is the exact failure the snapshot rule exists to prevent.
        builder.ComplexProperty(o => o.ShippingAddress, address => address.ToJson("shipping_address"));
        builder.ComplexProperty(o => o.BillingAddress, address => address.ToJson("billing_address"));

        builder.ComplexProperty(o => o.Shipping, shipping =>
        {
            shipping.Property(s => s.Amount).HasConversion(Jsonb.MoneyAsTextConverter);
            shipping.ToJson("shipping");
        });

        builder.ComplexProperty(o => o.Quote, quote => quote.ToJson("quote"));

        // The frozen totals, in one column. Five separate decimal columns would
        // read better in psql and would let four of them be updated without the
        // fifth, which is how a total stops matching its parts.
        builder.ComplexProperty(o => o.Totals, totals =>
        {
            totals.Property(t => t.Subtotal).HasConversion(Jsonb.MoneyAsTextConverter);
            totals.Property(t => t.DiscountTotal).HasConversion(Jsonb.MoneyAsTextConverter);
            totals.Property(t => t.Shipping).HasConversion(Jsonb.MoneyAsTextConverter);
            totals.Property(t => t.TaxTotal).HasConversion(Jsonb.MoneyAsTextConverter);
            totals.Property(t => t.Total).HasConversion(Jsonb.MoneyAsTextConverter);
            totals.ToJson("totals");
        });

        builder.ComplexCollection<List<OrderDiscount>, OrderDiscount>("discounts", discount =>
        {
            discount.Property(d => d.Amount).HasConversion(Jsonb.MoneyAsTextConverter);
            discount.ToJson("discounts");
        });

        builder.ComplexCollection<List<OrderTax>, OrderTax>("taxes", tax =>
        {
            tax.Property(t => t.Base).HasConversion(Jsonb.MoneyAsTextConverter);
            tax.Property(t => t.Amount).HasConversion(Jsonb.MoneyAsTextConverter);
            tax.ToJson("taxes");
        });

        builder.Ignore(o => o.Discounts);
        builder.Ignore(o => o.Taxes);

        // Nullable: an order exists before anybody has paid for it, and the
        // absence is what tells a return there is nothing to refund.
        builder.ComplexProperty(o => o.Payment, payment => payment.ToJson("payment"));

        // Nullable for the same reason and jsonb for the same one: it is read
        // with the order that owns it and nothing queries it on its own. The day
        // a shopkeeper filters "cancelled for stock", ADR 0008's criterion sends
        // the code to a column of its own — and only the code.
        builder.ComplexProperty(o => o.Stop, stop => stop.ToJson("stop"));

        builder.Ignore(o => o.LinesTotal);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}
