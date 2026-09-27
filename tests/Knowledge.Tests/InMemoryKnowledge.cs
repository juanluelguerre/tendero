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
            .. Documents.Where(document => document.ProductId == productId)
                .GroupBy(document => document.Culture)
                .Select(culture => culture.MaxBy(document => document.RetrievedAt)!),
        ]);

    public Task<IReadOnlyList<ProductClaim>> StandingClaimsAsync(ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductClaim>>(
            [.. Claims.Where(claim => claim.ProductId == productId && claim.Status is ClaimStatus.Proposed or ClaimStatus.Approved)]);

    public async Task<IReadOnlyList<ProductId>> WaitingAsync(int limit, CancellationToken cancellationToken = default)
    {
        var waiting = new List<ProductId>();
        foreach (var product in Documents.Select(document => document.ProductId).Distinct())
        {
            var current = await CurrentDocumentsAsync(product, cancellationToken);
            if (current.FirstOrDefault(document => document.Culture == "es") is { IsWaiting: true })
                waiting.Add(product);
        }

        return [.. waiting.Take(limit)];
    }

    public Task<ProductClaim?> FindClaimAsync(ClaimId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Claims.FirstOrDefault(claim => claim.Id == id));

    public Task<IReadOnlyList<ProductClaim>> ClaimsAsync(ClaimStatus status, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductClaim>>(
            [.. Claims.Where(claim => claim.Status == status).OrderBy(claim => claim.Support).ThenBy(claim => claim.CreatedAt).Take(limit)]);

    public void Add(SourceDocument document) => Documents.Add(document);

    public void Add(ProductClaim claim) => Claims.Add(claim);

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
