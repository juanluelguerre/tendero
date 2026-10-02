using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// What the worker gets, by what the AppHost handed it (ADR 0018, invariant 8):
/// the rules always; the model and the scorer only with an address to reach.
/// </summary>
public sealed class KnowledgeRegistrationTests
{
    private static ServiceProvider Compose(params (string Key, string Value)[] settings) =>
        new ServiceCollection()
            .AddKnowledge(new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build())
            .BuildServiceProvider();

    [Fact]
    public void Without_the_models_the_rules_are_the_only_extractor_and_there_is_no_scorer()
    {
        using var provider = Compose();

        Assert.IsType<RulesClaimExtractor>(provider.GetRequiredKeyedService<IClaimExtractor>(ClaimExtractors.Rules));
        Assert.Null(provider.GetKeyedService<IClaimExtractor>(ClaimExtractors.Chat));
        Assert.Null(provider.GetService<ISupportScorer>());
        // The model a proposal records as its provenance is known either way.
        Assert.Equal("Qwen/Qwen3.5-4B", provider.GetRequiredService<ExtractorModel>().ModelId);
    }

    [Fact]
    public void With_the_models_started_the_worker_gets_the_extractor_and_the_scorer()
    {
        using var provider = Compose(
            (KnowledgeServiceCollectionExtensions.ExtractorEndpointKey, "http://localhost:38083"),
            (KnowledgeServiceCollectionExtensions.SupportEndpointKey, "http://localhost:38084"));

        Assert.IsType<ChatClaimExtractor>(provider.GetRequiredKeyedService<IClaimExtractor>(ClaimExtractors.Chat));
        Assert.IsType<TeiNliSupportScorer>(provider.GetRequiredService<ISupportScorer>());
        Assert.IsType<RulesClaimExtractor>(provider.GetRequiredKeyedService<IClaimExtractor>(ClaimExtractors.Rules));
    }

    /// <summary>The extractor's calls are model calls like Steward's: each leaves a
    /// row in telemetry.ai_invocations (ADR 0053).</summary>
    [Fact]
    public void The_extractors_model_is_metered()
    {
        using var provider = Compose((KnowledgeServiceCollectionExtensions.ExtractorEndpointKey, "http://localhost:38083"));

        Assert.NotNull(provider.GetRequiredService<IChatClient>().GetService<MeteringChatClient>());
    }
}
