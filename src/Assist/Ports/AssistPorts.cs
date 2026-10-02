using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Ports;

public interface IProposalRepository
{
    Task<ActionProposal?> FindAsync(ProposalId id, CancellationToken cancellationToken = default);

    void Add(ActionProposal proposal);

    /// <summary>The newest first, a page at a time, of those in any of the statuses
    /// (all of them when null), and how many there are.</summary>
    Task<(IReadOnlyList<ActionProposal> Items, int Total)> PageAsync(
        IReadOnlyCollection<ProposalStatus>? statuses, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The proposals with these ids, in no particular order; an unknown id is skipped.</summary>
    Task<IReadOnlyList<ActionProposal>> FindManyAsync(IReadOnlyCollection<ProposalId> ids, CancellationToken cancellationToken = default);

    /// <summary>How many proposals are in each status, every status there is.</summary>
    Task<IReadOnlyDictionary<ProposalStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
}
