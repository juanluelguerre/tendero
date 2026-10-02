using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Ports;

public interface IProposalRepository
{
    Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default);

    void Add(ActionProposal proposal);
}
