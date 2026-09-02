using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElGuerre.Tendero.DevIssuer;

public static class DevIssuerServiceCollectionExtensions
{
    /// <summary>
    /// Registra el emisor de desarrollo. **Se niega a arrancar fuera de
    /// Development**, y eso no es celo: un emisor que firma tokens para tres
    /// identidades sin contraseña es un test fixture con superficie HTTP, y la
    /// forma en que un fixture acaba en producción es siempre la misma — nadie
    /// puso el guardia.
    ///
    /// El día que crezca un registro de usuarios o un cambio de contraseña, es
    /// que había que haber traído Keycloak. Esa es su condición de retirada.
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

        services.Configure<DevIssuerOptions>(configuration.GetSection(DevIssuerOptions.SectionName));
        services.AddSingleton<DevSigningKey>();

        return services;
    }
}
