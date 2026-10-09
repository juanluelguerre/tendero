using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Assist.Features.ProposeActions;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.Assist.Steward;

/// <param name="Rows">Rows the read carried; the panel says "read … · 12 rows".</param>
/// <param name="Tables">What the panel draws of those rows, built by the read's own context.</param>
public sealed record ToolUse(string Tool, int Rows, int? Total, IReadOnlyList<ResultTable>? Tables = null);

/// <summary>What one turn's tools did: what was read, what was proposed, what failed.</summary>
public sealed class StewardTrace
{
    private readonly List<ToolUse> reads = [];
    private readonly List<string> proposed = [];
    private readonly HashSet<string> asked = new(StringComparer.Ordinal);

    public IReadOnlyList<ToolUse> Reads => this.reads;

    /// <summary>The ids of the proposals stored, as the wire carries them.</summary>
    public IReadOnlyList<string> Proposed => this.proposed;

    internal void Read(ToolUse use) => this.reads.Add(use);

    /// <summary>Whether this read, with these arguments, was already answered in this turn.</summary>
    internal bool Answered(string asking) => this.asked.Contains(asking);

    internal void Answer(string asking) => this.asked.Add(asking);

    internal void Stored(ProposalId id) => this.proposed.Add(id.ToString());
}

/// <summary>
/// Steward's tools, a closed set (the spec, *What Steward reads* and *What Steward
/// proposes*): the contexts' reads for a shopkeeper (<see cref="IReadTool"/>,
/// <see cref="ToolAudience.Shopkeeper"/> — a shopper's read is never Steward's), and one proposal tool
/// per proposable command, generated from its payload's schema. Nothing here
/// approves or rejects — no tool can.
/// </summary>
public static class StewardTools
{
    public static IReadOnlyList<AIFunction> For(
        IEnumerable<IReadTool> reads, ProposableCommands proposals, ICommandDispatcher commands, ProposalSource source, StewardTrace trace) =>
    [
        .. reads.Where(read => read.Audience == ToolAudience.Shopkeeper).Select(read => (AIFunction)new JsonTool(read.Name, read.Description, read.ArgumentsSchema,
            (arguments, ct) => ReadAsync(read, arguments, trace, ct))),
        // A command that takes seeing its subject is a job's to propose: this turn
        // carries no picture, so it is not offered one to describe.
        .. proposals.All.Where(entry => !entry.OnlyFromAJob).Select(entry => (AIFunction)new JsonTool(
            "propose_" + JsonNamingPolicy.SnakeCaseLower.ConvertName(entry.Name),
            $"Propose to {entry.Label.In("en").ToLowerInvariant()}: one item per change, each with the reason a person will read. Nothing changes until a person approves.",
            ProposalSchema(entry.PayloadSchema),
            (arguments, ct) => ProposeAsync(entry, arguments, commands, source, trace, ct))),
    ];

    /// <summary>
    /// The read's answer inside a data envelope: catalogue text — a product's name
    /// written by an importer — reaches the model marked as data, which the system
    /// prompt says is never an instruction.
    ///
    /// **The same read again in a turn is not answered twice.** A small model
    /// may ask again for what it was just given, and the rows would then sit in
    /// its window twice: fifty catalogue gaps are about 4,000 tokens of 8,192,
    /// and one measured turn that read them reached 9,577 and failed. Whether a
    /// read was made is code's to know (invariant 12), so the second answer is a
    /// line saying where the rows are.
    /// A read that was refused was not made, and is refused again in its own
    /// words.
    /// </summary>
    private static async Task<object?> ReadAsync(IReadTool read, JsonElement arguments, StewardTrace trace, CancellationToken cancellationToken)
    {
        var asking = $"{read.Name} {Canonical(arguments)}";
        if (trace.Answered(asking))
        {
            Activity.Current?.SetTag("tendero.tool.repeated", true);
            return JsonSerializer.SerializeToElement(new { repeated = RepeatedRead }, ArgumentSchemas.Json);
        }

        var answer = await read.ReadAsync(arguments, cancellationToken);
        trace.Answer(asking);
        trace.Read(new ToolUse(read.Name, answer.Rows, answer.Total, answer.Tables));
        Activity.Current?.SetTag("tendero.tool.rows", answer.Rows);

        return JsonSerializer.SerializeToElement(new { data = answer.Data, rows = answer.Rows, total = answer.Total }, ArgumentSchemas.Json);
    }

    /// <summary>What a read asked twice is told. English, as every tool's words to the model are.</summary>
    public const string RepeatedRead =
        "You already made this read in this turn, with these arguments: its rows are in the conversation above. Answer from them; do not read it again.";

    /// <summary>The arguments as one text whatever order they were written in: the same question is the same key.</summary>
    private static string Canonical(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
            ? string.Join('&', arguments.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value.GetRawText()}"))
            : arguments.GetRawText();

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
    public static int Size(IEnumerable<AIFunction> tools) => JsonTool.Size(tools);
}
