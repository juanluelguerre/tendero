using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>The repository and the unit of work in memory, with the ordering rules the EF adapter keeps.</summary>
public sealed class InMemoryKnowledge : IKnowledgeRepository, IUnitOfWork
{
    public List<SourceDocument> Documents { get; } = [];
    public List<ProductClaim> Claims { get; } = [];

    public Task<SourceDocument?> FindDocumentAsync(SourceDocumentId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.FirstOrDefault(document => document.Id == id));

    public Task<IReadOnlyList<SourceDocument>> CurrentDocumentsAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>(
        [
            .. Documents.Where(document => document.ProductId == productId && document.Kind == SourceKind.ProductDescription)
                .GroupBy(document => document.Culture)
                .Select(culture => culture.MaxBy(document => document.RetrievedAt)!),
        ]);

    public Task<IReadOnlyList<SourceDocument>> CurrentReadingsAsync(ProductId productId, ImageId picture, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>(
        [
            .. Documents.Where(document => document.ProductId == productId && document.Reading?.Picture == picture)
                .GroupBy(document => document.Culture)
                .Select(culture => culture.MaxBy(document => document.RetrievedAt)!),
        ]);

    public Task<IReadOnlyList<ProductClaim>> StandingClaimsAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductClaim>>(
            [.. Claims.Where(claim => claim.ProductId == productId && claim.Status is ClaimStatus.Proposed or ClaimStatus.Approved)]);

    public Task<IReadOnlyList<SourceDocument>> CurrentReadingsAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>(
        [
            .. Documents.Where(document => document.ProductId == productId && document.Kind == SourceKind.ProductImage)
                .GroupBy(document => (document.PictureId, document.Culture))
                .Select(reading => reading.MaxBy(document => document.RetrievedAt)!),
        ]);

    public async Task<IReadOnlyList<ProductId>> WaitingAsync(int limit, CancellationToken cancellationToken = default)
    {
        var waiting = new List<(ProductId Product, DateTimeOffset Since)>();
        foreach (var product in Documents.Select(document => document.ProductId).Distinct())
        {
            SourceDocument[] current =
            [
                .. await CurrentDocumentsAsync(product, cancellationToken),
                .. await CurrentReadingsAsync(product, cancellationToken),
            ];
            var sources = current.Where(document => document is { Culture: "es", IsWaiting: true }).ToList();
            if (sources.Count > 0)
                waiting.Add((product, sources.Min(document => document.RetrievedAt)));
        }

        return [.. waiting.OrderBy(product => product.Since).Select(product => product.Product).Take(limit)];
    }

    public Task<ProductClaim?> FindClaimAsync(ClaimId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Claims.FirstOrDefault(claim => claim.Id == id));

    public Task<ClaimPage> ClaimsAsync(
        ClaimStatus status, ClaimSupportFilter support, int page, int pageSize, ProductId? product = null,
        CancellationToken cancellationToken = default)
    {
        var inStatus = Claims.Where(claim => claim.Status == status && (product is null || claim.ProductId == product)).ToList();
        var counts = new Dictionary<ClaimSupportFilter, int>
        {
            [ClaimSupportFilter.Weak] = inStatus.Count(claim => claim.IsWeaklySupported),
            [ClaimSupportFilter.All] = inStatus.Count,
        };

        var shown = support == ClaimSupportFilter.Weak ? inStatus.Where(claim => claim.IsWeaklySupported) : inStatus;
        return Task.FromResult(new ClaimPage(
            [
                .. shown.OrderBy(claim => claim.Support).ThenBy(claim => claim.CreatedAt).ThenBy(claim => claim.Id.Value)
                    .Skip(Paging.Offset(page, pageSize)).Take(pageSize),
            ],
            counts[support],
            counts));
    }

    public void Add(SourceDocument document) => Documents.Add(document);

    public void Add(ProductClaim claim) => Claims.Add(claim);

    /// <summary>How many times a handler saved: a decision that is never saved is a decision that did not happen.</summary>
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
