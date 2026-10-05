using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>
/// Tino's settings (`Tino:` in configuration). The caps are first estimates sized
/// for a shopper, who waits seconds and not minutes; the <c>assistant</c> suite
/// fixes them from the largest conversation it records (T-5).
/// </summary>
public sealed class TinoOptions
{
    public const string SectionName = "Tino";

    /// <summary>Whether Tino answers at all. Off, the storefront says so and the search is what there is.</summary>
    public bool Enabled { get; init; }

    /// <summary>The profile that answers, by its name among the shop's model profiles.</summary>
    public string? Model { get; init; }

    /// <summary>The one tried when it does not.</summary>
    public string? Fallback { get; init; }

    public int MaxOutputTokens { get; init; } = 400;

    /// <summary>Tool rounds the gathering may take.</summary>
    public int MaxIterations { get; init; } = 3;

    /// <summary>How long a whole turn may take: both phases, every call and tool.</summary>
    public int TurnSeconds { get; init; } = 20;

    public long ConversationTokenBudget { get; init; } = 30_000;

    /// <summary>
    /// Tokens every shopper together may spend in the shop's day. An anonymous
    /// shopper is nobody to bill and a conversation id is the client's to send, so
    /// this is what bounds the cost of an endpoint anyone may call.
    /// </summary>
    public long ShopDailyTokenBudget { get; init; } = 2_000_000;
}

/// <summary>
/// One turn of a conversation with Tino (phase 12b). The conversation travels with
/// the browser — the server keeps nothing between turns. Not audited: it reads.
/// </summary>
public sealed record TinoTurnCommand(
    IReadOnlyList<TurnMessage> Messages, TinoContext? Context, string ConversationId, string Culture)
    : ICommand<TinoAnswer>, IUnauditedCommand;

public sealed class TinoTurnValidator : AbstractValidator<TinoTurnCommand>
{
    /// <summary>A shopper's message at most: a question, not a document.</summary>
    public const int MaxMessage = 2_000;

    public TinoTurnValidator()
    {
        RuleFor(x => x.Messages).NotEmpty();
        RuleFor(x => x.Messages.Count).LessThanOrEqualTo(40);
        RuleFor(x => x.Messages).Must(messages => messages.Count > 0 && messages[^1].Role == "user")
            .WithMessage("A turn ends with the shopper's question.");
        RuleForEach(x => x.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Role).Must(role => role is "user" or "assistant");
            message.RuleFor(m => m.Text).NotEmpty().MaximumLength(MaxMessage);
        });
        RuleFor(x => x.ConversationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Culture).Must(CultureNegotiation.Supports).WithMessage("Supported cultures: es, en.");

        // What the shopper is looking at is written into the instructions: a kind
        // this phase knows and a code, one short line with nothing that breaks it.
        RuleFor(x => x.Context!.Kind).Equal(TinoContext.Product).When(x => x.Context is not null);
        RuleFor(x => x.Context!.Key).NotEmpty().MaximumLength(TinoContext.MaxLength)
            .Must(key => key is null || !key.Any(character => char.IsControl(character) || character == '"'))
            .When(x => x.Context is not null);
    }
}

/// <summary>
/// The shopper's reads as the model's tools: each read joins the turn's sheet with
/// what it returned and is written down with what it was asked. No proposal tool —
/// Tino reads, and an architecture rule holds it.
/// </summary>
public static class TinoTools
{
    public static IReadOnlyList<AIFunction> For(IEnumerable<IReadTool> reads, FactSheet sheet, List<TinoRead> trace) =>
    [
        .. reads.Where(read => read.Audience == ToolAudience.Shopper).Select(read => (AIFunction)new JsonTool(
            read.Name, read.Description, read.ArgumentsSchema,
            async (arguments, cancellationToken) =>
            {
                var answer = await read.ReadAsync(arguments, cancellationToken);
                sheet.Add(answer);
                trace.Add(new TinoRead(read.Name, arguments.Clone(), answer.Rows, answer.Total));
                Activity.Current?.SetTag("tendero.tool.rows", answer.Rows);

                // Inside a data envelope, as Steward's reads: a description reaches the
                // model marked as data, which the instructions say is never an instruction.
                return JsonSerializer.SerializeToElement(
                    new { data = answer.Data, rows = answer.Rows, total = answer.Total }, ArgumentSchemas.Json);
            })),
    ];
}

