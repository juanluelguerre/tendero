using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

internal sealed class EfProposalRepository(TenderoDbContext context) : IProposalRepository
{
    public async Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default) =>
        await context.Proposals.FindAsync([id], cancellationToken);

    public void Add(ActionProposal proposal) => context.Proposals.Add(proposal);

    public async Task<(IReadOnlyList<ActionProposal> Items, int Total)> PageAsync(
        IReadOnlyCollection<ProposalStatus>? statuses, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var matching = context.Proposals.AsNoTracking()
            .Where(proposal => statuses == null || statuses.Contains(proposal.Status));
        var items = await matching
            .OrderByDescending(proposal => proposal.ProposedAt).ThenByDescending(proposal => proposal.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, await matching.CountAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<ActionProposal>> FindManyAsync(IReadOnlyCollection<ProposalId> ids, CancellationToken cancellationToken = default) =>
        await context.Proposals.AsNoTracking().Where(proposal => ids.Contains(proposal.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<ProposalStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default) =>
        await context.Proposals.AsNoTracking()
            .GroupBy(proposal => proposal.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);
}
