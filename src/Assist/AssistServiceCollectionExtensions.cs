using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.Assist.Tino;
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

        // What the shopper is looking at, for a read that needs it (phase 12b): filled
        // by Tino's turn, never by a model.
        services.TryAddScoped<SharedKernel.ShopperScreen>();
        if (configuration is not null)
        {
            services.Configure<StewardOptions>(configuration.GetSection(StewardOptions.SectionName));
            services.Configure<TinoOptions>(configuration.GetSection(TinoOptions.SectionName));
            services.Configure<AltText.AltTextOptions>(configuration.GetSection(AltText.AltTextOptions.SectionName));

            // What Tino holds its answers to (the spec, *Grounding*): the scorer, when
            // the host was given its address.
            services.AddSupportScorer(configuration);
        }
        else
        {
            services.AddOptions<StewardOptions>();
            services.AddOptions<TinoOptions>();
        }
        return services;
    }
}
