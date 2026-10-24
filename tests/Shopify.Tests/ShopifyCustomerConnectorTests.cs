using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Accounts.Tests.Sources;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>The Shopify adapter of the customers port passes the contract every customer source does.</summary>
public sealed class ShopifyCustomerConnectorContractTests : CustomerSourceConnectorContractTests
{
    protected override ICustomerSourceConnector CreateConnector() => ShopifyCustomerConnectorTests.AConnector().Connector;
}

/// <summary>
/// A store's customers in the shop's words (phase 16, S2b, P16-17): their default
/// email, name and language, their addresses with the store's default marked, and
/// their email-marketing consent as the store dated it — given when subscribed,
/// withdrawn when unsubscribed, and nothing recorded otherwise. Read from the
/// fields current in the pinned version (<c>defaultEmailAddress</c>,
/// <c>addressesV2</c>), not the deprecated ones.
/// </summary>
public sealed class ShopifyCustomerConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static (ShopifyCustomerConnector Connector, FakeShopify Store) AConnector()
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);
        return (new ShopifyCustomerConnector(admin), store);
    }

    private static async Task<List<SourceCustomer>> Customers(ShopifyCustomerConnector connector)
    {
        var customers = new List<SourceCustomer>();
        await foreach (var customer in connector.StreamCustomersAsync(Ct))
            customers.Add(customer);
        return customers;
    }

    [Fact]
    public async Task A_customer_comes_with_their_email_name_language_and_addresses_the_default_marked()
    {
        var (connector, store) = AConnector();

        var ana = (await Customers(connector))[0];

        Assert.Equal(("6001", "ana@example.com", "Ana", "Ruiz", "es"), (ana.ExternalId, ana.Email, ana.FirstName, ana.LastName, ana.Culture));
        Assert.Equal(
            [("Barcelona", false), ("Madrid", true)],
            ana.Addresses.Select(address => (address.City, address.IsDefault)));
        Assert.Equal(
            new SourceAddress("Ana Ruiz", "Calle Mayor 1", "2º B", "Madrid", "M", "28013", "ES", "+34600000001", IsDefault: true),
            ana.Addresses[1]);
        Assert.Contains("defaultEmailAddress", store.LastBulkQuery, StringComparison.Ordinal);
        Assert.Contains("addressesV2", store.LastBulkQuery, StringComparison.Ordinal);
    }

    /// <summary>Subscribed is consent given and unsubscribed is consent withdrawn, each dated; anything else records none.</summary>
    [Fact]
    public async Task Consent_is_what_the_store_recorded_and_when()
    {
        var customers = await Customers(AConnector().Connector);

        Assert.Equal(new SourceConsent(true, new DateTimeOffset(2025, 11, 3, 18, 0, 0, TimeSpan.Zero)), customers[0].Marketing);
        Assert.Equal(new SourceConsent(false, new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)), customers[1].Marketing);
        Assert.Null(customers[2].Marketing);
    }

    /// <summary>The store's locale in the shop's languages: <c>en-GB</c> is English, French is none of ours.</summary>
    [Fact]
    public async Task A_language_the_shop_does_not_speak_is_no_language()
    {
        var customers = await Customers(AConnector().Connector);

        Assert.Equal(["es", "en", null, "es"], customers.Select(customer => customer.Culture));
    }

    /// <summary>A customer with no email comes as it is: the slice says it cannot import them.</summary>
    [Fact]
    public async Task A_customer_without_an_email_comes_without_one()
    {
        var customers = await Customers(AConnector().Connector);

        Assert.Null(customers[3].Email);
        Assert.Empty(customers[3].Addresses);
    }
}
