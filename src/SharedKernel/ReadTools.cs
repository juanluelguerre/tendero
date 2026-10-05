using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using FluentValidation;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A read a context lets a model make (phase 12, ADR 0054): its name, what it
/// answers, the schema of its arguments, and the read. A port in the kernel for
/// the reason <see cref="IProposableCommand"/> is one: Assist runs the reads and
/// may not reference the contexts that own them. Each context says, class by
/// class, what a model may read of it.
/// </summary>
public interface IReadTool
{
    /// <summary>Whose agent may be handed this read. A tool says it; none is everybody's.</summary>
    ToolAudience Audience { get; }

    /// <summary>The tool's name as the model sees it: snake_case, stable.</summary>
    string Name { get; }

    /// <summary>What it answers, in the model's words.</summary>
    string Description { get; }

    JsonElement ArgumentsSchema { get; }

    /// <summary>An argument the model got wrong is an <see cref="ArgumentException"/>
    /// it can read, never a fault.</summary>
    Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who a read is for (ADR 0054, amended in phase 12b). An agent is built from the
/// reads of its own audience and no other: the shopkeeper's copilot never reads a
/// shopper's order, and the shopper's assistant never reads the shop's sales.
/// </summary>
public enum ToolAudience
{
    /// <summary>The backoffice copilot, asked by a shopkeeper: the shop's figures, and no customer.</summary>
    Shopkeeper,

    /// <summary>The storefront's assistant, asked by a shopper: what the shop sells, and that shopper's own orders.</summary>
    Shopper,
}

/// <param name="Data">The tool's own DTO: codes, names, figures — never a customer.</param>
/// <param name="Rows">How many rows it carries, for the panel's "read … · 12 rows".</param>
/// <param name="Total">How many there were in all, when the read is capped.</param>
/// <param name="Tables">What the panel draws from the same rows: built by code from the read, so
/// what is shown is what was read and not the model's retelling of it.</param>
public sealed record ReadAnswer(object Data, int Rows, int? Total, IReadOnlyList<ResultTable>? Tables = null);

/// <summary>How the panel formats a cell: a number in the reader's culture, money in the table's currency.</summary>
public enum CellKind
{
    Text,

    /// <summary>A word of the shop's own — a status, a reason — that the panel translates, and shows as it is when it cannot.</summary>
    Term,

    Number,
    Money,
    Days,
}

/// <param name="Key">What the column holds, for the panel's heading.</param>
public sealed record ResultColumn(string Key, CellKind Kind);

/// <summary>One cell: a number when the column is one, the text otherwise; empty when the read had nothing.</summary>
public sealed record ResultCell(string? Text, decimal? Number);

/// <summary>
/// A table the panel draws from a read's rows (P12-10): the columns say how each
/// is formatted, and the headings and terms are the panel's to translate. At most
/// <see cref="ReadLimits.MaxRows"/> rows, and only what the read's own DTO carries —
/// so what the architecture rule keeps customer fields out of keeps them out here.
/// </summary>
/// <param name="Key">What the table is, for its title.</param>
/// <param name="Currency">For the money columns.</param>
public sealed record ResultTable(
    string Key, string? Currency, IReadOnlyList<ResultColumn> Columns, IReadOnlyList<IReadOnlyList<ResultCell>> Rows)
{
    /// <summary>A table from plain values: a string is text, a number is a number, null is empty.</summary>
    public static ResultTable Of(
        string key, IReadOnlyList<ResultColumn> columns, IEnumerable<IReadOnlyList<object?>> rows, string? currency = null) =>
        new(key, currency, columns,
        [
            .. rows.Take(ReadLimits.MaxRows).Select(row => (IReadOnlyList<ResultCell>)
            [
                .. row.Select(value => value switch
                {
                    null => new ResultCell(null, null),
                    string text => new ResultCell(text, null),
                    _ => new ResultCell(null, Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)),
                }),
            ]),
        ]);
}

