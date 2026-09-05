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

        builder.Property(customer => customer.CreatedAt);
        builder.Property(customer => customer.UpdatedAt);

        builder.Ignore(customer => customer.DomainEvents);
        builder.Ignore(customer => customer.IsGuest);
    }
}
