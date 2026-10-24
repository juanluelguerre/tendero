using ElGuerre.Tendero.Accounts.Ports;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests.Sources;

/// <summary>
/// The customers port's executable contract (phase 16, S2b): every customer source
/// inherits this class and passes the same suite. Linked as a file into each test
/// project that has one (Accounts.Tests for its fake, Shopify.Tests for Shopify),
/// like the catalogue's and the stock's suites.
/// </summary>
public abstract class CustomerSourceConnectorContractTests
{
    protected abstract ICustomerSourceConnector CreateConnector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<SourceCustomer>> All()
    {
        var customers = new List<SourceCustomer>();
        await foreach (var customer in CreateConnector().StreamCustomersAsync(Ct))
            customers.Add(customer);
        return customers;
    }

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    /// <summary>Each customer once, by an id of the source's: what a re-import and their orders find them by.</summary>
    [Fact]
    public async Task Customers_have_an_id_and_no_id_twice()
    {
        var customers = await All();

        Assert.NotEmpty(customers);
        Assert.All(customers, customer => Assert.False(string.IsNullOrWhiteSpace(customer.ExternalId)));
        Assert.Equal(customers.Count, customers.Select(customer => customer.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>At most one default address each: the default is a choice, not a list.</summary>
    [Fact]
    public async Task A_customer_has_at_most_one_default_address() =>
        Assert.All(await All(), customer => Assert.True(customer.Addresses.Count(address => address.IsDefault) <= 1, customer.ExternalId));
}
