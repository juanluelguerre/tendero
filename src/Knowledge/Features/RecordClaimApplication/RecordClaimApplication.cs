using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Features.RecordClaimApplication;

/// <summary>
/// What Catalog answered when an approved claim was applied (P10-6), recorded on
/// the claim: the value as Catalog holds it, or why it would not. Sent by the
/// worker that composes both contexts, with values only — Catalog never touches
/// a claim, and Knowledge never touches a product (ADR 0014, 0018).
/// </summary>
public sealed record RecordClaimApplicationCommand(ClaimId ClaimId, string? HeldAs, string? Refusal) : ICommand<bool>;

public sealed class RecordClaimApplicationHandler(IKnowledgeRepository knowledge, IUnitOfWork unitOfWork)
    : ICommandHandler<RecordClaimApplicationCommand, bool>
{
    /// <returns>False when the claim is no longer approved — superseded between the approval and its application — and nothing was recorded.</returns>
    public async Task<bool> HandleAsync(RecordClaimApplicationCommand command, CancellationToken cancellationToken)
    {
        var claim = await knowledge.FindClaimAsync(command.ClaimId, cancellationToken);
        if (claim is not { Status: ClaimStatus.Approved, AttributeCode: not null })
            return false;

        claim.RecordApplication(command.HeldAs, command.Refusal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
