using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace ElGuerre.Tendero.ServiceDefaults;

/// <summary>
/// The flags, read from configuration on every call: <c>Features:SearchVector=true</c>,
/// or <c>Features__SearchVector=true</c> in the environment the AppHost writes.
///
/// Not <c>Microsoft.FeatureManagement</c>. It is MIT and therefore allowed, but it
/// brings targeting, filters and variants for a closed set of two booleans, and the
/// dependency budget is deliberately small (roadmap D). Read per call rather than
/// once, so a reloaded configuration takes effect without a restart. Anything that
/// is not exactly "true" is off, because an unset flag is the default and the
/// default is the path with no AI in it.
/// </summary>
internal sealed class ConfigurationFeatureGate(IConfiguration configuration) : IFeatureGate
{
    public const string Section = "Features";

    public bool IsEnabled(FeatureId feature) =>
        bool.TryParse(configuration[$"{Section}:{feature}"], out var enabled) && enabled;
}
