namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// An agent's identifier. A separate type from <see cref="CustomerId"/> on
/// purpose: **an agent is a principal, not a customer**. When an agent buys on
/// somebody's behalf there are two identities in play, and collapsing them into
/// one makes the only question that matters afterwards unanswerable — who acted,
/// and on whose behalf?
/// </summary>
public readonly record struct AgentId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Who is acting. A customer always; an agent when there is one; and the mandate
/// that authorises it once one exists (phase 11, AP2).
/// </summary>
public sealed record CommercePrincipal(
    CustomerId? Customer,
    AgentId? Agent,
    string? Subject,

    /// <summary>
    /// What to call this person on a screen, which is NOT the subject.
    ///
    /// The two issuers disagree about what a subject looks like and both are
    /// right: the development issuer signs `sub: "juanlu"`, and Keycloak — like
    /// every real OIDC provider — signs an opaque UUID, because a subject has to
    /// be stable and a username is not. So an audit log that printed the subject
    /// read `juanlu` against the fake and
    /// `681c3d75-9cc6-437d-a9c2-98d1d41ecf36` against the real one.
    ///
    /// Both are kept. The subject is what you correlate on and it never changes;
    /// this is what a person reads.
    /// </summary>
    string? DisplayName,

    IReadOnlyCollection<string> Roles)
{
    /// <summary>Nobody authenticated. A value and not null: a guest browsing is
    /// a normal case, not the absence of a case.</summary>
    public static readonly CommercePrincipal Anonymous = new(null, null, null, null, []);

    public bool IsAgent => Agent is not null;

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Where the current principal comes from.
///
/// A port and not a read of <c>HttpContext</c>, because **it has to work where
/// there is no HTTP**: over MCP, and inside the worker that drains the outbox,
/// where the actor is the system. That constraint is what makes it
/// non-negotiable; with a single HTTP consumer the context would have done.
/// </summary>
public interface IPrincipalAccessor
{
    CommercePrincipal Current { get; }
}

/// <summary>
/// The actor when there is nobody: background processes. Registered explicitly
/// rather than letting <c>Current</c> return null, so that a handler never has
/// to wonder whether "no principal" means guest or means error.
/// </summary>
public sealed class SystemPrincipalAccessor : IPrincipalAccessor
{
    public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = "system", DisplayName = "system" };
}
