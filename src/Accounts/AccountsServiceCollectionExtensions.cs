using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Accounts;

public static class AccountsServiceCollectionExtensions
{
    /// <summary>
    /// Accounts has no adapters of its own to register.
    ///
    /// Its two ports — <c>ICustomerRepository</c> and <c>ICustomerDirectory</c>
    /// — are implemented in Persistence, like every other reader and writer, and
    /// the handlers are found by the CQRS assembly scan. The method exists so
    /// the composition root names the context out loud rather than the context
    /// arriving by accident, which is how Carter went missing from Ordering.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services) => services;
}
