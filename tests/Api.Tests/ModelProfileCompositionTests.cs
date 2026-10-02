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
