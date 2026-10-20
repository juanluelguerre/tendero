using ElGuerre.Tendero.Accounts.PersonalData;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
