using System.Diagnostics;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Assist.Steward;

/// <summary>Steward's settings (`Steward:` in configuration).</summary>
public sealed class StewardOptions
{
    public const string SectionName = "Steward";

    /// <summary>The profile that answers.</summary>
    public string? Model { get; init; }

    /// <summary>The one tried when it does not.</summary>
    public string? Fallback { get; init; }

    /// <summary>A model call's answer at most: writing is the slow half on a CPU (S-2).</summary>
    public int MaxOutputTokens { get; init; } = 600;

    /// <summary>Tool rounds a turn may take; one more model call answers, so six calls at most.</summary>
    public int MaxIterations { get; init; } = 5;

    /// <summary>Tokens a conversation may spend, input and output, before it must start again.</summary>
    public long ConversationTokenBudget { get; init; } = 60_000;
}

/// <param name="Role">"user" or "assistant".</param>
public sealed record TurnMessage(string Role, string Text);

/// <summary>
/// One turn of a conversation with Steward (phase 12). The conversation travels
/// with the client — the server keeps nothing between turns. Not audited itself:
/// what it does that changes anything is proposing, and that is an audited command.
/// </summary>
public sealed record StewardTurnCommand(
    IReadOnlyList<TurnMessage> Messages, ScreenContext? Screen, string ConversationId, string Culture)
    : ICommand<StewardAnswer>, IUnauditedCommand;

public enum StewardStatus
{
    Answered,

    /// <summary>No model answered: nothing was proposed by this turn unless the ids say so.</summary>
    Unavailable,

    /// <summary>The conversation spent its tokens; a new one starts again.</summary>
    BudgetExceeded,
}

/// <param name="Reads">Every read the turn made, by name, with its rows.</param>
/// <param name="ProposalIds">The proposals the turn stored, waiting for a person.</param>
/// <param name="Profile">The profile that answered.</param>
/// <param name="FellBack">Whether the first profile failed and the fallback answered.</param>
/// <param name="NearContextLimit">The history is near the answering profile's window: start a new conversation.</param>
public sealed record StewardAnswer(
    StewardStatus Status, string? Text, IReadOnlyList<ToolUse> Reads, IReadOnlyList<string> ProposalIds,
    TurnCost? Cost, string? Profile, bool FellBack, bool NearContextLimit);

/// <summary>What the turn cost, at the answering profile's price: the panel's cost line adds these up.</summary>
public sealed record TurnCost(int Calls, long InputTokens, long OutputTokens, long MicroEuros, int Milliseconds, string Model);

public sealed class StewardTurnValidator : AbstractValidator<StewardTurnCommand>
{
    public StewardTurnValidator()
    {
        RuleFor(x => x.Messages).NotEmpty();
        RuleFor(x => x.Messages.Count).LessThanOrEqualTo(200);
        RuleFor(x => x.Messages).Must(messages => messages.Count > 0 && messages[^1].Role == "user")
            .WithMessage("A turn ends with the shopkeeper's question.");
        RuleForEach(x => x.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Role).Must(role => role is "user" or "assistant");
            message.RuleFor(m => m.Text).NotEmpty().MaximumLength(20_000);
        });
        RuleFor(x => x.ConversationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Culture).Must(CultureNegotiation.Supports).WithMessage("Supported cultures: es, en.");
    }
}

