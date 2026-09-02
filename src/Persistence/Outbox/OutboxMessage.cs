namespace ElGuerre.Tendero.Persistence.Outbox;

/// <summary>
/// A row in the outbox. It is written in the SAME transaction as the state
/// change that raised it: if the commit fails the side effect does not exist; if
/// it succeeds, the worker will eventually run it (CLAUDE.md, 7).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }

    /// <summary>"Namespace.Type, Assembly" — enough for Type.GetType without
    /// tying itself to the assembly's version.</summary>
    public string Type { get; private set; } = default!;

    public string Payload { get; private set; } = default!;

    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    private OutboxMessage() { } // EF Core

    public static OutboxMessage For(string type, string payload, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Payload = payload,
        OccurredAt = occurredAt
    };

    public void MarkProcessed()
    {
        ProcessedAt = DateTimeOffset.UtcNow;
        Attempts++;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        // Truncated from the front: the exception type and the first lines of the
        // trace are what say what happened; the tail of a deep trace is not.
        Error = error.Length > 4000 ? error[..4000] : error;
    }
}
