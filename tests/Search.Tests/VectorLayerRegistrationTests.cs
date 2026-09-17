using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Answers;
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

    /// <summary>Layer B is off on a fresh clone: no calibration file is committed, and no model server was started.</summary>
    [Fact]
    public void Without_an_embeddings_endpoint_the_query_scope_is_the_stand_in()
    {
        var services = new ServiceCollection().AddHybridSearch(Configuration([]));

        Assert.Equal(typeof(NoQueryScope), QueryScopeImplementation(services));
    }

    [Fact]
    public void A_calibration_without_a_generator_to_embed_with_leaves_the_stand_in()
    {
        var services = new ServiceCollection();
        services.AddQueryScope(Calibration);
        services.AddSearchStrategies();

        Assert.Equal(typeof(NoQueryScope), QueryScopeImplementation(services));
    }

    /// <summary>Any other service is not a generator: the check reads the generator's type, not "something is registered".</summary>
    [Fact]
    public void A_calibration_beside_services_that_are_not_a_generator_leaves_the_stand_in()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueryScope(Calibration);
        services.AddSearchStrategies();

        Assert.Equal(typeof(NoQueryScope), QueryScopeImplementation(services));
    }

    [Fact]
    public void A_calibration_and_a_generator_register_the_anchor_classifier()
    {
        var services = new ServiceCollection();
        services.AddProductEmbeddings(new Uri("http://127.0.0.1:2"));
        services.AddQueryScope(Calibration);
        services.AddSearchStrategies();

        Assert.Equal(typeof(AnchorQueryScope), QueryScopeImplementation(services));
    }

    [Fact]
    public void No_calibration_leaves_the_stand_in_even_with_a_generator()
    {
        var services = new ServiceCollection();
        services.AddProductEmbeddings(new Uri("http://127.0.0.1:2"));
        services.AddQueryScope(calibration: null);
        services.AddSearchStrategies();

        Assert.Equal(typeof(NoQueryScope), QueryScopeImplementation(services));
    }

    /// <summary>Nothing is committed yet, so nothing is compiled in (Task 12 measures and writes it).</summary>
    [Fact]
    public void No_calibration_is_embedded_until_one_is_measured() =>
        Assert.Null(QueryScopeCalibration.LoadEmbedded());

    private static readonly QueryScopeCalibration Calibration = new(0.05, 23, "categories", "anchors");

    /// <summary>
    /// Resolved rather than read off a descriptor, because the classifier is
    /// built by a factory; building it reaches for nothing over the network — the
    /// anchors are embedded on first use, never on construction.
    /// </summary>
    private static Type QueryScopeImplementation(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<ICategoryReader>(new NoCategories());

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IQueryScope>().GetType();
    }

    private sealed class NoCategories : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(CategoryTree.Empty);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
