using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Features.ReindexProducts;

/// <summary>
/// Rebuilds the index from Postgres. docs/architecture.md already says the index
/// is a disposable projection and gets reindexed at will; what was missing was
/// the "at will".
///
/// It is genuinely needed, not needed in theory: Elasticsearch is declared with
/// ContainerLifetime.Persistent but WITHOUT a volume, so its documents live in
/// the container's write layer. The moment Aspire recreates it, the index is
/// empty with the whole catalogue in Active — and the outbox cannot restore it,
/// because it already delivered those events and republishing an already-active
/// product changes nothing (by design). Until 2026-09-11 nothing else did
/// either; <see cref="RebuildSearchIndexAtStartupWorker"/> now does, at worker startup.
///
/// It applies the SAME rule as the outbox projection, and literally the same
/// code: <see cref="ProductIndexProjection"/>. Active is indexed, the rest is
/// removed, so the reindex CONVERGES — a product that stopped being active
/// leaves the index — rather than merely adding.
///
/// It does not recreate the indexes or touch their mappings: that is
/// CreateSearchIndexesAtStartupWorker's job, and duplicating the mapping definition here
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
/// <param name="Target">
/// Which projection to rebuild: <c>lexical</c> (the default, and what the
/// endpoint always did), <c>vector</c> or <c>all</c> (P8-4).
///
/// Separate because they cost different things: the lexical index rebuilds a
/// hundred products in seconds, while embedding them takes as long as the model
/// server needs — a few seconds on a GPU and a couple of minutes on a CPU. A
/// mapping change should not wait for a model, and a model change has nothing to
/// ask of the lexical index.
/// </param>
public sealed record ReindexProductsCommand(bool Recreate = false, string Target = ReindexTargets.Lexical)
    : ICommand<ReindexProductsResult>;

public static class ReindexTargets
{
    public const string Lexical = "lexical";
    public const string Vector = "vector";

    /// <summary>
    /// The pictures' vectors (phase 13a), asked for by name and not part of
    /// <see cref="All"/>: <c>all</c> is the two projections of a product's words,
    /// and a shop started without visual search — every fresh clone — would have
    /// its "rebuild everything" refused for a layer it never had.
    /// </summary>
    public const string Pictures = "pictures";

    public const string All = "all";

    public const string Supported = "Supported targets: lexical, vector, pictures, all.";
}

public sealed record ReindexProductsResult(int Indexed, int Removed, double ElapsedSeconds);

public sealed class ReindexProductsValidator : AbstractValidator<ReindexProductsCommand>
{
    public ReindexProductsValidator()
    {
        // A closed set, answered at the door: a target nobody implements would
        // otherwise rebuild nothing and report success.
        RuleFor(command => command.Target)
            .Must(target => target is ReindexTargets.Lexical or ReindexTargets.Vector or ReindexTargets.Pictures or ReindexTargets.All)
            .WithMessage(ReindexTargets.Supported);
    }
}

public sealed class ReindexProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/search/reindex                        — rewrite the lexical documents
        // POST /api/search/reindex?recreate=true           — and rebuild the mapping first
        // POST /api/search/reindex?target=vector           — embed the catalogue again
        // POST /api/search/reindex?target=pictures         — embed the pictures the collection lacks
        // POST /api/search/reindex?target=all&recreate=true — both projections of the words, from scratch
        app.MapPost("/api/search/reindex",
            async Task<Ok<ReindexProductsResult>> (
                   bool? recreate, string? target, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ReindexProductsCommand(recreate ?? false, target ?? ReindexTargets.Lexical), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Search")
            .WithName("ReindexProducts");
    }
}