public sealed class TinoTurnHandler(
    IEnumerable<IReadTool> reads,
    IServiceProvider services,
    ModelProfiles profiles,
    IAIInvocationReader usage,
    IPrincipalAccessor principal,
    IOptions<ShopOptions> shop,
    IOptions<TinoOptions> options,
    TimeProvider clock,
    ILogger<TinoTurnHandler> logger) : ICommandHandler<TinoTurnCommand, TinoAnswer>
{
    /// <summary>What both of a turn's calls are metered under.</summary>
    public const string Operation = "tino.turn";

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);
    private static readonly Meter Meter = new(TelemetrySources.Assist);

    /// <summary>`tendero.tino.turns`: turns by how they ended, why Tino did not answer, and the kind of answer.</summary>
    private static readonly Counter<long> Turns = Meter.CreateCounter<long>(
        "tendero.tino.turns", unit: "{turn}", description: "Tino's turns, by status, by cause when it did not answer, and by the kind of answer.");

    public async Task<TinoAnswer> HandleAsync(TinoTurnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.tino.turn");
        activity?.SetTag("assist.conversation", command.ConversationId);
        var settings = options.Value;
        if (!settings.Enabled)
            return Unavailable(command, TinoCause.NoProfile, [], null, false);

        var gathering = TinoPrompt.ToGather(command.Culture, command.Context);
        var composing = TinoPrompt.ToCompose(command.Context);
        var said = command.Messages.Sum(message => message.Text.Length);
        var candidates = ProfileWalk.Find(services, profiles, settings.Model, settings.Fallback)
            .Select(found => new Candidate(found.Name, found.Client, found.Profile))
            .ToList();
        foreach (var candidate in candidates)
        {
            candidate.Tools = TinoTools.For(reads, candidate.Sheet, candidate.Reads);
            candidate.Needs = ProfileWalk.Tokens(gathering.Length + JsonTool.Size(candidate.Tools) + said) + settings.MaxOutputTokens;
        }

        if (candidates.Count == 0)
            return Unavailable(command, TinoCause.NoProfile, [], null, false);

        // The shop's day and the conversation, with this turn in them, before anything
        // is spent: past either, no model is asked.
        var asking = candidates.Min(candidate => candidate.Needs);
        if (await usage.OperationTokensSinceAsync(Operation, StartOfShopDay(), cancellationToken) + asking > settings.ShopDailyTokenBudget
            || await usage.ConversationTokensAsync(command.ConversationId, cancellationToken) + asking > settings.ConversationTokenBudget)
            return Unavailable(command, TinoCause.Budget, [], null, false);

        var walk = await ProfileWalk.RunAsync("Tino", candidates, settings.TurnSeconds, clock, logger,
            (candidate, turn) => AskAsync(candidate, gathering, composing, command, settings, turn),
            _ => false,
            cancellationToken);

        if (walk.End == WalkEnd.Answered)
        {
            var (body, cost, answered) = (walk.Answer.Body!, walk.Answer.Cost, walk.Candidate!);
            activity?.SetTag("assist.profile", answered.Name);
            return Count(new TinoAnswer(TinoStatus.Answered, null, body.Kind, body.Products, body.Says, body.Ask, body.NoData,
                answered.Reads, SearchText(command), body.Dropped, cost, answered.Name, walk.FellBack, command.ConversationId));
        }

        activity?.SetTag("assist.unavailable", true);
        return Unavailable(command, walk.End switch
        {
            WalkEnd.OutOfTime => TinoCause.Time,
            WalkEnd.Broken => TinoCause.Shop,
            WalkEnd.TooLong => TinoCause.TooLong,
            WalkEnd.NoProfile => TinoCause.NoProfile,
            _ => TinoCause.Model,
        }, walk.Candidate?.Reads ?? [], walk.Candidate?.Name, walk.FellBack);
    }

    /// <summary>
    /// A turn on one profile, in its two phases (<c>docs/analysis/tino-models.md</c>):
    /// the server refuses tools and an answer's schema in the same call. Gathering
    /// reads through the tools and its words are thrown away; composing has no tools
    /// and is held to the schema built from what was read.
    /// </summary>
    private async Task<(TinoBody Body, TurnCost Cost)> AskAsync(
        Candidate candidate, string gathering, string composing, TinoTurnCommand command, TinoOptions settings,
        CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var person = principal.Current.Subject;
        var history = command.Messages
            .Select(message => new ChatMessage(message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, message.Text))
            .ToList();

        // Short-lived, one per turn (invariant 9); the caps are the function loop's.
        var pipeline = candidate.Client.AsBuilder()
            .UseFunctionInvocation(configure: invocation => invocation.MaximumIterationsPerRequest = settings.MaxIterations)
            .Build(services);
        var agent = new ChatClientAgent(pipeline, new ChatClientAgentOptions
        {
            Name = "tino",
            ChatOptions = new ChatOptions { Instructions = gathering, Tools = [.. candidate.Tools], MaxOutputTokens = settings.MaxOutputTokens },
            UseProvidedChatClientAsIs = true,
        }).AsBuilder().UseOpenTelemetry(sourceName: TelemetrySources.AI).Build(services);
        var gathered = await agent.RunAsync(history,
            options: new ChatClientAgentRunOptions(ModelCall.Options(Operation, command.ConversationId, TinoPrompt.Version, person: person)),
            cancellationToken: cancellationToken);

        // The sheet goes where the shopper's last words were, in front of them: what
        // the model answers from, and the question it answers.
        var sheet = candidate.Sheet;
        List<ChatMessage> asked =
        [
            new(ChatRole.System, composing),
            .. history[..^1],
            new(ChatRole.User, $"{sheet.ToModel()}\nSHOPPER: {history[^1].Text}"),
        ];
        var composed = await candidate.Client.GetResponseAsync(asked,
            ModelCall.Options(Operation, command.ConversationId, TinoPrompt.Version,
                new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(AnswerSchema.For(sheet), AnswerSchema.Name),
                    MaxOutputTokens = settings.MaxOutputTokens,
                    Temperature = 0,
                },
                person),
            cancellationToken);

        // What was written is not an answer: the model's failure, worth another profile.
        var body = TinoReply.Read(composed.Text, sheet)
            ?? throw new UnusableAnswerException("Tino's model wrote nothing that is an answer.");

        var calls = gathered.Messages.Count(message => message.Role == ChatRole.Assistant) + 1;
        var input = (gathered.Usage?.InputTokenCount ?? 0) + (composed.Usage?.InputTokenCount ?? 0);
        var output = (gathered.Usage?.OutputTokenCount ?? 0) + (composed.Usage?.OutputTokenCount ?? 0);
        return (body, new TurnCost(calls, input, output, candidate.Profile.CostMicroEuros(input, output),
            (int)clock.GetElapsedTime(started).TotalMilliseconds, candidate.Profile.Model ?? candidate.Name));
    }

    /// <summary>What the storefront searches for when Tino did not answer: the shopper's last words.</summary>
    private static string SearchText(TinoTurnCommand command) => command.Messages[^1].Text;

    private static TinoAnswer Unavailable(
        TinoTurnCommand command, TinoCause cause, IReadOnlyList<TinoRead> reads, string? profile, bool fellBack) =>
        Count(new TinoAnswer(TinoStatus.Unavailable, cause, null, [], [], null, null, reads, SearchText(command), 0, null,
            profile, fellBack, command.ConversationId));

    /// <summary>Every turn ends here, so every turn is counted here, once.</summary>
    private static TinoAnswer Count(TinoAnswer answer)
    {
        Turns.Add(1,
            new KeyValuePair<string, object?>("status", MetricLabels.Of(answer.Status)),
            new KeyValuePair<string, object?>("cause", answer.Cause is { } cause ? MetricLabels.Of(cause) : "none"),
            new KeyValuePair<string, object?>("kind", answer.Kind is { } kind ? MetricLabels.Of(kind) : "none"));
        return answer;
    }

    private DateTimeOffset StartOfShopDay()
    {
        var zone = DateTimeZoneProviders.Tzdb[shop.Value.TimeZone];
        var today = Instant.FromDateTimeOffset(clock.GetUtcNow()).InZone(zone).Date;
        return today.AtStartOfDayInZone(zone).ToInstant().ToDateTimeOffset();
    }

    /// <summary>A profile the turn may ask, with what it read on it: a fallback starts with an empty sheet.</summary>
    private sealed record Candidate(string Name, IChatClient Client, ModelProfile Profile) : IWalkCandidate
    {
        public FactSheet Sheet { get; } = new();

        public List<TinoRead> Reads { get; } = [];

        public IReadOnlyList<AIFunction> Tools { get; set; } = [];

        public int Needs { get; set; }
    }
}
