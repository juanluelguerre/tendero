using System.Text;
using ElGuerre.Tendero.Accounts;
using ElGuerre.Tendero.Exports;
using ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// Orders for the accountant after a customer's erasure (phase 16, E-4; spec, *Who may
/// export what*): an erased customer's orders are kept for the six years the Commercial
/// Code asks, blocked (P15-9), and the accountant's export is the one use they are kept
/// for. So it still carries the order — the billing name and country an invoice keeps —
/// and never what the order froze besides it: the buyer's email and phone.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrdersForTheBooksTests(PostgresFixture postgres)
{
    private const string Email = "lucia.martin@example.com";
    private const string Phone = "+34600111201";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task An_erased_customers_order_leaves_with_what_its_invoice_keeps_and_nothing_else()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("orders_for_the_books");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var customer = CustomerId.New();
        var placed = new TestClock(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var order = OrderBuilder.Default()
            .At(placed)
            .ForCustomer(customer)
            .WithContactEmail(Email)
            .ShippedTo(Address.Create("Lucía Martín", "Calle de Hortaleza 48", null, "Madrid", "M", "28004", "ES", Phone))
            .InStatus(OrderStatus.Delivered);
        await using (var context = factory.Create())
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync(ct);
        }

        await using var provider = Compose(factory.ConnectionString);

        // The account removed as YourData removes it: every context forgets, or keeps what the law keeps.
        await using (var scope = provider.CreateAsyncScope())
        {
            foreach (var holder in scope.ServiceProvider.GetServices<IPersonalDataHolder>())
                await holder.EraseAsync(customer, ct);
        }

        ExportFile file;
        await using (var scope = provider.CreateAsyncScope())
            file = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ExportOrdersCommand(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "csv", "es"), ct);

        var text = Encoding.UTF8.GetString(file.Content);
        Assert.Contains(order.Id.ToString().Replace("-", "", StringComparison.Ordinal)[^8..], text, StringComparison.Ordinal);
        Assert.Contains("Lucía Martín", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Phone, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hortaleza", text, StringComparison.Ordinal);
    }

    private static ServiceProvider Compose(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ExportOrdersCommand).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddAccounts();
        services.AddOrdering(configuration);
        services.AddExports(configuration);
        services.AddSingleton<IPrincipalAccessor>(new SystemPrincipalAccessor());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