/// <summary>
/// The "nothing restores it" above, closed: when the worker starts and an index
/// is empty, the catalogue is replayed into it.
///
/// Found by the UI review of 2026-09-11, after Docker Desktop restarted and
/// Aspire recreated the container: a hundred Active products, an empty index,
/// and a storefront answering its own suggested query with "Nothing matched —
/// try fewer words". The rebuild existed; nothing asked for it.
///
/// It goes through the dispatcher and not straight to the handler, so the
/// rebuild is audited like the button would be — as the system, which is who
/// asked. It is empty-or-nothing on purpose: a populated index is left alone,
/// because rebuilding on every start would make a restart cost a walk of the
/// catalogue for nothing, and an index that is merely STALE is what the outbox
/// keeps current.
///
/// A search engine that does not answer is logged and not thrown. The same
/// process drains the outbox that holds stock and captures payments, and a shop
/// that cannot take orders because its index is down has made lexical search
/// the thing everything depends on — the reverse of invariant 8.
///
/// It runs inside <c>StartAsync</c>, so the worker is not up until the rebuild
/// is done: nine seconds at a hundred products, measured. At the full ABO set
/// that is the wrong shape, and it is the standing backlog's "reindex as a
/// background job" row, which already names its trigger.
/// </summary>
public sealed class RebuildSearchIndexAtStartupWorker(
    IServiceScopeFactory scopes,
    ILogger<RebuildSearchIndexAtStartupWorker> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();

        var recreate = false;
        try
        {
            // An index built from an older mapping is recreated as well as
            // refilled (R-7.3, F-1): a field added to the document would
            // otherwise be mapped as text and fail its filters in silence.
            recreate = scope.ServiceProvider.GetService<ISearchIndexSchema>() is { } schema
                && !await schema.IsCurrentAsync(cancellationToken);

            if (!recreate && !await scope.ServiceProvider.GetRequiredService<IProductIndexer>().IsEmptyAsync(cancellationToken))
                return;
        }
        catch (SearchUnavailableException exception)
        {
            logger.LogWarning(exception,
                "The search index could not be checked at startup; it is left as it is. " +
                "POST /api/search/reindex rebuilds it once the engine answers");
            return;
        }

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new ReindexProductsCommand(Recreate: recreate), cancellationToken);

        logger.LogInformation(
            "The search index was {Reason} at startup and was rebuilt: {Indexed} indexed, {Removed} removed, {Elapsed:F1}s",
            recreate ? "on an older mapping" : "empty", result.Indexed, result.Removed, result.ElapsedSeconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The same repair for the vectors, and it cannot be the same shape.
///
/// Qdrant keeps no volume either (ADR 0012), so a recreated container is an empty
/// collection behind a full catalogue. But nothing waits for the vector layer
/// (invariant 8), and on a first start the model server spends minutes
/// downloading its weights — so a check inside <c>StartAsync</c> would either hold
/// the worker, and the outbox with it, hostage to a download, or look once, find
/// nothing and give up. This one runs in the background: it asks every fifteen
/// seconds until the layer answers, rebuilds if the collection is empty, and
/// stops. A process started without a vector layer stops at once.
///
/// Whatever the outbox tried to project while it waited was logged and skipped
/// (<c>ProjectProductToVectors</c>), and this rebuild is what catches it up.
/// </summary>
public sealed class RebuildVectorIndexAtStartupWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RebuildVectorIndexAtStartupWorker> logger) : BackgroundService
{
    internal TimeSpan RetryEvery { get; init; } = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var told = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var status = await scope.ServiceProvider.GetRequiredService<IVectorLayer>().CheckAsync(stoppingToken);

                if (!status.IsConfigured)
                    return;

                if (status.IsAvailable)
                {
                    if (await scope.ServiceProvider.GetRequiredService<IProductVectorIndexer>().IsEmptyAsync(stoppingToken))
                    {
                        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                            .SendAsync(new ReindexProductsCommand(Target: ReindexTargets.Vector), stoppingToken);

                        logger.LogInformation(
                            "The vector collection was empty and was rebuilt: {Indexed} embedded, {Removed} removed, {Elapsed:F1}s",
                            result.Indexed, result.Removed, result.ElapsedSeconds);
                    }

                    return;
                }

                if (!told)
                {
                    logger.LogInformation(
                        "Waiting for the vector layer before checking its collection: {Reason}", status.Reason);
                    told = true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An exception out of a BackgroundService stops the host, and this
                // host drains the outbox that takes the money. A rebuild that fails
                // is a warning and one more try, never a worker that is gone.
                logger.LogWarning(exception,
                    "The vector collection could not be checked or rebuilt; trying again in {RetryEvery}", RetryEvery);
            }

