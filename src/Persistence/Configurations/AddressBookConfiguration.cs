using ElGuerre.Tendero.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>
/// A customer's address book (P15-2), in Accounts' schema: <c>accounts.address_books</c>,
/// one row per customer, the entries as one jsonb list — always read and written
/// with their book, and ten at most — and the two defaults as columns beside it.
/// </summary>
internal sealed class AddressBookConfiguration : IEntityTypeConfiguration<AddressBook>
{
    public void Configure(EntityTypeBuilder<AddressBook> builder)
    {
        builder.ToTable("AddressBooks", "accounts");

        // The customer is the key: a book has no identity of its own.
        builder.HasKey(book => book.CustomerId);
        builder.Property(book => book.CustomerId).ValueGeneratedNever();

        builder.Property(book => book.DeliveryDefault);
        builder.Property(book => book.BillingDefault);
        builder.Property(book => book.CreatedAt);
        builder.Property(book => book.UpdatedAt);

        builder.Property<List<AddressEntry>>("entries")
            .HasColumnName("Entries")
            .HasColumnType("jsonb")
            .HasConversion(Jsonb.AddressEntriesConverter, Jsonb.AddressEntriesComparer)
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(book => book.Entries);
        builder.Ignore(book => book.IsFull);
        builder.Ignore(book => book.DomainEvents);
    }
}
