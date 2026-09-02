using Microsoft.Extensions.Configuration;

namespace ElGuerre.Tendero.ServiceDefaults;

public static class ConfigurationExtensions
{
    /// <summary>
    /// A missing connection string is a startup failure, not a null that travels
    /// as far as the first query. The message says where it should have come
    /// from, because the normal case is having launched the service outside the
    /// AppHost.
    /// </summary>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException(
            $"Connection string '{name}' is missing. The AppHost injects it as " +
            $"ConnectionStrings__{name}; running this service on its own means setting it yourself.");
}
