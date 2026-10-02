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
    /// <summary>The tool's name as the model sees it: snake_case, stable.</summary>
    string Name { get; }

    /// <summary>What it answers, in the model's words.</summary>
    string Description { get; }

    JsonElement ArgumentsSchema { get; }

    /// <summary>An argument the model got wrong is an <see cref="ArgumentException"/>
    /// it can read, never a fault.</summary>
    Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <param name="Data">The tool's own DTO: codes, names, figures — never a customer.</param>
/// <param name="Rows">How many rows it carries, for the panel's "read … · 12 rows".</param>
/// <param name="Total">How many there were in all, when the read is capped.</param>
public sealed record ReadAnswer(object Data, int Rows, int? Total);

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

    public abstract string Name { get; }

    public abstract string Description { get; }

    public JsonElement ArgumentsSchema => Schema;

    protected abstract Task<(TData Data, int Rows, int? Total)> ReadAsync(
        TArgs arguments, IQueryDispatcher queries, CancellationToken cancellationToken);

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
            return new ReadAnswer(data, rows, total);
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
    /// Optional is what <c>required</c> already says; null adds nothing a model needs.
    /// </summary>
    private static JsonNode OneTypeEach(JsonNode schema)
    {
        schema["type"] = "object";
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (_, property) in properties)
            {
                if (property is JsonObject node && node["type"] is JsonArray types &&
                    types.Select(type => type?.GetValue<string>()).FirstOrDefault(type => type is not "null") is { } single)
                    node["type"] = single;
            }
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
