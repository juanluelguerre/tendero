using System.Text.Json;
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonElement Schema =
        JsonSerializer.SerializeToElement(JsonSchemaExporter.GetJsonSchemaAsNode(Json, typeof(TArgs)));

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