public sealed partial class StewardTurnHandler(
    IEnumerable<IReadTool> reads,
    ProposableCommands proposals,
    ICommandDispatcher commands,
    IServiceProvider services,
    ModelProfiles profiles,
    IAIInvocationReader usage,
    IOptions<StewardOptions> options,
    TimeProvider clock,
    ILogger<StewardTurnHandler> logger) : ICommandHandler<StewardTurnCommand, StewardAnswer>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<StewardAnswer> HandleAsync(StewardTurnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.steward.turn");
        activity?.SetTag("assist.conversation", command.ConversationId);
        var settings = options.Value;

        if (await usage.ConversationTokensAsync(command.ConversationId, cancellationToken) >= settings.ConversationTokenBudget)
            return Answer(StewardStatus.BudgetExceeded, null, new StewardTrace(), null, null, false, false);

        var history = command.Messages
            .Select(message => new ChatMessage(message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, message.Text))
            .ToList();
        var reading = command.Messages.Sum(message => message.Text.Length) / 4;   // four characters a token

        string?[] candidates = [settings.Model, settings.Fallback];
        var fellBack = false;
        foreach (var name in candidates.OfType<string>().Where(name => name.Length > 0).Distinct())
        {
            var client = services.GetKeyedService<IChatClient>(name);
            var profile = profiles.Find(name);
            if (client is null || profile is null)
                continue;

            var trace = new StewardTrace();
            var started = clock.GetTimestamp();
            try
            {
                var response = await RunAsync(client, name, profile, trace, command, history, settings, cancellationToken);
                var calls = response.Messages.Count(message => message.Role == ChatRole.Assistant);
                var input = response.Usage?.InputTokenCount ?? 0;
                var output = response.Usage?.OutputTokenCount ?? 0;
                activity?.SetTag("assist.profile", name);

                return Answer(StewardStatus.Answered,
                    string.IsNullOrWhiteSpace(response.Text) ? StewardPrompt.StoppedAtTheCap(command.Culture) : response.Text,
                    trace,
                    new TurnCost(calls, input, output, profile.CostMicroEuros(input, output),
                        (int)clock.GetElapsedTime(started).TotalMilliseconds, profile.Model ?? name),
                    name, fellBack, reading > profile.ContextTokens * 0.8);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(logger, failure, name);

                // A turn that stored proposals before failing keeps them — they wait
                // for a person — and does not fall back: a second model would propose
                // them again.
                if (trace.Proposed.Count > 0)
                    return Answer(StewardStatus.Unavailable, null, trace, null, name, fellBack, false);

                fellBack = true;
            }
        }

        activity?.SetTag("assist.unavailable", true);
        return Answer(StewardStatus.Unavailable, null, new StewardTrace(), null, null, fellBack, false);
    }

    private async Task<AgentResponse> RunAsync(
        IChatClient client, string profileName, ModelProfile profile, StewardTrace trace,
        StewardTurnCommand command, List<ChatMessage> history, StewardOptions settings, CancellationToken cancellationToken)
    {
        var tools = StewardTools.For(reads, proposals, commands,
            new ProposalSource(profileName, profile.Model, StewardPrompt.Version, command.ConversationId), trace);

        // Short-lived, one per turn (invariant 9): the caps are the function loop's,
        // and the model's client is the profile's, metered and traced call by call.
        var pipeline = client.AsBuilder()
            .UseFunctionInvocation(configure: invocation =>
            {
                invocation.MaximumIterationsPerRequest = settings.MaxIterations;
                invocation.IncludeDetailedErrors = true;
            })
            .Build(services);

        var agent = new ChatClientAgent(pipeline, new ChatClientAgentOptions
        {
            Name = "steward",
            ChatOptions = new ChatOptions
            {
                Instructions = StewardPrompt.For(command.Culture, command.Screen),
                Tools = [.. tools],
                MaxOutputTokens = settings.MaxOutputTokens,
            },
            UseProvidedChatClientAsIs = true,
        }).AsBuilder().UseOpenTelemetry(sourceName: TelemetrySources.AI).Build(services);

        return await agent.RunAsync(history,
            options: new ChatClientAgentRunOptions(ModelCall.Options("steward.turn", command.ConversationId, StewardPrompt.Version)),
            cancellationToken: cancellationToken);
    }

    private static StewardAnswer Answer(
        StewardStatus status, string? text, StewardTrace trace, TurnCost? cost, string? profile, bool fellBack, bool nearLimit) =>
        new(status, text, trace.Reads, trace.Proposed, cost, profile, fellBack, nearLimit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steward's profile {Profile} failed a turn")]
    private static partial void LogFailed(ILogger logger, Exception failure, string profile);
}
