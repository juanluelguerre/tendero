using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.AI;

public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// One keyed <see cref="IChatClient"/> per profile under <paramref name="section"/>,
    /// each the same pipeline: OpenTelemetry (GenAI conventions) → metering → the
    /// model. A profile that cannot be called stops the host here, naming itself,
    /// rather than failing at its first call.
    /// </summary>
    public static IServiceCollection AddModelProfiles(
        this IServiceCollection services, IConfiguration configuration, string section = "Steward:Models")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AIInvocationLog>();

        // Each profile on its own: one this host cannot reach is left out, and the
        // others are clients whether or not it is (a hosted profile does not wait
        // for the local container).
        var profiles = configuration.GetSection(section).GetChildren()
            .Select(child => (Name: child.Key, Profile: child.Get<ModelProfile>() ?? new ModelProfile()))
            .Where(entry => entry.Profile.IsAvailable)
            .ToList();

        var problems = profiles.SelectMany(entry => entry.Profile.Problems(entry.Name)).ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

        foreach (var (name, profile) in profiles)
            services.AddKeyedSingleton<IChatClient>(name, (provider, _) => Model(profile).Meter(name, profile, provider));

        return services;
    }

    /// <summary>The log and the worker that drains it — in every host that calls a model.</summary>
    public static IServiceCollection AddAIInvocationLog(this IServiceCollection services)
    {
        services.TryAddSingleton<AIInvocationLog>();
        services.AddHostedService<AIInvocationsWorker>();
        return services;
    }

    /// <summary>The same tracing and metering for a client built in code — the claim
    /// extractor's, whose model the AppHost hands it directly.</summary>
    public static IChatClient Meter(this IChatClient inner, string profileName, ModelProfile profile, IServiceProvider services) =>
        inner.AsBuilder()
            .UseOpenTelemetry(sourceName: TelemetrySources.AI)
            .Use(model => new MeteringChatClient(
                model, profileName, profile,
                services.GetRequiredService<AIInvocationLog>(),
                services.GetService<TimeProvider>() ?? TimeProvider.System))
            .Build(services);

    private static IChatClient Model(ModelProfile profile) => profile.Kind switch
    {
        ModelKind.OpenAICompatible => OpenAICompatibleChat.CreateAt(
            profile.Endpoint!, profile.Model!,
            profile.Auth == ModelAuth.Bearer ? profile.ApiKey : null,
            TimeSpan.FromSeconds(profile.TimeoutSeconds)),
        ModelKind.OnnxInProcess => InProcessOnnxChat.Create(profile.Directory!),
        ModelKind.Scripted => ScriptedChatClient.FromFile(profile.Script!, TimeSpan.FromSeconds(profile.TimeoutSeconds)),
        _ => throw new InvalidOperationException($"No model kind {profile.Kind}."),
    };
}
