using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
/// <param name="Then">A second round, called once the first round answered (S-6):
/// an argument written <c>{from:tool:path}</c> takes that tool's latest result at
/// that path — <c>data.products.0.productId</c> — so a script can find a product
/// and then propose for it, though its id is minted at import.</param>
/// <param name="Reply">What it answers a call that asks for JSON of a schema
/// (phase 12b): the object itself, or a string to answer something that is not JSON.
/// Such a call has no tools, so the step's calls are not made again. A value written
/// <c>{asked:pattern}</c> takes the first group the pattern captures in what was
/// asked — a product's code is minted at import, so a script finds it by the name
/// beside it: <c>{asked:(\S+) = Sartén}</c>.</param>
public sealed record ScriptedTurn(
    string When, IReadOnlyList<ScriptedCall>? Calls = null, string? Say = null,
    int InputTokens = 100, int OutputTokens = 20, int DelayMs = 0, bool Always = false, bool ThenFail = false,
    IReadOnlyList<ScriptedCall>? Then = null, JsonElement? Reply = null);

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

        // A call held to a schema is answered with the step's reply, as it is written.
        if (options?.ResponseFormat is ChatResponseFormatJson)
        {
            var reply = step.Reply switch
            {
                { ValueKind: JsonValueKind.String } text => text.GetString() ?? "",
                { } json => FromAsked(JsonNode.Parse(json.GetRawText()), asked)?.ToJsonString() ?? "null",
                null => NotScripted,
            };
            return Answer(new ChatMessage(ChatRole.Assistant, reply), step.InputTokens, step.OutputTokens);
        }

        // How many rounds of calls this turn has made since the person spoke.
        var rounds = conversation
            .Skip(conversation.FindLastIndex(message => message.Role == ChatRole.User) + 1)
            .Count(message => message.Role == ChatRole.Assistant && message.Contents.OfType<FunctionCallContent>().Any());

        var next = rounds switch
        {
            0 => step.Calls,
            1 when step.Then is { Count: > 0 } then => then,
            _ when step.Always => step.Calls,
            _ => null,
        };
        if (rounds > 0 && step.ThenFail && (step.Then is null || rounds > 1))
            throw new HttpRequestException("The scripted provider dropped the turn.");
        if (next is { Count: > 0 } calls)
        {
            return Answer(new ChatMessage(ChatRole.Assistant,
            [
                .. calls.Select((call, index) =>
                    new FunctionCallContent($"call-{rounds}-{index}", call.Tool, Arguments(call.Arguments, conversation))),
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

    private static Dictionary<string, object?> Arguments(JsonElement arguments, IReadOnlyList<ChatMessage> conversation)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return [];

        var resolved = Resolve(JsonNode.Parse(arguments.GetRawText()), conversation);
        return JsonSerializer.SerializeToElement(resolved).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }

    /// <summary>A word of what was asked: <c>{asked:(\S+) = Sartén}</c> is the code beside that name.</summary>
    private static readonly Regex Asked = new(@"^\{asked:(?<pattern>.+)\}$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The reply with every <c>{asked:pattern}</c> replaced by what the pattern captures in what was asked; a pattern that finds nothing stays as written, which no answer accepts.</summary>
    private static JsonNode? FromAsked(JsonNode? node, string asked)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var name in properties.Select(property => property.Key).ToList())
                {
                    var child = properties[name];
                    if (FromAsked(child, asked) is var resolved && !ReferenceEquals(child, resolved))
                        properties[name] = resolved;
                }

                return properties;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    var child = items[index];
                    if (FromAsked(child, asked) is var resolved && !ReferenceEquals(child, resolved))
                        items[index] = resolved;
                }

                return items;
            case JsonValue value when value.TryGetValue<string>(out var text) && Asked.Match(text) is { Success: true } reference:
                var found = Regex.Match(asked, reference.Groups["pattern"].Value, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
                return found is { Success: true, Groups.Count: > 1 } ? JsonValue.Create(found.Groups[1].Value) : node;
            default:
                return node;
        }
    }

    /// <summary>A reference to an earlier result: <c>{from:find_products:data.products.0.productId}</c>.</summary>
    private static readonly Regex Reference = new(@"^\{from:(?<tool>[a-z_]+):(?<path>[^}]+)\}$", RegexOptions.CultureInvariant);

    private static JsonNode? Resolve(JsonNode? node, IReadOnlyList<ChatMessage> conversation)
    {
        switch (node)
        {
            // Only a reference is replaced: a node kept is not set again, because a
            // node cannot be given to the parent it already has.
            case JsonObject properties:
                foreach (var name in properties.Select(property => property.Key).ToList())
                {
                    var child = properties[name];
                    if (Resolve(child, conversation) is var resolved && !ReferenceEquals(child, resolved))
                        properties[name] = resolved;
                }

                return properties;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    var child = items[index];
                    if (Resolve(child, conversation) is var resolved && !ReferenceEquals(child, resolved))
                        items[index] = resolved;
                }

                return items;
            case JsonValue value when value.TryGetValue<string>(out var text) && Reference.Match(text) is { Success: true } reference:
                return Lookup(reference.Groups["tool"].Value, reference.Groups["path"].Value, conversation)
                    ?? throw new InvalidOperationException($"The script refers to {text}, which no earlier result holds.");
            default:
                return node;
        }
    }

    /// <summary>The value at a dotted path of a tool's latest result; numbers index arrays.</summary>
    private static JsonNode? Lookup(string tool, string path, IReadOnlyList<ChatMessage> conversation)
    {
        var names = conversation.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
            .ToDictionary(call => call.CallId, call => call.Name);
        var result = conversation.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .LastOrDefault(found => names.GetValueOrDefault(found.CallId) == tool);
        if (result is null)
            return null;

        var node = JsonNode.Parse(Text(result.Result));
        foreach (var segment in path.Split('.'))
        {
            node = node switch
            {
                JsonArray items when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < items.Count
                    => items[index],
                JsonObject properties => properties[segment],
                _ => null,
            };
            if (node is null)
                return null;
        }

        return node?.DeepClone();
    }

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
