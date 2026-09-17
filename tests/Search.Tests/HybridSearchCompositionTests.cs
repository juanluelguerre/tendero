using ElGuerre.Tendero.Search.Answers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Reranking;
using ElGuerre.Tendero.Search.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What <c>AddHybridSearch</c> composes, read from the descriptors. The API's and
/// the worker's own composition tests see this too, but from projects Stryker does
/// not run against Search: at the 2026-09-17 phase close every line of this
/// registration could be deleted with nothing red here.
/// </summary>
public sealed class HybridSearchCompositionTests
{
    [Fact]
    public void A_fresh_clone_gets_both_strategies_the_answerer_and_named_stand_ins()
    {
        var services = new ServiceCollection().AddHybridSearch(Configuration([]));

        Assert.Contains(services, descriptor => IsKeyed(descriptor, SearchStrategies.Lexical, typeof(LexicalSearchStrategy)));
        Assert.Contains(services, descriptor => IsKeyed(descriptor, SearchStrategies.Hybrid, typeof(HybridSearchStrategy)));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISearchAnswerer) && descriptor.ImplementationType == typeof(SearchAnswerer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IReranker) && descriptor.ImplementationType == typeof(NoReranker));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IVectorLayer));
    }

    [Fact]
    public void A_reranker_the_apphost_started_is_the_reranker_searches_use()
    {
        var services = new ServiceCollection().AddHybridSearch(Configuration(new()
        {
            [RerankerServiceCollectionExtensions.EndpointKey] = "http://127.0.0.1:3"
        }));

        var reranker = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IReranker));
        Assert.IsType<TextEmbeddingsInferenceReranker>(reranker.ImplementationInstance);
    }

    [Fact]
    public void The_hybrid_settings_are_read_from_configuration()
    {
        var services = new ServiceCollection().AddHybridSearch(Configuration(new()
        {
            [HybridSearchOptions.Section + ":VectorDepth"] = "7"
        }));

        using var provider = services.BuildServiceProvider();

        Assert.Equal(7, provider.GetRequiredService<IOptions<HybridSearchOptions>>().Value.VectorDepth);
    }

    /// <summary>The strategies alone still resolve their settings: a test host that composes only them gets the defaults.</summary>
    [Fact]
    public void The_strategies_alone_bring_their_default_settings()
    {
        using var provider = new ServiceCollection().AddSearchStrategies().BuildServiceProvider();

        Assert.Equal(new HybridSearchOptions(), provider.GetRequiredService<IOptions<HybridSearchOptions>>().Value);
    }

    private static bool IsKeyed(ServiceDescriptor descriptor, string key, Type implementation) =>
        descriptor.ServiceType == typeof(ISearchStrategy)
        && descriptor.IsKeyedService
        && Equals(descriptor.ServiceKey, key)
        && descriptor.KeyedImplementationType == implementation;

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
