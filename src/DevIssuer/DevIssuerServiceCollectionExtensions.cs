using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ElGuerre.Tendero.DevIssuer;

public static class DevIssuerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the development issuer. **It refuses to start outside
    /// Development**, and that is not zeal: an issuer that signs tokens for three
    /// identities with no password is a test fixture with an HTTP surface, and
    /// the way a fixture ends up in production is always the same — nobody posted
    /// the guard.
    ///
    /// The day it grows a user registry or a password change, that means Keycloak
    /// should have arrived. That is its retirement condition.
    /// </summary>
    public static IServiceCollection AddDevIssuer(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The development issuer signs tokens for seeded identities with no credentials. " +
                "It must never be registered outside Development — point JwtBearer at a real issuer instead.");
        }

        // `TryAdd`, the way every context registers it: whoever composes the
        // process may have put a test clock in already, and a second
        // registration would silently win over it.
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<DevIssuerOptions>(configuration.GetSection(DevIssuerOptions.SectionName));
        services.AddSingleton<DevSigningKey>();

        // Singleton for the same reason the key is: a code issued by one request
        // is redeemed by the next, so a scoped store would hand out codes nobody
        // could spend.
        services.AddSingleton<DevAuthorizationCodes>();

        return services;
    }
}
