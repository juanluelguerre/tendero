using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Knowledge.Chat;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ProposeClaims;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Knowledge;

public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>Where the AppHost hands over the extractor's address (<c>WithReference(extractor)</c>).</summary>
    public const string ExtractorEndpointKey = "services:extractor:http:0";

    /// <summary>Where the AppHost hands over the support scorer's address.</summary>
    public const string SupportEndpointKey = SupportServiceCollectionExtensions.EndpointKey;

    /// <summary>The model the extractor serves, as the claims' provenance records it (ADR 0018).</summary>
    public static readonly ExtractorModel Extractor = new(
        "Qwen/Qwen3.5-4B", "851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a",
        "959b3e3b581774dabfd57dddfb37df22a3609b49b8ea4ae5c082cd756bfd7c59");

    /// <summary>
    /// The claims layer's ports. The rules are always there — the path with no
    /// model (invariant 8). The model extractor and the support scorer are
    /// registered only when the AppHost started them and handed their addresses
    /// over: an address nothing answers would fail every product, no address at
    /// all is the rules alone.
    /// </summary>
    public static IServiceCollection AddKnowledge(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddKeyedSingleton<IClaimExtractor, RulesClaimExtractor>(ClaimExtractors.Rules);

        // What the storefront's assistant may read of the shop's knowledge (phase 12b,
        // ADR 0054): the sheet the product page reads, a shopper's.
        services.AddScoped<IReadTool, Features.ProductSpecs.ProductSpecsTool>();

        if (configuration[ExtractorEndpointKey] is { Length: > 0 } extractor)
        {
            // Traced and metered like every model call (ADR 0053): one row per
            // product it reads, under knowledge.extract.
            services.TryAddSingleton<AIInvocationLog>();
            services.AddChatClient(provider => OpenAICompatibleChat.Create(new Uri(extractor), Extractor.ModelId)
                .Meter("extractor", new ModelProfile { Model = Extractor.ModelId, ContextTokens = 4096 }, provider));
            services.AddKeyedSingleton<IClaimExtractor>(ClaimExtractors.Chat,
                (provider, _) => new ChatClaimExtractor(provider.GetRequiredService<IChatClient>()));
        }

        // The scorer is the model port's to register (phase 12b): the shopper's
        // assistant holds its answers to it too.
        services.AddSupportScorer(configuration);

        services.AddSingleton(Extractor);
        return services;
    }

    /// <summary>
    /// The queue that proposes claims, for the worker only: the API records
    /// nothing and proposes nothing, it reads what was approved.
    /// </summary>
    public static IServiceCollection AddClaimProposer(this IServiceCollection services)
    {
        services.AddHostedService<ClaimProposalWorker>();
        return services;
    }
}

/// <summary>The extractor's model, pinned: repository, revision and the SHA-256 of the file served.</summary>
public sealed record ExtractorModel(string ModelId, string Revision, string FileSha256);
