using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Assist;

public static class AssistServiceCollectionExtensions
{
    /// <summary>The registry, scoped like the entries it gathers: an entry reads
    /// through its context's repository, which is scoped to the request.</summary>
    public static IServiceCollection AddAssist(this IServiceCollection services)
    {
        services.AddScoped<ProposableCommands>();
        return services;
    }
}
