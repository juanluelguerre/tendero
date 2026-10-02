using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
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

    /// <summary>How long a whole turn may take, every call and tool included.</summary>
    public int TurnSeconds { get; init; } = 600;
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

    /// <summary>No profile's window holds the conversation; a new one starts again.</summary>
    ConversationTooLong,
}

/// <param name="Reads">Every read the turn made, by name, with its rows.</param>
/// <param name="ProposalIds">The proposals the turn stored, waiting for a person.</param>
/// <param name="Profile">The profile that answered.</param>
/// <param name="FellBack">Whether the first profile failed and the fallback answered.</param>
/// <param name="NearContextLimit">The history is near the answering profile's window: start a new conversation.</param>
/// <param name="ConversationId">The conversation this turn belongs to — minted on the first turn.</param>
public sealed record StewardAnswer(
    StewardStatus Status, string? Text, IReadOnlyList<ToolUse> Reads, IReadOnlyList<string> ProposalIds,
    TurnCost? Cost, string? Profile, bool FellBack, bool NearContextLimit, string ConversationId);

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

        // The screen is written into the instructions: each part one short line.
        RuleFor(x => x.Screen!.Page).NotEmpty().MaximumLength(ScreenContext.MaxLength).Must(OneLine)
            .When(x => x.Screen is not null);
        RuleFor(x => x.Screen!.Subject).MaximumLength(ScreenContext.MaxLength).Must(OneLine)
            .When(x => x.Screen?.Subject is not null);
        RuleFor(x => x.Culture).Must(CultureNegotiation.Supports).WithMessage("Supported cultures: es, en.");
    }

    private static bool OneLine(string? text) => text is null || !text.Any(char.IsControl);
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

    private static readonly Meter Meter = new(TelemetrySources.Assist);

    /// <summary>`tendero.steward.turns` (the tracking plan): turns by how they ended
    /// and whether a fallback answered — `ai-usage.json`'s unavailability and fallbacks.</summary>
    private static readonly Counter<long> Turns = Meter.CreateCounter<long>(
        "tendero.steward.turns", unit: "{turn}", description: "Steward's turns, by status and whether a fallback profile was reached.");

    public async Task<StewardAnswer> HandleAsync(StewardTurnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.steward.turn");
        activity?.SetTag("assist.conversation", command.ConversationId);
        var settings = options.Value;

        var instructions = StewardPrompt.For(command.Culture, command.Screen);
        var history = command.Messages
            .Select(message => new ChatMessage(message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, message.Text))
            .ToList();
        var said = command.Messages.Sum(message => message.Text.Length);

        string?[] names = [settings.Model, settings.Fallback];
        var candidates = names.OfType<string>().Where(name => name.Length > 0).Distinct()
            .Select(name => (Name: name, Client: services.GetKeyedService<IChatClient>(name), Profile: profiles.Find(name)))
            .Where(candidate => candidate.Client is not null && candidate.Profile is not null)
            .Select(candidate => new Candidate(candidate.Name, candidate.Client!, candidate.Profile!, new StewardTrace()))
            .ToList();
        foreach (var candidate in candidates)
        {
            candidate.Tools = StewardTools.For(reads, proposals, commands,
                new ProposalSource(candidate.Name, candidate.Profile.Model, StewardPrompt.Version, command.ConversationId),
                candidate.Trace);
            // The instructions and the tools' schemas travel with every call, and an
            // answer needs its room: all of it is what the window must hold.
            candidate.Needs = Tokens(instructions.Length + StewardTools.Size(candidate.Tools) + said) + settings.MaxOutputTokens;
        }

        // Checked with this turn in it, before anything is spent.
        var asking = candidates.Count > 0 ? candidates.Min(candidate => candidate.Needs) : 0;
        if (await usage.ConversationTokensAsync(command.ConversationId, cancellationToken) + asking > settings.ConversationTokenBudget)
            return Answer(command, StewardStatus.BudgetExceeded, null, new StewardTrace(), null, null, false, false);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TurnSeconds), clock);
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var fellBack = false;
        var tooLong = false;
        var passedOne = false;
        foreach (var candidate in candidates)
        {
            var (name, profile, trace) = (candidate.Name, candidate.Profile, candidate.Trace);

            // Falling back is reaching a profile after another: one that failed alone
            // was not a fallback failing too, and the panel must not say it was.
            fellBack |= passedOne;
            passedOne = true;

            // A window that cannot hold the question and an answer is not asked: the
            // model would cut the history and answer something else.
            if (candidate.Needs > profile.ContextTokens)
            {
                tooLong = true;
                continue;
            }

            var started = clock.GetTimestamp();
            try
            {
                var response = await RunAsync(candidate, instructions, command, history, settings, turn.Token);
                var calls = response.Messages.Count(message => message.Role == ChatRole.Assistant);
                var input = response.Usage?.InputTokenCount ?? 0;
                var output = response.Usage?.OutputTokenCount ?? 0;
                activity?.SetTag("assist.profile", name);

                return Answer(command, StewardStatus.Answered,
                    string.IsNullOrWhiteSpace(response.Text) ? StewardPrompt.StoppedAtTheCap(command.Culture) : response.Text,
                    trace,
                    new TurnCost(calls, input, output, profile.CostMicroEuros(input, output),
                        (int)clock.GetElapsedTime(started).TotalMilliseconds, profile.Model ?? name),
                    name, fellBack, candidate.Needs > profile.ContextTokens * 0.8);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                // Only the model failing is worth another profile. The turn outliving
                // its deadline, or a failure of the shop's own, would spend the same
                // on the fallback and end the same.
                var modelFailed = !deadline.IsCancellationRequested && ModelFailures.IsModelFailure(failure);
                if (modelFailed)
                    LogFailed(logger, failure, name);
                else if (deadline.IsCancellationRequested)
                    LogOutlived(logger, failure, name, settings.TurnSeconds);
                else
                    LogBroken(logger, failure, name);

                // A turn that stored proposals before failing keeps them — they wait
                // for a person — and does not fall back: a second model would propose
                // them again.
                if (!modelFailed || trace.Proposed.Count > 0)
                {
                    activity?.SetTag("assist.unavailable", true);
                    return Answer(command, StewardStatus.Unavailable, null, trace, null, name, fellBack, false);
                }
            }
        }

        if (tooLong)
            return Answer(command, StewardStatus.ConversationTooLong, null, new StewardTrace(), null, null, fellBack, true);

        activity?.SetTag("assist.unavailable", true);
        return Answer(command, StewardStatus.Unavailable, null, new StewardTrace(), null, null, fellBack, false);
    }

    /// <summary>A profile the turn may ask, with its tools and what asking it takes.</summary>
    private sealed record Candidate(string Name, IChatClient Client, ModelProfile Profile, StewardTrace Trace)
    {
        public IReadOnlyList<AIFunction> Tools { get; set; } = [];

        /// <summary>Tokens the first call takes, the answer's room included.</summary>
        public int Needs { get; set; }
    }

    /// <summary>Four characters a token, rounded up: an estimate, which the window's
    /// margin and the near-limit flag absorb.</summary>
    private static int Tokens(int characters) => (characters + 3) / 4;

    private async Task<AgentResponse> RunAsync(
        Candidate candidate, string instructions, StewardTurnCommand command, List<ChatMessage> history,
        StewardOptions settings, CancellationToken cancellationToken)
    {
        // Short-lived, one per turn (invariant 9): the caps are the function loop's,
        // and the model's client is the profile's, metered and traced call by call.
        // No detailed errors: what a failure of the shop's says is not the model's to
        // read — a model's own mistake reaches it as the tool's answer instead.
        var pipeline = candidate.Client.AsBuilder()
            .UseFunctionInvocation(configure: invocation => invocation.MaximumIterationsPerRequest = settings.MaxIterations)
            .Build(services);

        var agent = new ChatClientAgent(pipeline, new ChatClientAgentOptions
        {
            Name = "steward",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [.. candidate.Tools],
                MaxOutputTokens = settings.MaxOutputTokens,
            },
            UseProvidedChatClientAsIs = true,
        }).AsBuilder().UseOpenTelemetry(sourceName: TelemetrySources.AI).Build(services);

        return await agent.RunAsync(history,
            options: new ChatClientAgentRunOptions(ModelCall.Options("steward.turn", command.ConversationId, StewardPrompt.Version)),
            cancellationToken: cancellationToken);
    }

    /// <summary>Every turn ends here, so every turn is counted here, once.</summary>
    private static StewardAnswer Answer(
        StewardTurnCommand command, StewardStatus status, string? text, StewardTrace trace, TurnCost? cost, string? profile,
        bool fellBack, bool nearLimit)
    {
        Turns.Add(1,
            new KeyValuePair<string, object?>("status", MetricLabels.Of(status)),
            new KeyValuePair<string, object?>("fell_back", fellBack));
        return new(status, text, trace.Reads, trace.Proposed, cost, profile, fellBack, nearLimit, command.ConversationId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steward's profile {Profile} failed a turn")]
    private static partial void LogFailed(ILogger logger, Exception failure, string profile);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steward's turn on {Profile} outlived its {Seconds} seconds")]
    private static partial void LogOutlived(ILogger logger, Exception failure, string profile, int seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Steward's turn on {Profile} broke on the shop's side, not the model's")]
    private static partial void LogBroken(ILogger logger, Exception failure, string profile);
}

// ---------- The wire ----------

/// <param name="ConversationId">The conversation to continue; omitted on its first turn.</param>
public sealed record StewardTurnRequest(IReadOnlyList<TurnMessage> Messages, ScreenContext? Screen, string? ConversationId);

public sealed class StewardTurnEndpoints : ICarterModule
{
    /// <summary>The rate limiter's policy: twenty turns a minute per person (the spec, *Security*).</summary>
    public const string TurnsPerPerson = "steward-turns";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/steward/turns?culture=en — the conversation so far, the screen it
        // was asked on; the server keeps nothing between turns.
        app.MapPost("/api/steward/turns",
                async Task<Ok<StewardAnswer>> (StewardTurnRequest body, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var conversation = body.ConversationId is { Length: > 0 } id ? id : Guid.CreateVersion7().ToString();
                    var answer = await dispatcher.SendAsync(
                        new StewardTurnCommand(body.Messages ?? [], body.Screen, conversation, language), ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(answer);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .RequireRateLimiting(TurnsPerPerson)
            .WithTags("Steward")
            .WithName("TakeStewardTurn");
    }
}
