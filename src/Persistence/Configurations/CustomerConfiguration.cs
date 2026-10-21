using ElGuerre.Tendero.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", "accounts");

        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();

        // Nullable, because a guest is a Customer with no subject rather than
        // the absence of a customer. UNIQUE where it is present: a subject with
        // two customers is a person with two order histories, and the aggregate
        // refusing a second identity only guards one side of that.
        builder.Property(customer => customer.Subject).HasMaxLength(200);

        builder.HasIndex(customer => customer.Subject)
            .IsUnique()
            .HasFilter("subject IS NOT NULL")
            .HasDatabaseName("ux_customers_subject");

        builder.Property(customer => customer.DisplayName).HasMaxLength(200);
        builder.Property(customer => customer.Culture).HasMaxLength(10).IsRequired();
        builder.Property(customer => customer.Segment).HasMaxLength(50).IsRequired();

        // P15-6: a language chosen in the account, and the one marketing consent,
        // in the customer's own row — three columns, null until the shopper says.
        builder.Property(customer => customer.CultureChosen).HasDefaultValue(false);
        builder.ComplexProperty(customer => customer.Marketing, marketing =>
        {
            marketing.Property(consent => consent.Given).HasColumnName("MarketingGiven");
            marketing.Property(consent => consent.At).HasColumnName("MarketingAt");
            marketing.Property(consent => consent.Wording).HasColumnName("MarketingWording").HasMaxLength(50);
        });
        builder.Ignore(customer => customer.MayBeSentMarketing);

        // P15-9: an erased customer is kept, anonymised, with when.
        builder.Property(customer => customer.ErasedAt);
        builder.Ignore(customer => customer.IsErased);

        builder.Property(customer => customer.CreatedAt);
        builder.Property(customer => customer.UpdatedAt);

        builder.Ignore(customer => customer.DomainEvents);
        builder.Ignore(customer => customer.IsGuest);
    }
}
