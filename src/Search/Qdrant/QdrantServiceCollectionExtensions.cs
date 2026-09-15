using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Qdrant.Client;

// The namespace is the fence, as it is for Elasticsearch and the OpenAI SDK: an
// architecture rule keeps Qdrant.Client inside Tendero.Search.Qdrant.
namespace ElGuerre.Tendero.Search.Qdrant;

public static class QdrantServiceCollectionExtensions
{
    /// <summary>The connection name the AppHost's <c>WithReference(qdrant)</c> writes.</summary>
    public const string ConnectionName = "qdrant";

    /// <summary>
    /// The vector store, registered only when there is one to reach AND a model
    /// server to fill it: a collection with no embeddings is not a vector layer.
    /// </summary>
    public static IServiceCollection AddQdrantVectorsIfConfigured(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetConnectionString(ConnectionName) is not { Length: > 0 } connection
            || configuration[EmbeddingServiceCollectionExtensions.EndpointKey] is not { Length: > 0 })
        {
            return services;
        }

        var (endpoint, key) = QdrantConnection.Parse(connection);
        return services.AddQdrantVectors(endpoint, key);
    }

    /// <summary>
    /// Qdrant over gRPC. Needs <see cref="EmbeddingServiceCollectionExtensions.AddProductEmbeddings"/>,
    /// which registers the generator and the model this layer checks against.
    /// </summary>
    public static IServiceCollection AddQdrantVectors(this IServiceCollection services, Uri endpoint, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // Five seconds per call: a stopped container refuses at once, and this is
        // for the one that accepts a connection and never answers. The client does
        // not connect on construction, so a process with no Qdrant still starts.
        services.AddSingleton(provider => new QdrantClient(
            endpoint, apiKey, TimeSpan.FromSeconds(5), provider.GetService<ILoggerFactory>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<QdrantVectorLayer>();
        services.AddSingleton<IVectorLayer>(provider => provider.GetRequiredService<QdrantVectorLayer>());

        services.AddScoped<QdrantProductVectors>();
        services.AddScoped<IProductVectorIndexer>(provider => provider.GetRequiredService<QdrantProductVectors>());
        services.AddScoped<IVectorProductSearch>(provider => provider.GetRequiredService<QdrantProductVectors>());

        return services;
    }
}

/// <summary>
/// Aspire's Qdrant connection string, <c>Endpoint=http://host:port;Key=…</c>, or a
/// bare URL for a Qdrant nobody put a key on.
/// </summary>
internal static class QdrantConnection
{
    public static (Uri Endpoint, string? Key) Parse(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (Uri.TryCreate(connectionString.Trim(), UriKind.Absolute, out var bare) && bare.Scheme is "http" or "https")
            return (bare, null);

        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        // The message names what is missing and never repeats the string: it
        // carries the API key.
        if (!parts.TryGetValue("Endpoint", out var value) || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException(
                $"ConnectionStrings:{QdrantServiceCollectionExtensions.ConnectionName} has no absolute Endpoint.");

        return (endpoint, parts.GetValueOrDefault("Key") is { Length: > 0 } key ? key : null);
    }
}
