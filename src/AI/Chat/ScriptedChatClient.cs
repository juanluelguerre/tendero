using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI.Chat;

/// <param name="When">Matched, case-insensitively, against the last thing the person said.</param>
/// <param name="Calls">The tools it calls first, if any.</param>
/// <param name="Say">What it answers once the tools have answered, with
/// <c>{tool:name}</c> replaced by that tool's result.</param>
/// <param name="DelayMs">How long it takes, so a timeout can be shown.</param>
/// <param name="Always">Calls its tools again even after they answered — a model
/// that loops, so a turn's cap can be shown.</param>
/// <param name="ThenFail">Fails once its tools answered — a provider dropping a turn
/// half way.</param>
public sealed record ScriptedTurn(
    string When, IReadOnlyList<ScriptedCall>? Calls = null, string? Say = null,
    int InputTokens = 100, int OutputTokens = 20, int DelayMs = 0, bool Always = false, bool ThenFail = false);

public sealed record ScriptedCall(string Tool, JsonElement Arguments);

/// <summary>
/// A model that answers from a script (the Steward spec, *Evaluation and tests*): a
/// profile kind for tests only, deterministic, so the whole pipeline — the agent,
/// the tools, the proposals, the approval — runs in CI, where no model fits. It
/// calls a step's tools when the person's last words match it, and says its line
/// once the tools have answered.
/// </summary>
public sealed class ScriptedChatClient(IReadOnlyList<ScriptedTurn> script, TimeSpan timeout) : IChatClient
{
    public const string NotScripted = "I was not scripted for that.";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScriptedChatClient FromFile(string path, TimeSpan timeout) =>
        new(JsonSerializer.Deserialize<ScriptedTurn[]>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException($"The script {path} is empty."), timeout);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var conversation = messages.ToList();
        var asked = conversation.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? "";
        var step = script.FirstOrDefault(turn => asked.Contains(turn.When, StringComparison.OrdinalIgnoreCase));

        if (step is null)
            return Answer(new ChatMessage(ChatRole.Assistant, NotScripted), 10, 6);

        if (step.DelayMs > 0)
        {
            // The HTTP client's timeout, played by the script: a step slower than
            // the profile's timeout surfaces as one.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await Task.Delay(step.DelayMs, deadline.Token);
        }

        var answered = conversation.LastOrDefault()?.Role == ChatRole.Tool;
        if (answered && step.ThenFail)
            throw new HttpRequestException("The scripted provider dropped the turn.");
        if (step.Calls is { Count: > 0 } calls && (!answered || step.Always))
        {
            return Answer(new ChatMessage(ChatRole.Assistant,
            [
                .. calls.Select((call, index) => new FunctionCallContent($"call-{index}", call.Tool, Arguments(call.Arguments))),
            ]), step.InputTokens, step.OutputTokens, ChatFinishReason.ToolCalls);
        }

        return Answer(new ChatMessage(ChatRole.Assistant, Fill(step.Say ?? "", conversation)), step.InputTokens, step.OutputTokens);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is a non-goal of Steward's spec, and the script does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static ChatResponse Answer(ChatMessage message, int input, int output, ChatFinishReason? finish = null) =>
        new(message)
        {
            FinishReason = finish ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output },
            ModelId = "scripted",
        };

    private static Dictionary<string, object?> Arguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
            : [];

    /// <summary>Each <c>{tool:name}</c> becomes that tool's latest result, as text.</summary>
    private static string Fill(string say, IReadOnlyList<ChatMessage> conversation)
    {
        var names = conversation.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
            .GroupBy(call => call.CallId).ToDictionary(group => group.Key, group => group.Last().Name);

        foreach (var result in conversation.SelectMany(message => message.Contents).OfType<FunctionResultContent>())
        {
            if (names.TryGetValue(result.CallId, out var name))
                say = say.Replace($"{{tool:{name}}}", Text(result.Result), StringComparison.Ordinal);
        }

        return say;
    }

    private static string Text(object? result) => result switch
    {
        null => "",
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? "",
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result, Json),
    };
}
