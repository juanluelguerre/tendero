using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using ElGuerre.Tendero.Search.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// When a process has a vector layer, and what it says when it has none. The
/// registrations are read as descriptors rather than resolved: resolving the real
/// layer would reach for a model server this suite does not start.
/// </summary>
public sealed class VectorLayerRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_vector_store_without_a_model_server_to_fill_it_is_not_registered(string? embeddings)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:" + QdrantServiceCollectionExtensions.ConnectionName] = "http://127.0.0.1:1" };
        if (embeddings is not null)
            settings[EmbeddingServiceCollectionExtensions.EndpointKey] = embeddings;

        var services = new ServiceCollection().AddQdrantVectorsIfConfigured(Configuration(settings));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IVectorLayer));
    }

    [Fact]
    public void With_a_store_and_a_model_server_the_layer_the_indexer_and_the_search_are_all_registered()
    {
        var services = new ServiceCollection().AddQdrantVectorsIfConfigured(Configuration(new()
        {
            ["ConnectionStrings:" + QdrantServiceCollectionExtensions.ConnectionName] = "http://127.0.0.1:1",
            [EmbeddingServiceCollectionExtensions.EndpointKey] = "http://127.0.0.1:2"
        }));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IVectorLayer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductVectorIndexer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IVectorProductSearch));
    }

    /// <summary>"Not configured" and "unavailable" are different answers: the first stops the background rebuild for good, the second makes it try again.</summary>
    [Fact]
    public async Task A_process_without_a_vector_layer_says_it_has_none_rather_than_that_it_is_down()
    {
        var status = await new NoVectorLayer().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal((false, false), (status.IsConfigured, status.IsAvailable));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
