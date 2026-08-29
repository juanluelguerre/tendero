using Microsoft.Extensions.Configuration;

namespace Tendero.ServiceDefaults;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Una cadena de conexión que falta es un fallo de arranque, no un null que
    /// viaja hasta el primer query. El mensaje dice de dónde debería venir,
    /// porque el caso normal es haber lanzado el servicio fuera del AppHost.
    /// </summary>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException(
            $"Connection string '{name}' is missing. The AppHost injects it as " +
            $"ConnectionStrings__{name}; running this service on its own means setting it yourself.");
}
