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

        // Live unless the credential that placed it was a test one (ADR 0038).
        // Every order before the column existed was live, hence the default.
        builder.Property(o => o.ContactEmail).HasMaxLength(320);

        // Counted per agent over the last hour by the shop's own rules (P11-6),
        // hence the index that starts with it.
        builder.Property(o => o.PlacedBy).HasMaxLength(100);
        builder.HasIndex(o => new { o.PlacedBy, o.CreatedAt });

        builder.Property(o => o.Mode)
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasDefaultValue(OrderMode.Live)
            .HasSentinel((OrderMode)(-1));

        builder.Property(o => o.CreatedAt);
        builder.Property(o => o.UpdatedAt);

        // The facts the dashboard reads, recorded when they happen (R-9, D-7).
        // Null on an order older than them, and never back-filled: an order whose
        // channel nobody recorded is counted apart, not guessed as the web.
        builder.Property(o => o.Channel).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(o => new { o.Channel, o.CreatedAt });
        builder.Property(o => o.ConfirmedAt);
        builder.Property(o => o.ShippedAt);

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

        // Null for every order no mandate stood behind; jsonb like the payment,
        // read with the order and never queried on its own (P11-5).
        builder.ComplexProperty(o => o.Mandate, mandate => mandate.ToJson("mandate"));

        // Null for every order placed here; jsonb like the mandate (ADR 0066). An
        // imported order is found again by its idempotency key, import:<source>:<id>,
        // which the unique index above already serves.
        builder.ComplexProperty(o => o.ImportedFrom, origin => origin.ToJson("imported_from"));

        // Null for every order a channel did not take; jsonb like the import's (ADR 0070),
        // found again by its key, channel:<channel>:<id>, which the same index serves.
        builder.ComplexProperty(o => o.ReceivedFrom, origin => origin.ToJson("received_from"));

        // The ledger's reason, when a channel sold what could not be held. A column of
        // its own: the backoffice lists the orders to act on by it.
        builder.Property(o => o.Oversold).HasMaxLength(500);

        builder.Ignore(o => o.LinesTotal);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}
