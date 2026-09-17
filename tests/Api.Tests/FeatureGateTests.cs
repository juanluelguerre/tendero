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
///
/// The set is empty since 2026-09-17 — <c>SearchRerank</c> became a capability
/// and <c>SearchVector</c> met its removal condition — so the tests that parsed
/// a value went with the last member they could parse it for. The gate stays,
/// because phases 11 and 12 bring flags of their own, and what it must still do
/// with nothing configured is answer off.
/// </summary>
public sealed class FeatureGateTests
{
    [Fact]
    public void An_unset_flag_is_off()
    {
        var gate = GateOver(new Dictionary<string, string?>());

        Assert.All(Enum.GetValues<FeatureId>(), flag => Assert.False(gate.IsEnabled(flag)));
    }

    private static IFeatureGate GateOver(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();

        return builder.Services.BuildServiceProvider().GetRequiredService<IFeatureGate>();
    }
}
