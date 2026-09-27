using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Accounts' agent registry, and the directory the principal accessor reads — one
/// class over one table for the reason <see cref="EfCustomerRepository"/> is, the
/// two interfaces kept apart for the same reason too.
/// </summary>
internal sealed class EfAgentRepository(TenderoDbContext context) : IAgentRepository, IAgentDirectory
{
    public Task<Agent?> FindByIdAsync(AgentId id, CancellationToken ct = default) =>
        context.Agents.FirstOrDefaultAsync(agent => agent.Id == id, ct);

    public async Task<IReadOnlyList<Agent>> AllAsync(CancellationToken ct = default) =>
        await context.Agents.AsNoTracking().OrderByDescending(agent => agent.UpdatedAt).ToListAsync(ct);

    public void Add(Agent agent) => context.Agents.Add(agent);

    /// <summary>Two columns on the primary key, untracked, and never a write: an agent seen for the first time is not registered by being seen.</summary>
    public Task<AgentStanding?> StandingOfAsync(AgentId id, CancellationToken ct = default) =>
        context.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == id)
            .Select(agent => new AgentStanding(agent.Trust, agent.Status == AgentStatus.Suspended))
            .FirstOrDefaultAsync(ct);
}
