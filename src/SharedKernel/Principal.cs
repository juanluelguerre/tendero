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

    /// <summary>
    /// The tariff the shop has this customer on, as Accounts holds it. Null for
    /// a guest, an agent, or somebody who signed in and has not been registered
    /// yet — all three are quoted the default.
    /// </summary>
    public string? Segment { get; init; }

    public bool IsAgent => Agent is not null;

    /// <summary>
    /// A test credential: whatever it places is a rehearsal (ADR 0038, P11-0). Read
    /// from the token the identity provider signed — <see cref="TenderoClaims.Mode"/>
    /// — never from a request, so an agent cannot choose to be charged or not.
    /// </summary>
    public bool IsTest { get; init; }

    /// <summary>
    /// What the shop knows about the agent behind this principal — how far it is
    /// trusted and whether it is suspended — as Accounts holds it (P11-2). Null
    /// for a person; <see cref="AgentStanding.Unknown"/> for an agent with a valid
    /// token the shop never registered. A value, not the registration: the rules
    /// that read it (P11-6) need two facts, not an aggregate.
    /// </summary>
    public AgentStanding? Standing { get; init; }

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

/// <summary>The claims Tendero reads from a token beyond OIDC's own.</summary>
public static class TenderoClaims
{
    /// <summary>The agent a token was issued to, when it was issued to one.</summary>
    public const string Agent = "agent_id";

    /// <summary><c>test</c> on a test credential (ADR 0038); absent on every other.</summary>
    public const string Mode = "tendero_mode";

    public const string TestMode = "test";
}

/// <summary>
/// How far the shop trusts an agent (Know Your Agent). A valid token says who an
/// agent is; this says what the shop thinks of it.
/// </summary>
public enum AgentTrust
{
    /// <summary>A valid token and nothing else: the shop has never heard of it.</summary>
    Untrusted,

    /// <summary>Registered by a shopkeeper, who knows who operates it.</summary>
    Known,

    /// <summary>Known, and its operator's identity checked beyond the registration.</summary>
    Verified,
}

/// <summary>An agent's standing with the shop, as the principal carries it.</summary>
public sealed record AgentStanding(AgentTrust Trust, bool Suspended)
{
    /// <summary>An agent the shop never registered: untrusted, and not suspended — there is nothing to suspend.</summary>
    public static readonly AgentStanding Unknown = new(AgentTrust.Untrusted, false);
}
