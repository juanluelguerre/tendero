using System.ClientModel;
using System.ClientModel.Primitives;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Embeddings;

// The namespace is the fence: an architecture rule keeps the OpenAI SDK inside
// Tendero.Search.Embeddings, the way the Elastic client is kept inside
// Tendero.Search.Elasticsearch. Everything else sees IEmbeddingGenerator only.
namespace ElGuerre.Tendero.Search.Embeddings;

public static class EmbeddingServiceCollectionExtensions
{
    /// <summary>
    /// Where the AppHost hands over the model server's address: the service
    /// discovery entry Aspire writes for <c>WithReference(embeddings)</c>.
    /// </summary>
    public const string EndpointKey = "services:embeddings:http:0";

    /// <summary>
    /// The vector layer's embedding generator, registered only when there is a
    /// model server to reach.
    ///
    /// **Optional on purpose** (invariant 8). A host started without the vector
    /// layer gets no generator at all rather than one that throws on first use,
    /// so whatever asks for it has to be written to cope with its absence — which
    /// is the degradation the search strategies must prove, not a failure they
    /// discover in production.
    /// </summary>
    public static IServiceCollection AddProductEmbeddingsIfConfigured(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[EndpointKey] is { Length: > 0 } endpoint
            ? services.AddProductEmbeddings(new Uri(endpoint))
            : services;
    }

    /// <summary>
    /// <c>IEmbeddingGenerator</c> over Text Embeddings Inference, through
    /// Microsoft.Extensions.AI's OpenAI adapter (ADR 0005: the MEAI abstraction
    /// IS the port, so there is no interface of ours in front of it).
    ///
    /// Traced with the OpenTelemetry GenAI conventions under Search's own source,
    /// which the composition root already registers — a span nobody registered
    /// would be a span nobody sees.
    /// </summary>
    public static IServiceCollection AddProductEmbeddings(
        this IServiceCollection services, Uri endpoint, EmbeddingModelDescriptor? model = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var descriptor = model ?? EmbeddingModelDescriptor.BgeM3;

        services.AddSingleton(descriptor);
        services.AddEmbeddingGenerator(_ => TextEmbeddingsInference.CreateGenerator(endpoint, descriptor))
            .UseOpenTelemetry(sourceName: TelemetrySources.Search);

        return services;
    }
}

/// <summary>
/// The one place that knows the model server speaks OpenAI's embeddings API.
/// </summary>
internal static class TextEmbeddingsInference
{
    // TEI checks no key, and the OpenAI SDK refuses to build a client without
    // one. The value says so, rather than an empty string that reads like a
    // secret somebody forgot to set.
    private const string UnusedApiKey = "text-embeddings-inference-checks-no-key";

    /// <param name="transport">
    /// Replaced only by tests, which capture the request the SDK really sends —
    /// the only way to know it stays inside what TEI accepts.
    /// </param>
    internal static IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(
        Uri endpoint, EmbeddingModelDescriptor model, PipelineTransport? transport = null)
    {
        // TEI serves the OpenAI-compatible routes under /v1, and the SDK appends
        // "/embeddings" to whatever endpoint it is given.
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint, "v1") };
        if (transport is not null)
            options.Transport = transport;

        // No default dimensions are passed: the SDK would send them, and a model
        // that is not Matryoshka-trained has no business being asked to truncate.
        // The descriptor's length is checked against the vectors instead.
        return new EmbeddingClient(model.ModelId, new ApiKeyCredential(UnusedApiKey), options)
            .AsIEmbeddingGenerator();
    }
}
