namespace Tendero.Persistence.Outbox;

/// <summary>
/// Fila de la bandeja de salida. Se escribe en la MISMA transacción que el
/// cambio de estado que la originó: si el commit falla, el efecto lateral no
/// existe; si tiene éxito, el worker acabará ejecutándolo (CLAUDE.md, 7).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }

    /// <summary>"Namespace.Tipo, Ensamblado" — suficiente para Type.GetType sin
    /// atarse a la versión del ensamblado.</summary>
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
        Error = error.Length > 2000 ? error[..2000] : error;
    }
}
