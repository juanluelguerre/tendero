using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class ModelProfileRegistrationTests
{
    [Fact]
    public void Each_profile_is_a_keyed_client_that_meters()
    {
        using var provider = Compose(new()
        {
            ["Steward:Models:local:Endpoint"] = "http://localhost:38085/v1/",
            ["Steward:Models:local:Model"] = "qwen3.5-4b",
            ["Steward:Models:gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
            ["Steward:Models:gemini:Model"] = "gemini-2.5-flash",
            ["Steward:Models:gemini:Auth"] = "Bearer",
            ["Steward:Models:gemini:ApiKey"] = "secret",
        });

        Assert.NotNull(provider.GetRequiredKeyedService<IChatClient>("local").GetService<MeteringChatClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IChatClient>("gemini").GetService<MeteringChatClient>());
        Assert.Null(provider.GetKeyedService<IChatClient>("deepseek"));
    }

    /// <summary>The order the spec gives: tracing outside metering, so the metered
    /// row's trace is the GenAI span's (M-9).</summary>
    [Fact]
    public void Tracing_is_outside_metering()
    {
        using var provider = Compose(new()
        {
            ["Steward:Models:local:Endpoint"] = "http://localhost:38085/v1/",
            ["Steward:Models:local:Model"] = "Qwen/Qwen3.5-4B",
        });

        var client = provider.GetRequiredKeyedService<IChatClient>("local");

        Assert.IsType<OpenTelemetryChatClient>(client);
        Assert.IsType<MeteringChatClient>(((DelegatingChatClient)client).GetService<MeteringChatClient>());
    }

    [Fact]
    public void A_profile_that_cannot_be_called_stops_the_host_naming_it()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Compose(new()
        {
            ["Steward:Models:gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
            ["Steward:Models:gemini:Model"] = "gemini-2.5-flash",
            ["Steward:Models:gemini:Auth"] = "Bearer",
        }));

        Assert.Contains("gemini", failure.Message);
        Assert.Contains("ApiKey", failure.Message);
    }

    /// <summary>Each profile is registered on its own: a hosted one is a client
    /// whether or not the local one has an address (final review, I-1).</summary>
    [Fact]
    public void A_hosted_profile_is_a_client_while_the_local_one_has_no_address()
    {
        using var provider = Compose(new()
        {
            ["Steward:Models:local:Model"] = "qwen3.5-4b-q8_0",
            ["Steward:Models:gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai/",
            ["Steward:Models:gemini:Model"] = "gemini-2.5-flash",
            ["Steward:Models:gemini:Auth"] = "Bearer",
            ["Steward:Models:gemini:ApiKey"] = "secret",
        });

        Assert.Null(provider.GetKeyedService<IChatClient>("local"));
        Assert.NotNull(provider.GetKeyedService<IChatClient>("gemini"));
    }

    [Fact]
    public void No_profiles_is_no_clients_and_no_failure()
    {
        using var provider = Compose([]);

        Assert.Null(provider.GetKeyedService<IChatClient>("local"));
    }

    private static ServiceProvider Compose(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddModelProfiles(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();
}
