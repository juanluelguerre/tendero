using System.Text.Json;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tests;

internal sealed class InMemoryProposals : IProposalRepository
{
    public List<ActionProposal> All { get; } = [];

    public Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(All.SingleOrDefault(proposal => proposal.Id == id));

    public void Add(ActionProposal proposal) => All.Add(proposal);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>A unit of work that fails on the saves a test names, counting from one.</summary>
internal sealed class ScriptedUnitOfWork : IUnitOfWork
{
    private int saves;

    public Func<int, Exception?> FailOn { get; set; } = _ => null;

    /// <summary>The saves that reached the store, as EF's would: a cancelled token is refused.</summary>
    public int Saved { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);
        if (FailOn(++saves) is { } failure)
            return Task.FromException(failure);
        Saved++;
        return Task.CompletedTask;
    }
}

internal sealed class FixedPrincipal(string name) : IPrincipalAccessor
{
    public CommercePrincipal Current { get; } = new(null, null, name.ToLowerInvariant(), name, ["shopkeeper"]);
}

/// <summary>
/// A proposable command whose world is a dictionary: subject → value. What it
/// does on apply is set per subject, so one test can have a row that applies, a
/// row that refuses and a row that throws. Its sets are static, so every test
/// class that uses it shares one collection and clears them.
/// </summary>
internal sealed class FakeEntry(Dictionary<string, string> world, ProposalScope scope) : IProposableCommand
{
    public const string CommandName = "SetValue";

    public static HashSet<string> Refusing { get; } = [];
    public static HashSet<string> Throwing { get; } = [];
    public static List<(string Subject, ProposalId? Proposal)> Applied { get; } = [];

    /// <summary>Runs as a subject is applied: the moment a client disconnects.</summary>
    public static Action<string>? OnApply { get; set; }

    /// <summary>Subjects whose "now" is not JSON — an entry that skipped the typed base.</summary>
    public static HashSet<string> NotJson { get; } = [];

    public string Name => CommandName;

    public LocalizedText Label { get; } = new(new Dictionary<string, string> { ["en"] = "Set a value", ["es"] = "Poner un valor" });

    public Task<ProposalCheck> CheckAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var (subject, value) = Read(payload);
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(ProposalCheck.Invalid("Value must not be blank."));
        return Task.FromResult(Snapshot(world, subject) is { } now
            ? ProposalCheck.Valid(subject, now)
            : ProposalCheck.Invalid($"No {subject}."));
    }

    public Task<string?> NowAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var subject = Read(payload).Subject;
        return Task.FromResult(NotJson.Contains(subject) ? "not json" : Snapshot(world, subject));
    }

    public Task<ProposalApplication> ApplyAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var (subject, value) = Read(payload);
        if (Throwing.Contains(subject))
            throw new TimeoutException($"{subject} timed out.");
        if (Refusing.Contains(subject))
            return Task.FromResult(ProposalApplication.Failed("Archived"));

        world[subject] = value;
        Applied.Add((subject, scope.Current));
        OnApply?.Invoke(subject);
        return Task.FromResult(ProposalApplication.Done);
    }

    /// <summary>JSON, as every entry's snapshot is: the base class serialises one.</summary>
    public static string? Snapshot(Dictionary<string, string> world, string subject) =>
        world.TryGetValue(subject, out var value) ? JsonSerializer.Serialize(new { value, subject }) : null;

    public static JsonElement Payload(string subject, string value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { subject, value })).RootElement.Clone();

    private static (string Subject, string Value) Read(JsonElement payload) =>
        (payload.GetProperty("subject").GetString()!, payload.GetProperty("value").GetString()!);
}
