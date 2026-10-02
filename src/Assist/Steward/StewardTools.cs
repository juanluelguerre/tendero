using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.Assist.Steward;

/// <param name="Rows">Rows the read carried; the panel says "read … · 12 rows".</param>
public sealed record ToolUse(string Tool, int Rows, int? Total);

/// <summary>What one turn's tools did: what was read, what was proposed, what failed.</summary>
public sealed class StewardTrace
{
    private readonly List<ToolUse> reads = [];
    private readonly List<string> proposed = [];

    public IReadOnlyList<ToolUse> Reads => this.reads;

    /// <summary>The ids of the proposals stored, as the wire carries them.</summary>
    public IReadOnlyList<string> Proposed => this.proposed;

    internal void Read(ToolUse use) => this.reads.Add(use);

    internal void Stored(ProposalId id) => this.proposed.Add(id.ToString());
}

/// <summary>
/// Steward's tools, a closed set (the spec, *What Steward reads* and *What Steward
/// proposes*): the contexts' reads (<see cref="IReadTool"/>), and one proposal tool
/// per proposable command, generated from its payload's schema. Nothing here
/// approves or rejects — no tool can.
/// </summary>
public static class StewardTools
{
    public static IReadOnlyList<AIFunction> For(
        IEnumerable<IReadTool> reads, ProposableCommands proposals, ICommandDispatcher commands, ProposalSource source, StewardTrace trace) =>
    [
        .. reads.Select(read => (AIFunction)new JsonTool(read.Name, read.Description, read.ArgumentsSchema,
            (arguments, ct) => ReadAsync(read, arguments, trace, ct))),
        .. proposals.All.Select(entry => (AIFunction)new JsonTool(
            "propose_" + JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Name),
            $"Propose to {entry.Label.In("en").ToLowerInvariant()}: one item per change, each with the reason a person will read. Nothing changes until a person approves.",
            ProposalSchema(entry.PayloadSchema),
            (arguments, ct) => ProposeAsync(entry, arguments, commands, source, trace, ct))),
    ];

    /// <summary>
    /// The read's answer inside a data envelope: catalogue text — a product's name
    /// written by an importer — reaches the model marked as data, which the system
    /// prompt says is never an instruction.
    /// </summary>
    private static async Task<object?> ReadAsync(IReadTool read, JsonElement arguments, StewardTrace trace, CancellationToken cancellationToken)
    {
        var answer = await read.ReadAsync(arguments, cancellationToken);
        trace.Read(new ToolUse(read.Name, answer.Rows, answer.Total));
        Activity.Current?.SetTag("tendero.tool.rows", answer.Rows);

        return JsonSerializer.SerializeToElement(new { data = answer.Data, rows = answer.Rows, total = answer.Total }, ArgumentSchemas.Json);
    }

    private static async Task<object?> ProposeAsync(
        IProposableCommand entry, JsonElement arguments, ICommandDispatcher commands, ProposalSource source, StewardTrace trace,
        CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("A proposal tool takes { items: [ … ] }.");

        var actions = items.EnumerateArray().Select(item =>
        {
            var payload = JsonNode.Parse(item.GetRawText()) as JsonObject ?? throw new ArgumentException("Each item is an object.");
            var reason = payload["reason"] switch
            {
                null => "",
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                _ => throw new ArgumentException("Each item's reason is text: why, as a person will read it."),
            };
            payload.Remove("reason");
            return new ProposedAction(entry.Name, JsonSerializer.SerializeToElement(payload), reason);
        }).ToList();

        ProposeActionsResult result;
        try
        {
            result = await commands.SendAsync(new ProposeActionsCommand(actions, source), cancellationToken);
        }
        catch (ValidationException refused)
        {
            throw new ArgumentException(string.Join(" ", refused.Errors.Select(error => error.ErrorMessage)), refused);
        }

        foreach (var row in result.Rows.Where(row => row.Id is not null))
            trace.Stored(row.Id!.Value);

        return JsonSerializer.SerializeToElement(new
        {
            proposed = result.Rows.Count(row => row.Id is not null),
            refused = result.Rows.Where(row => row.Id is null).Select(row => new { index = row.Index, errors = row.Errors }),
        }, ArgumentSchemas.Json);
    }

    /// <summary>{ items: [ payload + reason ] } — the reason required, as a person reads it.</summary>
    private static JsonElement ProposalSchema(JsonElement payload)
    {
        var item = JsonNode.Parse(payload.GetRawText()) as JsonObject ?? new JsonObject();
        item["type"] = "object";
        var properties = item["properties"] as JsonObject ?? new JsonObject();
        properties["reason"] = new JsonObject { ["type"] = "string", ["description"] = "Why, in the conversation's language." };
        item["properties"] = properties;
        var required = item["required"] as JsonArray ?? new JsonArray();
        required.Add("reason");
        item["required"] = required;

        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["items"] = new JsonObject { ["type"] = "array", ["items"] = item } },
            ["required"] = new JsonArray("items"),
        });
    }

    /// <summary>The characters the tools add to every call — names, descriptions,
    /// schemas — which a turn counts against the window before it asks.</summary>
    public static int Size(IEnumerable<AIFunction> tools) =>
        tools.Sum(tool => tool.Name.Length + tool.Description.Length + tool.JsonSchema.GetRawText().Length);

    /// <summary>
    /// A function whose schema is given, not inferred: what the contexts declared is
    /// exactly what the model is shown. A model's mistake — arguments a read or a
    /// proposal refuses — is its answer, with the reason, so the model can ask again
    /// (ADR 0054); any other failure is the shop's, and the function loop gives the
    /// model only that it failed.
    /// </summary>
    private sealed class JsonTool(
        string name, string description, JsonElement schema, Func<JsonElement, CancellationToken, Task<object?>> invoke) : AIFunction
    {
        public override string Name => name;

        public override string Description => description;

        public override JsonElement JsonSchema => schema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            try
            {
                return await invoke(JsonSerializer.SerializeToElement(
                    arguments.ToDictionary(pair => pair.Key, pair => pair.Value), ArgumentSchemas.Json), cancellationToken);
            }
            catch (ArgumentException mistake)
            {
                return JsonSerializer.SerializeToElement(new { error = mistake.Message }, ArgumentSchemas.Json);
            }
        }
    }
}
