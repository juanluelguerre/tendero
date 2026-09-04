using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features.ReindexProducts;

/// <summary>
/// Rebuilds the index from Postgres. docs/architecture.md already says the index
/// is a disposable projection and gets reindexed at will; what was missing was
/// the "at will".
///
/// It is genuinely needed, not needed in theory: Elasticsearch is declared with
/// ContainerLifetime.Persistent but WITHOUT a volume, so its documents live in
/// the container's write layer. The moment Aspire recreates it, the index is
/// empty with the whole catalogue in Active — and nothing restores it, because
/// the outbox already delivered those events and republishing an already-active
/// product changes nothing (by design).
///
/// It applies the SAME rule as the outbox projection, and literally the same
/// code: <see cref="ProductIndexProjection"/>. Active is indexed, the rest is
/// removed, so the reindex CONVERGES — a product that stopped being active
/// leaves the index — rather than merely adding.
///
/// It does not recreate the indexes or touch their mappings: that is
/// SearchIndexInitializer's job, and duplicating the mapping definition here
/// would be two sources for the same truth.
///
/// A known LIMIT: it converges for the products that EXIST. A product deleted
/// from the table leaves its document orphaned, because the walk cannot see what
/// is no longer there. It does not happen through the application — the domain
/// does not delete, it archives, and Archive() emits ProductArchived which
/// removes it — but it does with a hand-written DELETE or by restoring an old
/// copy of the database. Actually observed: six rows deleted by SQL left twelve
/// documents for six products. Closing that means dropping the indexes before
/// rebuilding, and that moves ownership of the mapping; it gets decided when
/// there is a case that is not a manual reset in development.
/// </summary>
/// <param name="Recreate">
/// Drop the indexes and rebuild their mapping before replaying, rather than
/// only rewriting the documents.
///
/// **It is off by default because it is destructive and briefly empties the
/// shop**, and on when the DOCUMENT changed shape. An index that already exists
/// is never re-mapped, so a new field gets mapped dynamically and comes back
/// with the wrong type — a keyword array as `text`, which a term filter fails to
/// match while reporting nothing at all.
/// </param>
public sealed record ReindexProductsCommand(bool Recreate = false)
    : ICommand<ReindexProductsResult>;

public sealed record ReindexProductsResult(int Indexed, int Removed, double ElapsedSeconds);

public sealed class ReindexProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/search/reindex                — rewrite the documents
        // POST /api/search/reindex?recreate=true   — and rebuild the mapping first
        app.MapPost("/api/search/reindex",
            async Task<Ok<ReindexProductsResult>> (
                   bool? recreate, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ReindexProductsCommand(recreate ?? false), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Search")
            .WithName("ReindexProducts");
    }
}

public sealed class ReindexProductsHandler(
    IProductReader products,
    IProductIndexer indexer)
    : ICommandHandler<ReindexProductsCommand, ReindexProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<ReindexProductsResult> HandleAsync(
        ReindexProductsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("search.reindex");
        activity?.SetTag("search.recreate", command.Recreate);
        var stopwatch = Stopwatch.StartNew();

        // Before anything is written, so the documents land in an index whose
        // mapping matches them.
        if (command.Recreate)
            await indexer.RecreateAsync(cancellationToken);

        int indexed = 0, removed = 0;

        // Streamed, not ToList: with the full Amazon Berkeley Objects catalogue
        // (147k) loading it whole into memory to walk it once makes no sense, and
        // the reader already hands it over lazily.
        await foreach (var product in products.StreamAllAsync(cancellationToken))
        {
            if (await ProductIndexProjection.ApplyAsync(indexer, product, cancellationToken))
                indexed++;
            else
                removed++;
        }

        stopwatch.Stop();
        activity?.SetTag("search.reindex.indexed", indexed);
        activity?.SetTag("search.reindex.removed", removed);

        return new ReindexProductsResult(indexed, removed, stopwatch.Elapsed.TotalSeconds);
    }
}
