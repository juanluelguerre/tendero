using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Catalog.Features.ImportProducts;

public sealed record ImportProductsCommand(string Source) : ICommand<ImportProductsResult>;

public sealed record ImportProductsResult(int Created, int Updated, int Failed, double ElapsedSeconds);

public sealed class ImportProductsValidator : AbstractValidator<ImportProductsCommand>
{
    /// <summary>
    /// It checks that the source EXISTS, not that it looks like a source. A
    /// <c>^[a-z0-9-]+$</c> accepted "shopify" while there was no Shopify
    /// connector, and the failure came out as a 500 from the DI container: a
    /// caller's error reported as a server fault.
    /// </summary>
    public ImportProductsValidator(ICatalogSourceRegistry sources)
    {
        RuleFor(command => command.Source)
            .NotEmpty()
            .Must(source => sources.Sources.Contains(source, StringComparer.Ordinal))
            .WithMessage(command =>
                $"Unknown catalog source '{command.Source}'. " +
                $"Registered sources: {string.Join(", ", sources.Sources)}.");
    }
}

public sealed class ImportProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/import  { "source": "seed" }
        app.MapPost("/api/catalog/import",
            async Task<Ok<ImportProductsResult>> (
                   ImportProductsCommand command, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(command, ct);
                return TypedResults.Ok(result);
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("ImportProducts");
    }
}

public sealed class ImportProductsHandler(
    ICatalogSourceRegistry sources,
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    IExternalImageReader imageReader,
    IImageStore imageStore,
    IAttributeDefinitionReader attributeDefinitions,
    TimeProvider clock,
    ILogger<ImportProductsHandler> logger)
    : ICommandHandler<ImportProductsCommand, ImportProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private const int BatchSize = 200;

    public async Task<ImportProductsResult> HandleAsync(
        ImportProductsCommand command, CancellationToken cancellationToken)
    {
        // The source name arrives in the request, so resolution is dynamic; what
        // is NOT dynamic is what this handler depends on. Adding a new source is
        // still registering a class and zero `if`s.
        var connector = sources.Get(command.Source);

        using var activity = Telemetry.StartActivity("catalog.import");
        activity?.SetTag("catalog.source", connector.Source);

        var stopwatch = Stopwatch.StartNew();
        var tally = new ImportTally();

        // Once per import, not per product: resolving "azul marino" to NAVY_BLUE
        // needs the whole definition catalogue, and asking for it per product
        // would be N+1 over something that does not change during the import.
        var definitions = await attributeDefinitions.AllAsync(cancellationToken);

        await foreach (var external in connector.StreamProductsAsync(cancellationToken))
        {
            try
            {
                await ImportOneAsync(external, connector.Source, definitions, tally, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One unreadable product cannot bring down a catalogue of 147k. A
                // CANCELLATION does have to stop: while it came through here it
                // was counted as a failed product and the loop kept turning until
                // the whole source was exhausted, so Ctrl+C cancelled nothing.
                tally.Failed++;
                logger.LogWarning(exception, "Failed to import product {ExternalId} from {Source}",
                    external.ExternalId, connector.Source);
            }

            if (tally.Pending >= BatchSize)
                await FlushAsync(tally, cancellationToken);
        }

        await FlushAsync(tally, cancellationToken);

        stopwatch.Stop();
        activity?.SetTag("catalog.import.created", tally.Created);
        activity?.SetTag("catalog.import.updated", tally.Updated);
        activity?.SetTag("catalog.import.failed", tally.Failed);

        logger.LogInformation(
            "Import from {Source}: {Created} created, {Updated} updated, {Failed} failed in {Elapsed:0.0}s",
            connector.Source, tally.Created, tally.Updated, tally.Failed, stopwatch.Elapsed.TotalSeconds);

        return new ImportProductsResult(
            tally.Created, tally.Updated, tally.Failed, stopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// One product from the source: created or re-imported, idempotent by
    /// (source, external id). The mapping to the aggregate lives in
    /// <see cref="ExternalProductMapper"/> because the quality gate
    /// (tools/SearchEval) builds the same product and cannot call here.
    /// </summary>
    private async Task ImportOneAsync(
        ExternalProduct external, string source, AttributeDefinitions definitions,
        ImportTally tally, CancellationToken cancellationToken)
    {
        var existing = await repository.FindByExternalReferenceAsync(
            source, external.ExternalId, cancellationToken);

        if (existing is null)
        {
            var product = external.ToNewProduct(source, clock, definitions);
            await IngestImagesAsync(product, external, cancellationToken);
            repository.Add(product);
            tally.Created++;
        }
        else
        {
            external.ApplyTo(existing, clock, definitions);
            await IngestImagesAsync(existing, external, cancellationToken);
            tally.Updated++;
        }

        tally.Pending++;
    }

    /// <summary>
    /// Commits the batch. The <c>ProductUpserted</c> events travel to the outbox
    /// table in THIS same transaction; the indexing worker does the rest.
    ///
    /// If the batch cannot be saved it is lost whole, and the products already
    /// counted as created or updated become failures. Counting them as good
    /// because the loop did not throw is what made the response say
    /// <c>"created": 200</c> about a transaction that never reached Postgres.
    /// </summary>
    private async Task FlushAsync(ImportTally tally, CancellationToken cancellationToken)
    {
        if (tally.Pending == 0)
            return;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Import batch of {Count} products could not be saved", tally.Pending);
            tally.DiscardPendingBatch();
            return;
        }

        tally.MarkFlushed();
    }

    /// <summary>
    /// Ingests the images instead of storing the source's URL: the catalogue
    /// stops depending on Shopify or whoever keeping their CDN alive, and phase 4
    /// will be able to compute embeddings over bytes we control
    /// (docs/adr/0011-product-images.md).
    ///
    /// An image that cannot be read aborts nothing: the product goes in without
    /// it and the next import retries.
    /// </summary>
    private async Task IngestImagesAsync(
        Product product, ExternalProduct external, CancellationToken cancellationToken)
    {
        foreach (var image in external.Images)
        {
            var content = await imageReader.OpenAsync(image, cancellationToken);
            if (content is null)
                continue;

            await using var stream = content.Content;
            var id = await imageStore.SaveAsync(stream, content.ContentType, cancellationToken);
            product.AddImage(clock, id, image.LocalizedAlt);
        }
    }

    /// <summary>
    /// The import's counters. A type and not four local variables, because the
    /// flush has to be able to CORRECT them: until the batch is in Postgres,
    /// "created" is an intention and not a fact.
    /// </summary>
    private sealed class ImportTally
    {
        private int _createdWhenLastFlushed;
        private int _updatedWhenLastFlushed;

        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }

        public void MarkFlushed()
        {
            _createdWhenLastFlushed = Created;
            _updatedWhenLastFlushed = Updated;
            Pending = 0;
        }

        public void DiscardPendingBatch()
        {
            Failed += Created - _createdWhenLastFlushed + (Updated - _updatedWhenLastFlushed);
            Created = _createdWhenLastFlushed;
            Updated = _updatedWhenLastFlushed;
            Pending = 0;
        }
    }
}

// The ports this slice uses live in Catalog/Ports: PublishProduct needs the same
// ones, and a slice cannot reference another slice.
