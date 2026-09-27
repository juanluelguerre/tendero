using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>Knowledge's documents and claims over EF Core.</summary>
internal sealed class EfKnowledgeRepository(TenderoDbContext context) : IKnowledgeRepository
{
    private static readonly ClaimStatus[] Standing = [ClaimStatus.Proposed, ClaimStatus.Approved];

    public Task<SourceDocument?> FindDocumentAsync(SourceDocumentId id, CancellationToken cancellationToken = default) =>
        context.SourceDocuments.FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SourceDocument>> CurrentDocumentsAsync(ProductId productId, CancellationToken cancellationToken = default)
    {
        // A product has a handful of documents in its life; the newest per culture is picked here.
        var documents = await context.SourceDocuments
            .Where(document => document.ProductId == productId)
            .ToListAsync(cancellationToken);

        return [.. documents.GroupBy(document => document.Culture).Select(culture => culture.MaxBy(document => document.RetrievedAt)!)];
    }

    public async Task<IReadOnlyList<ProductClaim>> StandingClaimsAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        await context.Claims
            .Where(claim => claim.ProductId == productId && Standing.Contains(claim.Status))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Only a product's CURRENT Spanish document can be waiting: a text replaced
    /// before the model reached it is history, and asking for it again would turn
    /// the queue into a loop that never empties.
    /// </summary>
    public async Task<IReadOnlyList<ProductId>> WaitingAsync(int limit, CancellationToken cancellationToken = default) =>
        await context.SourceDocuments
            .Where(document => document.Culture == "es"
                               && document.ProposedAt == null
                               && document.FailedAttempts < SourceDocument.MaxAttempts
                               && !context.SourceDocuments.Any(newer => newer.ProductId == document.ProductId
                                                                         && newer.Culture == document.Culture
                                                                         && newer.RetrievedAt > document.RetrievedAt))
            .OrderBy(document => document.RetrievedAt)
            .Select(document => document.ProductId)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<ProductClaim?> FindClaimAsync(ClaimId id, CancellationToken cancellationToken = default) =>
        context.Claims.FirstOrDefaultAsync(claim => claim.Id == id, cancellationToken);

    /// <summary>
    /// The weakest first. Support lives in the evidence's jsonb, so the order is
    /// taken here over a status's claims — a review queue, never a catalogue.
    /// </summary>
    public async Task<IReadOnlyList<ProductClaim>> ClaimsAsync(ClaimStatus status, int limit, CancellationToken cancellationToken = default)
    {
        var claims = await context.Claims.AsNoTracking()
            .Where(claim => claim.Status == status)
            .ToListAsync(cancellationToken);

        return [.. claims.OrderBy(claim => claim.Support).ThenBy(claim => claim.CreatedAt).Take(limit)];
    }

    public void Add(SourceDocument document) => context.SourceDocuments.Add(document);

    public void Add(ProductClaim claim) => context.Claims.Add(claim);
}