/// <summary>
/// What every read tool does the same way: read the arguments, run the read through
/// the dispatcher — the query's own validator included — and turn a mistake the
/// model made into an error it can read and correct. A derived class maps a query's
/// result into its own small DTO (<typeparamref name="TData"/>), which is what an
/// architecture rule checks for customer fields.
/// </summary>
public abstract class ReadTool<TArgs, TData>(IQueryDispatcher queries) : IReadTool
    where TArgs : class
    where TData : class
{
    private static readonly JsonSerializerOptions Json = ArgumentSchemas.Json;

    private static readonly JsonElement Schema = ArgumentSchemas.Of(typeof(TArgs));

    public abstract ToolAudience Audience { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public JsonElement ArgumentsSchema => Schema;

    protected abstract Task<(TData Data, int Rows, int? Total)> ReadAsync(
        TArgs arguments, IQueryDispatcher queries, CancellationToken cancellationToken);

    /// <summary>What the panel draws of what was read; none when a read has no rows worth a table.</summary>
    protected virtual IReadOnlyList<ResultTable> Tables(TData data) => [];

    public async Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        TArgs read;
        try
        {
            read = arguments.Deserialize<TArgs>(Json)
                   ?? throw new ArgumentException($"{Name} needs its arguments.");
        }
        catch (JsonException unreadable)
        {
            throw new ArgumentException($"{Name} could not read its arguments: {unreadable.Message}", unreadable);
        }

        try
        {
            var (data, rows, total) = await ReadAsync(read, queries, cancellationToken);
            return new ReadAnswer(data, rows, total, Tables(data));
        }
        catch (ValidationException refused)
        {
            throw new ArgumentException(
                $"{Name} refused its arguments: {string.Join(" ", refused.Errors.Select(error => error.ErrorMessage))}", refused);
        }
    }
}

/// <summary>
/// How a model's arguments are read and described, for read tools and proposal
/// tools alike: the web's names, and a closed set by its name as on the wire
/// (ADR 0043) — the schema the model is shown lists the names it may write.
/// </summary>
public static class ArgumentSchemas
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { WireJson.Enums() },
    };

    public static JsonElement Of(Type arguments) =>
        JsonSerializer.SerializeToElement(
            OneTypeEach(NamingClosedSets(arguments, JsonSchemaExporter.GetJsonSchemaAsNode(Json, arguments))));

    /// <summary>
    /// An object at the root and one type per property, as an OpenAI-compatible
    /// client takes them. The exporter writes a record and an optional argument as
    /// nullable — <c>["object","null"]</c> — and the OpenAI client refuses a type
    /// that is not a string: every turn against a real model failed on it (S-6).
    /// A nullable argument is one the model may leave out, so dropping its null
    /// takes it out of <c>required</c> too — the exporter lists a positional record's
    /// every parameter there, and a grammar that keeps it would make the model write
    /// one (a translation's description it invents, over the product's). A number
    /// the web's options also read from a string stays a number, and a null default
    /// says nothing.
    /// </summary>
    private static JsonNode OneTypeEach(JsonNode schema)
    {
        schema["type"] = "object";
        if (schema["properties"] is not JsonObject properties)
            return schema;

        var omittable = new List<string>();
        foreach (var (name, property) in properties)
        {
            if (property is not JsonObject node || node["type"] is not JsonArray types)
                continue;

            var named = types.Select(type => type?.GetValue<string>()).OfType<string>().ToList();
            if (named.Contains("null"))
                omittable.Add(name);
            var others = named.Where(type => type is not "null").ToList();
            if (others.FirstOrDefault(type => type is not "string") is { } number)
            {
                node["type"] = number;
                node.Remove("pattern");
            }
            else if (others.FirstOrDefault() is { } single)
                node["type"] = single;

            if (node.TryGetPropertyValue("default", out var value) && value is null)
                node.Remove("default");
        }

        if (schema["required"] is JsonArray required)
        {
            foreach (var name in omittable)
                required.Remove(required.FirstOrDefault(item => item?.GetValue<string>() == name));
            if (required.Count == 0)
                schema.AsObject().Remove("required");
        }

        return schema;
    }

    /// <summary>
    /// The exporter cannot see through the names-only converter and describes a
    /// closed set as nothing at all. Arguments are flat records, so their own
    /// properties are all there is to name.
    /// </summary>
    private static JsonNode NamingClosedSets(Type arguments, JsonNode schema)
    {
        if (schema["properties"] is not JsonObject properties)
            return schema;

        foreach (var property in arguments.GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!type.IsEnum || !properties.ContainsKey(name))
                continue;

            // A required one comes out as `true` — any value — and an optional one as
            // an object with its default: both become an object naming the values,
            // which is what a model is shown and what llama.cpp's grammar takes.
            if (properties[name] is not JsonObject node)
                properties[name] = node = new JsonObject();
            node["type"] = "string";
            node["enum"] = new JsonArray([.. Enum.GetNames(type).Select(value => (JsonNode)JsonValue.Create(value))]);
        }

        return schema;
    }
}
