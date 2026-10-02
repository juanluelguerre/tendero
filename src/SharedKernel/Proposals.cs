using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A proposal's identity. In the kernel and not in Assist because the audit log
/// carries it: a command run for an approved proposal writes this id into its
/// row, which is what lets the log say who proposed a change and who approved it.
/// </summary>
public readonly record struct ProposalId(Guid Value)
{
    public static ProposalId New(DateTimeOffset at) => new(Guid.CreateVersion7(at));

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The proposal the commands of this DI scope run for, read by the audit step.
///
/// Approving runs each proposal in a scope of its own, so a scope carries one
/// proposal and a second one is refused: a row filed under the wrong proposal
/// would name the wrong approver for a change.
/// </summary>
public sealed class ProposalScope
{
    public ProposalId? Current { get; private set; }

    public void Enter(ProposalId id)
    {
        if (Current is { } current && current != id)
            throw new InvalidOperationException($"This scope already runs proposal {current}.");

        Current = id;
    }
}

/// <summary>
/// A command a model may propose and a person may approve (phase 12): its stable
/// name, its label, how to check a payload, how to read what it would change,
/// and how to run it.
///
/// A port in the kernel because the command belongs to its context — Catalog
/// knows what translating a product means — and Assist must not reference
/// Catalog. Adding a proposable command is a class in the owning context and
/// one registration (invariant 3). Approving or rejecting is never one: an
/// architecture rule holds that.
/// </summary>
public interface IProposableCommand
{
    /// <summary>Stable, stored in every proposal: renaming it orphans them.</summary>
    string Name { get; }

    /// <summary>What a person reads: "Translate a product".</summary>
    LocalizedText Label { get; }

    /// <summary>The payload's JSON schema: what a model is shown it must write.</summary>
    JsonElement PayloadSchema { get; }

    Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>A snapshot of what the command would change, as it is now, in JSON
    /// (compared by value, never as text); null when the subject no longer exists
    /// or the payload cannot be read.</summary>
    Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default);

    Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default);
}

/// <param name="Subject">What the proposal is about, as a value (a product's code).</param>
/// <param name="Now">The snapshot <see cref="IProposableCommand.NowAsync"/> returns, taken when proposed.</param>
/// <param name="Display">What a person reads of it, when the entry writes one.</param>
public sealed record ProposalCheck(string? Subject, string? Now, IReadOnlyList<string> Errors, ProposalDisplay? Display = null)
{
    public bool IsValid => Errors.Count == 0;

    public static ProposalCheck Valid(string subject, string now, ProposalDisplay? display = null) => new(subject, now, [], display);

    public static ProposalCheck Invalid(params string[] errors) => new(null, null, errors);
}

public sealed record ProposalApplication(bool Applied, string? Reason)
{
    public static readonly ProposalApplication Done = new(true, null);

    public static ProposalApplication Failed(string reason) => new(false, reason);
}

/// <param name="Now">The fields the command would change, as they are: serialised
/// into the snapshot staleness compares.</param>
/// <param name="Display">What a person reads of the proposal: written here, by the
/// context that knows its subject, and stored with it.</param>
public sealed record ProposalSubject(string Subject, object Now, ProposalDisplay? Display = null);

/// <summary>One field a proposal changes, as a person reads it: "Name · EN", now, proposed.</summary>
public sealed record ProposalChange(LocalizedText Field, string? Now, string? Proposed);

/// <summary>
/// What a person reads of a proposal (S-4): its subject's title, the kind of
/// change ("English name"), and each field now and proposed. The owning context
/// writes it when the proposal is made and Assist stores it as it came, so the
/// card, the review and the list show a product's name without Assist reading
/// Catalog (invariant 1) — and show what was proposed, as it was proposed.
/// </summary>
public sealed record ProposalDisplay(LocalizedText Title, LocalizedText Kind, IReadOnlyList<ProposalChange> Changes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(
        new Stored(Title.Values, Kind.Values, [.. Changes.Select(change => new StoredChange(change.Field.Values, change.Now, change.Proposed))]),
        Json);

    /// <summary>The display a proposal stored, or null when it has none or it
    /// cannot be read: a list shows the proposal without it, never fails on it.</summary>
    public static ProposalDisplay? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Stored>(json, Json) is { Title.Count: > 0, Kind.Count: > 0 } stored
                ? new ProposalDisplay(new LocalizedText(stored.Title), new LocalizedText(stored.Kind),
                [
                    .. (stored.Changes ?? []).Where(change => change.Field is { Count: > 0 })
                        .Select(change => new ProposalChange(new LocalizedText(change.Field), change.Now, change.Proposed)),
                ])
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Stored(
        IReadOnlyDictionary<string, string> Title, IReadOnlyDictionary<string, string> Kind, IReadOnlyList<StoredChange>? Changes);

    private sealed record StoredChange(IReadOnlyDictionary<string, string> Field, string? Now, string? Proposed);
}

/// <summary>
/// What every proposable command does the same way: read the payload into the
/// command, run the command's own validators — the ones its endpoint runs — and
/// dispatch it, so the audit, the validation and the handler are the ones a
/// person's click goes through. A derived class says only how to build the
/// command and how to read its subject.
/// </summary>
public abstract class ProposableCommand<TPayload, TCommand, TResult>(IServiceProvider services) : IProposableCommand
    where TPayload : class
    where TCommand : ICommand<TResult>
    where TResult : IAuditedResult
{
    /// <summary>Fixed, so the same state always writes the same snapshot; Assist
    /// still compares two of them by value, since jsonb keeps neither spacing nor order.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }

    public abstract LocalizedText Label { get; }

    public JsonElement PayloadSchema { get; } = ArgumentSchemas.Of(typeof(TPayload));

    protected abstract TCommand ToCommand(TPayload payload);

    /// <summary>The subject and what the command would change, or null when the
    /// subject does not exist.</summary>
    protected abstract Task<ProposalSubject?> ReadAsync(TCommand command, CancellationToken cancellationToken);

    public async Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (Read(payload) is not { } command)
            return ProposalCheck.Invalid($"The payload is not a {Name}.");

        var errors = new List<string>();
        foreach (var validator in services.GetServices<IValidator<TCommand>>())
            errors.AddRange((await validator.ValidateAsync(command, cancellationToken)).Errors.Select(error => error.ErrorMessage));

        if (errors.Count > 0)
            return ProposalCheck.Invalid([.. errors]);

        return await ReadAsync(command, cancellationToken) is { } subject
            ? ProposalCheck.Valid(subject.Subject, Snapshot(subject.Now), subject.Display)
            : ProposalCheck.Invalid($"The subject of this {Name} does not exist.");
    }

    public async Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default) =>
        Read(payload) is { } command && await ReadAsync(command, cancellationToken) is { } subject
            ? Snapshot(subject.Now)
            : null;

    public async Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (Read(payload) is not { } command)
            return ProposalApplication.Failed($"The payload is not a {Name}.");

        IAuditedResult result = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken);

        return result.AuditOutcome == AuditOutcome.Allowed
            ? ProposalApplication.Done
            : ProposalApplication.Failed(result.AuditReason ?? result.AuditOutcome.ToString());
    }

    private TCommand? Read(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<TPayload>(Json) is { } read ? ToCommand(read) : default;
        }
        catch (Exception unreadable) when (unreadable is JsonException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }

    private static string Snapshot(object now) => JsonSerializer.Serialize(now, Json);
}
