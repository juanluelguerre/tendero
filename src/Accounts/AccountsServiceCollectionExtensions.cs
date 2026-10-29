using ElGuerre.Tendero.Accounts.Adapters;
using ElGuerre.Tendero.Accounts.PersonalData;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Accounts;

public static class AccountsServiceCollectionExtensions
{
    /// <summary>
    /// Accounts registers no adapters of its own, only its share of a person's data.
    ///
    /// Its two ports — <c>ICustomerRepository</c> and <c>ICustomerDirectory</c>
    /// — are implemented in Persistence, like every other reader and writer, and
    /// the handlers are found by the CQRS assembly scan. The method exists so
    /// the composition root names the context out loud rather than the context
    /// arriving by accident, which is how Carter went missing from Ordering.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        // What Accounts holds about a person, for their download and the removal of
        // their account (P15-9): the address book and the trail, both forgotten.
        services.AddScoped<IPersonalDataHolder, AddressBookPersonalData>();
        services.AddScoped<IPersonalDataHolder, BrowsingHistoryPersonalData>();

        // The customer sources a store's people are imported from (phase 16, S2b):
        // each registers itself keyed (AddShopify), and this lists them.
        services.TryAddScoped<ICustomerSourceRegistry, KeyedCustomerSourceRegistry>();

        // Who a rehearsal would bring, for the rest of a migration rehearsed in the same request (P16-21).
        services.TryAddScoped<IRehearsedCustomers, RehearsedCustomers>();
        return services;
    }

    /// <summary>
    /// Keycloak's adapter of the provisioning port (phase 16, S2b; ADR 0067),
    /// registered only when its service account is configured: without it, an
    /// import brings customers as guests and says so, which is the development
    /// issuer's case on purpose. Composed by the host beside <c>AddAccounts</c>.
    /// </summary>
    public static IServiceCollection AddKeycloakProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(KeycloakProvisioningOptions.Section);
        if (section.Get<KeycloakProvisioningOptions>() is not { IsConfigured: true })
            return services;

        services.Configure<KeycloakProvisioningOptions>(section);
        services.AddHttpClient<IIdentityProvisioner, KeycloakIdentityProvisioner>();
        return services;
    }
}
