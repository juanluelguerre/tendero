namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// How a command ended.
///
/// `Denied` is separate from `Failed` because they are different questions. A
/// failure is the system breaking; a denial is the system WORKING — a rule said
/// no, and which rule it was is the most interesting column in the table. Phase
/// 11's agent policy writes nothing but denials, and an audit log that filed
/// them under "error" would make the agent activity panel unbuildable.
/// </summary>
public enum AuditOutcome
{
    Allowed,
    Denied,
    Failed
}

/// <summary>
/// One thing somebody did, and what happened.
///
/// **One writer, three readers**: the audit screen, phase 11's agent activity
/// panel and phase 12's copilot. Getting it right once is why it is a dispatcher
/// decorator rather than logging sprinkled through handlers — a handler that
/// remembers to audit is a handler that will one day forget.
///
/// Commands only. A query is not an action, and auditing reads would bury the
/// hundred rows that matter under a hundred thousand that do not.
/// </summary>
public sealed record AuditEntry(
    Guid Id,
    string CommandType,
    string Payload,
    CustomerId? Customer,
    AgentId? Agent,

    /// <summary>The stable identifier. Opaque with a real issuer, and the thing
    /// two rows are correlated on.</summary>
    string? Subject,

    /// <summary>
    /// What that subject was CALLED when this happened, snapshotted rather than
    /// resolved on read.
    ///
    /// It is the snapshot rule (ADR 0002) applied a fourth time, and for the
    /// reason the other three were: a log that joined to the customer table
    /// would rewrite its own history the day somebody changed their display
    /// name. An audit row says what was true then.
    /// </summary>
    string? ActorName,

    AuditOutcome Outcome,
    string? Reason,
    string? TraceId,
    DateTimeOffset At)
{
    public static AuditEntry For(
        string commandType,
        string payload,
        CommercePrincipal principal,
        AuditOutcome outcome,
        string? reason,
        string? traceId,
        DateTimeOffset at) =>
        new(Guid.CreateVersion7(at),
            commandType,
            payload,
            principal.Customer,
            principal.Agent,
            principal.Subject,
            principal.DisplayName,
            outcome,
            reason,
            traceId,
            at);
}

/// <summary>
/// Where an audit entry goes.
///
/// A port in the SharedKernel because the decorator lives beside the dispatcher
/// and cannot reference a context. The adapter is in Persistence, like every
/// other reader and writer.
///
/// **It writes on its OWN connection, outside whatever transaction the command
/// used**, and that is the load-bearing detail. An audit row written inside the
/// command's transaction disappears when the command fails — so the log would
/// hold every success and no denial, which is the exact inverse of what it is
/// for. The row you most want is the one whose transaction rolled back.
///
/// The cost of that choice is stated rather than hidden: a process that dies
/// between the command committing and this returning loses a row. An audit log
/// that is complete about refusals and occasionally short of a success is the
/// better failure of the two, and the alternative — a second outbox — buys
/// durability for a log nobody reads in real time.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// A page of the audit log, newest first.
///
/// Reading it is a separate port from writing it, and not out of symmetry: the
/// writer runs on every command and must never do more than one insert, while
/// the reader is a screen somebody opens occasionally and filters. One interface
/// with both would be a port whose two halves have nothing in common but a
/// table.
/// </summary>
public interface IAuditReader
{
    Task<IReadOnlyList<AuditEntry>> RecentAsync(
        AuditOutcome? outcome, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a property that must never reach the audit log.
///
/// Not speculative: `ClaimGuestAccount` and checkout both carry a CART TOKEN,
/// which is a 256-bit bearer credential — a log holding one is a log that can
/// be used to take over somebody's basket. The value is replaced with a fixed
/// marker rather than dropped, so the row still shows that the field was there.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditRedactedAttribute : Attribute;