            await Task.Delay(RetryEvery, clock, stoppingToken);
        }
    }
}

/// <summary>
/// And for the pictures (phase 13a, P13-3). The model is in this process, so
/// there is no layer to ask whether it is ready: the only thing that may not be
/// there yet is the store, which on a cold stack starts after the worker. So it
/// asks the collection itself, every fifteen seconds until it answers, rebuilds
/// if it is empty, and stops. A process started without picture vectors stops
/// at once.
///
/// In the background for the reason the words' is — nothing waits for an AI
/// layer — and for one of its own: the first rebuild embeds every picture of the
/// catalogue on the CPU, and the outbox does not wait for that.
/// </summary>
public sealed class RebuildPictureVectorsAtStartupWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RebuildPictureVectorsAtStartupWorker> logger) : BackgroundService
{
    internal TimeSpan RetryEvery { get; init; } = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                // The stand-in is never empty, so a process without picture
                // vectors asks once and is done.
                if (await scope.ServiceProvider.GetRequiredService<IPictureVectorIndexer>().IsEmptyAsync(stoppingToken))
                {
                    var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                        .SendAsync(new ReindexProductsCommand(Target: ReindexTargets.Pictures), stoppingToken);

                    logger.LogInformation(
                        "The picture collection was empty and was rebuilt: the pictures of {Indexed} products, {Elapsed:F1}s",
                        result.Indexed, result.ElapsedSeconds);
                }

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An exception out of a BackgroundService stops the host, and this
                // host drains the outbox that takes the money.
                logger.LogWarning(
                    "The picture collection could not be checked or rebuilt ({Reason}); trying again in {RetryEvery}",
                    exception.Message, RetryEvery);
            }

            await Task.Delay(RetryEvery, clock, stoppingToken);
        }
    }
}

public sealed class ReindexProductsHandler(
    IProductReader products,
    IProductIndexer indexer,
    IProductVectorIndexer vectors,
    IVectorLayer vectorLayer,
    IPictureVectorIndexer pictures)
    : ICommandHandler<ReindexProductsCommand, ReindexProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<ReindexProductsResult> HandleAsync(
        ReindexProductsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("search.reindex");
        activity?.SetTag("search.recreate", command.Recreate);
        activity?.SetTag("search.reindex.target", command.Target);
        var stopwatch = Stopwatch.StartNew();

        IProductIndexer[] targets = command.Target switch
        {
            ReindexTargets.Lexical => [indexer],
            ReindexTargets.Vector => [vectors],
            ReindexTargets.Pictures => [pictures],
            ReindexTargets.All => [indexer, vectors],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Target, ReindexTargets.Supported)
        };

        // Asked before anything is touched. The vector projection's own failures
        // are swallowed on the outbox path, on purpose; a person who pressed
        // "rebuild the vectors" is owed the reason it cannot, not a count of
        // products that were silently skipped.
        if (command.Target is ReindexTargets.Vector or ReindexTargets.All)
        {
            var status = await vectorLayer.CheckAsync(cancellationToken);
            if (!status.IsAvailable && !(command.Recreate && status.IsConfigured))
                throw new SearchUnavailableException("reindexing the vectors", status.Reason);
        }

        // The same debt for the pictures: the stand-in writes nothing, and a walk
        // of the catalogue that wrote nothing must not answer "120 indexed".
        if (command.Target is ReindexTargets.Pictures && !pictures.IsConfigured)
            throw new SearchUnavailableException("reindexing the pictures", NoPictureVectors.Reason);

        // Before anything is written, so the documents land in an index whose
        // mapping matches them.
        if (command.Recreate)
            foreach (var target in targets)
                await target.RecreateAsync(cancellationToken);

        int indexed = 0, removed = 0;

        // Streamed, not ToList: with the full Amazon Berkeley Objects catalogue
        // (147k) loading it whole into memory to walk it once makes no sense, and
        // the reader already hands it over lazily.
        await foreach (var product in products.StreamAllAsync(cancellationToken))
        {
            var isIndexed = false;

            foreach (var target in targets)
                isIndexed = await ProductIndexProjection.ApplyAsync(target, product, cancellationToken);

            if (isIndexed)
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
