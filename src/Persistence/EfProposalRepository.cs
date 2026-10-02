using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Persistence;

internal sealed class EfProposalRepository(TenderoDbContext context) : IProposalRepository
{
    public async Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default) =>
        await context.Proposals.FindAsync([id], cancellationToken);

    public void Add(ActionProposal proposal) => context.Proposals.Add(proposal);
}
