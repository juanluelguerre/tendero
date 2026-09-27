using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Ports;

public interface IAgentRepository
{
    Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default);

    /// <summary>Every registered agent, the most recently changed first — there are a handful, not thousands.</summary>
    Task<IReadOnlyList<Agent>> AllAsync(CancellationToken ct = default);

    void Add(Agent agent);
}

/// <summary>
/// Agent id to standing, and nothing else — the <see cref="ICustomerDirectory"/>
/// of agents, for the same single caller: the principal accessor, on every
/// request an agent makes. A read, never a write: an agent seen for the first
/// time is <see cref="AgentStanding.Unknown"/>, not a row.
/// </summary>
public interface IAgentDirectory
{
    Task<AgentStanding?> StandingOfAsync(AgentId id, CancellationToken ct = default);
}
