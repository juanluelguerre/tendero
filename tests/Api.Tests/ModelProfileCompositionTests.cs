using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Steward's local profile exists only where its container runs (ADR 0053): the
/// AppHost hands the API the address with Tendero__Steward=true, and without it the
/// API registers no model profile — never a client for a container nobody started.
/// </summary>
public sealed class ModelProfileCompositionTests
{
    [Fact]
    public void Without_the_stewards_container_there_is_no_model_profile()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        Assert.Null(factory.Services.GetKeyedService<IChatClient>("local"));
    }

    /// <summary>A hosted profile does not wait for the local container (final
    /// review, I-1): with a key and no Tendero__Steward, it is still a client.</summary>
    [Fact]
    public void A_hosted_profile_is_a_client_without_the_stewards_container()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder
            .UseSetting("Steward:Models:gemini:Endpoint", "https://generativelanguage.googleapis.com/v1beta/openai/")
            .UseSetting("Steward:Models:gemini:Model", "gemini-2.5-flash")
            .UseSetting("Steward:Models:gemini:Auth", "Bearer")
            .UseSetting("Steward:Models:gemini:ApiKey", "secret"));
        _ = factory.CreateClient();

        Assert.Null(factory.Services.GetKeyedService<IChatClient>("local"));
        Assert.NotNull(factory.Services.GetKeyedService<IChatClient>("gemini"));
    }

    /// <summary>Steward's local model is the extractor's GGUF: its rows carry the
    /// same model id, or "tokens per model" would split one model in two (M-5).</summary>
    [Fact]
    public void The_local_profile_names_its_model_as_the_extractor_does()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        Assert.Equal(
            ElGuerre.Tendero.Knowledge.KnowledgeServiceCollectionExtensions.Extractor.ModelId,
            factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Steward:Models:local:Model"]);
    }

    [Fact]
    public void With_its_address_the_local_profile_is_a_client()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder =>
            builder.UseSetting("Steward:Models:local:Endpoint", "http://localhost:38085/v1/"));
        _ = factory.CreateClient();

        Assert.NotNull(factory.Services.GetKeyedService<IChatClient>("local"));
    }
}
