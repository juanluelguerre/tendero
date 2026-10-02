using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Steward;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Assist;

public static class AssistServiceCollectionExtensions
{
    /// <summary>The registry, scoped like the entries it gathers: an entry reads
    /// through its context's repository, which is scoped to the request. And
    /// Steward's settings, with no profile at all until a host registers them.</summary>
    public static IServiceCollection AddAssist(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ProposableCommands>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(ModelProfiles.None);
        if (configuration is not null)
            services.Configure<StewardOptions>(configuration.GetSection(StewardOptions.SectionName));
        else
            services.AddOptions<StewardOptions>();
        return services;
    }
}
