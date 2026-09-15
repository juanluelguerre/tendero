using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The flags as every process reads them: from <c>Features:*</c>, through the
/// gate the service defaults register (ADR 0023).
/// </summary>
public sealed class FeatureGateTests
{
    [Fact]
    public void A_flag_is_on_only_when_its_value_says_true()
    {
        var gate = GateOver(new Dictionary<string, string?>
        {
            ["Features:SearchVector"] = "True",
            ["Features:SearchRerank"] = "yes"
        }, out _);

        Assert.True(gate.IsEnabled(FeatureId.SearchVector));

        // Anything that is not a boolean is off: the default is the path with no AI in it.
        Assert.False(gate.IsEnabled(FeatureId.SearchRerank));
    }

    [Fact]
    public void An_unset_flag_is_off()
    {
        var gate = GateOver(new Dictionary<string, string?>(), out _);

        Assert.All(Enum.GetValues<FeatureId>(), flag => Assert.False(gate.IsEnabled(flag)));
    }

    /// <summary>Read per call, so a reloaded configuration takes effect without a restart.</summary>
    [Fact]
    public void A_changed_value_is_read_on_the_next_call()
    {
        var gate = GateOver(new Dictionary<string, string?> { ["Features:SearchVector"] = "true" }, out var configuration);
        Assert.True(gate.IsEnabled(FeatureId.SearchVector));

        configuration["Features:SearchVector"] = "false";

        Assert.False(gate.IsEnabled(FeatureId.SearchVector));
    }

    private static IFeatureGate GateOver(Dictionary<string, string?> settings, out IConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();

        var provider = builder.Services.BuildServiceProvider();
        configuration = provider.GetRequiredService<IConfiguration>();
        return provider.GetRequiredService<IFeatureGate>();
    }
}
