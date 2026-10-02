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
    /// The weakest first, paged and counted in Postgres. Support lives in the
    /// evidence's jsonb, and a claim's support is its weakest quote's, so the
    /// order is the minimum over that array and "weak" is any quote under the
    /// line — the same rule as <see cref="ProductClaim.IsWeaklySupported"/>,
    /// said the way SQL can, and held to it by the integration test.
    /// </summary>
    public async Task<ClaimPage> ClaimsAsync(
        ClaimStatus status, ClaimSupportFilter support, int page, int pageSize, ProductId? product = null,
        CancellationToken cancellationToken = default)
    {
        var claims = context.Claims.AsNoTracking().Where(claim => claim.Status == status);
        if (product is { } one)
            claims = claims.Where(claim => claim.ProductId == one);
        var weak = claims.Where(claim => EF.Property<List<Evidence>>(claim, EvidenceColumn)
            .Any(item => item.Support < ProductClaim.WeakSupport));

        var counts = new Dictionary<ClaimSupportFilter, int>
        {
            [ClaimSupportFilter.Weak] = await weak.CountAsync(cancellationToken),
            [ClaimSupportFilter.All] = await claims.CountAsync(cancellationToken),
        };

        var shown = support == ClaimSupportFilter.Weak ? weak : claims;
        var items = await shown
            .OrderBy(claim => EF.Property<List<Evidence>>(claim, EvidenceColumn).Min(item => item.Support))
            .ThenBy(claim => claim.CreatedAt)
            .ThenBy(claim => claim.Id)   // a stable tie-break, or two pages repeat a claim
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ClaimPage(items, counts[support], counts);
    }

    /// <summary>The field the evidence is mapped through (<c>ProductClaimConfiguration</c>).</summary>
    private const string EvidenceColumn = "evidence";

    public void Add(SourceDocument document) => context.SourceDocuments.Add(document);

    public void Add(ProductClaim claim) => context.Claims.Add(claim);
}
