using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Domain;

public enum AgentStatus
{
    Active,
    Suspended,
}

public sealed record AgentRegistered(AgentId AgentId, AgentTrust Trust, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AgentTrustChanged(AgentId AgentId, AgentTrust From, AgentTrust To, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AgentSuspended(AgentId AgentId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AgentReinstated(AgentId AgentId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// An agent the shop knows (Know Your Agent, P11-2): who operates it, how far it
/// is trusted, and whether it may act at all.
///
/// **An agent is a principal, not a customer** (ADR 0022). A token proves who an
/// agent is — the identity provider's job, and the only thing Tendero validates
/// (invariant 10). What the shop *thinks* of that agent is a fact the shop owns,
/// and it lives here: the same split as a customer, whose subject comes from the
/// token and whose tariff comes from Accounts.
///
/// **No keys and no client secrets here.** The identity provider holds the
/// credentials; an AP2 mandate carries the agent's key in its own `cnf` claim,
/// signed by the user. A registry that copied either would be a second source of
/// truth about something it cannot rotate.
/// </summary>
public sealed class Agent : AggregateRoot
{
    private static readonly TransitionTable<AgentStatus> AllowedTransitions = new()
    {
        [AgentStatus.Active] = [AgentStatus.Suspended],
        [AgentStatus.Suspended] = [AgentStatus.Active],
    };

    /// <summary>The token's <c>agent_id</c>: the key the principal is matched on.</summary>
    public AgentId Id { get; private set; }

    public string DisplayName { get; private set; } = "";

    /// <summary>Who runs it — a company or a person — as the shopkeeper recorded it.</summary>
    public string Operator { get; private set; } = "";

    public AgentTrust Trust { get; private set; }

    public AgentStatus Status { get; private set; }

    /// <summary>Why it was suspended, while it is; null otherwise.</summary>
    public string? SuspensionReason { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>What the principal carries: two facts, not this aggregate.</summary>
    public AgentStanding Standing => new(Trust, Status == AgentStatus.Suspended);

    private Agent() { } // EF Core

    /// <summary>
    /// A shopkeeper registers an agent they know. Registering makes it at least
    /// <see cref="AgentTrust.Known"/>: an untrusted agent is one nobody registered,
    /// so registering one as untrusted would be a contradiction the type allows and
    /// the domain does not.
    /// </summary>
    public static Agent Register(TimeProvider clock, AgentId id, string displayName, string @operator, AgentTrust trust = AgentTrust.Known)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(@operator);
        if (trust == AgentTrust.Untrusted)
            throw new InvalidOperationException("A registered agent is at least known: untrusted is what an unregistered one is.");

        var now = clock.GetUtcNow();
        var agent = new Agent
        {
            Id = id,
            DisplayName = displayName.Trim(),
            Operator = @operator.Trim(),
            Trust = trust,
            Status = AgentStatus.Active,
            RegisteredAt = now,
            UpdatedAt = now,
        };
        agent.Raise(new AgentRegistered(id, trust, now));
        return agent;
    }

    /// <summary>Known or verified; never back to untrusted — that is unregistering, and an audit trail is not unregistered.</summary>
    public void ChangeTrust(TimeProvider clock, AgentTrust trust)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (trust == AgentTrust.Untrusted)
            throw new InvalidOperationException("A registered agent is at least known; suspend it instead.");
        if (trust == Trust)
            return;

        var from = Trust;
        Trust = trust;
        UpdatedAt = clock.GetUtcNow();
        Raise(new AgentTrustChanged(Id, from, trust, UpdatedAt));
    }

    public void Suspend(TimeProvider clock, string reason)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        AllowedTransitions.EnsureAllowed(Status, AgentStatus.Suspended, $"agent {Id}");
        Status = AgentStatus.Suspended;
        SuspensionReason = reason.Trim();
        UpdatedAt = clock.GetUtcNow();
        Raise(new AgentSuspended(Id, SuspensionReason, UpdatedAt));
    }

    public void Reinstate(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        AllowedTransitions.EnsureAllowed(Status, AgentStatus.Active, $"agent {Id}");
        Status = AgentStatus.Active;
        SuspensionReason = null;
        UpdatedAt = clock.GetUtcNow();
        Raise(new AgentReinstated(Id, UpdatedAt));
    }
}
