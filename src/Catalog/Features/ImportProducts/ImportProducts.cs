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

/// <param name="Culture">The language the notes are said in: the endpoint's <c>?culture=</c> or
/// <c>Accept-Language</c>, as every endpoint picks it (ADR 0013).</param>
/// <param name="Rehearse">Run the import with nothing written — no product, picture, run or
/// collection — and answer what would come and the notes (phase 16, P16-13): what a
/// merchant reads before a migration, rather than after.</param>
public sealed record ImportProductsCommand(string Source, string Culture = "es", bool Rehearse = false) : ICommand<ImportProductsResult>;

/// <param name="Notes">What the source could not hand over cleanly, a note per thing
/// (phase 16, P16-5): a person reads them instead of the logs.</param>
/// <param name="ImportId">This run, for publishing what it created in one operation once
/// a person has read the notes (P16-12): <c>POST /api/catalog/imports/{id}/publish</c>. Null
/// for a rehearsal, which records no run.</param>
public sealed record ImportProductsResult(
    int Created, int Updated, int Failed, double ElapsedSeconds, IReadOnlyList<Message<ImportNoteCode>> Notes, string? ImportId);

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
        // POST /api/catalog/import  { "source": "seed" }, or { "source": "shopify", "rehearse": true }
        app.MapPost("/api/catalog/import",
            async Task<Ok<ImportProductsResult>> (
                   ImportProductsCommand command, string? culture, HttpContext http,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                var result = await dispatcher.SendAsync(command with { Culture = language }, ct);
                http.Response.Headers.ContentLanguage = language;
                http.Response.Headers.Vary = "Accept-Language";
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
    ICategoryReader categories,
    ISourceCollections collections,
    IRehearsedCatalogue rehearsed,
    IImportRunRepository runs,
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
        activity?.SetTag("catalog.import.rehearsal", command.Rehearse);

        var stopwatch = Stopwatch.StartNew();
        var run = ImportRun.Start(connector.Source, clock.GetUtcNow());
        if (!command.Rehearse)
            runs.Add(run);
        var tally = new ImportTally(run, command.Rehearse);

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
        await KeepCollectionsAsync(connector, tally, cancellationToken);
        if (!command.Rehearse)
            await SaveRunAsync(connector.Source, cancellationToken);

        stopwatch.Stop();
        activity?.SetTag("catalog.import.created", tally.Created);
        activity?.SetTag("catalog.import.updated", tally.Updated);
        activity?.SetTag("catalog.import.failed", tally.Failed);

        logger.LogInformation(
            "Import from {Source}: {Created} created, {Updated} updated, {Failed} failed in {Elapsed:0.0}s",
            connector.Source, tally.Created, tally.Updated, tally.Failed, stopwatch.Elapsed.TotalSeconds);

        tally.Notes.AddRange(connector.SourceNotes);
        // A rehearsal brought nothing in: the metrics count what an import did.
        if (!command.Rehearse)
            ImportMetrics.Record(connector.Source, tally.Created, tally.Updated, tally.Failed, tally.Notes);
        var english = command.Culture == "en";

        return new ImportProductsResult(
            tally.Created, tally.Updated, tally.Failed, stopwatch.Elapsed.TotalSeconds,
            [.. tally.Notes.Select(note => ImportNotes.Said(note, english))],
            command.Rehearse ? null : run.Id.ToString());
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

        if (external.Notes is { } notes)
            tally.Notes.AddRange(notes);
        if (existing is not null)
            tally.Notes.AddRange(RenamedSkus(existing, external, source));

        if (tally.Rehearsal)
        {
            // What the import would build, built and kept by nobody: a product
            // the mapping refuses fails here as it would there. A product the
            // shop has is mapped afresh rather than changed, so nothing it holds
            // is touched; its pictures are read, not stored.
            var product = external.ToNewProduct(source, clock, definitions);
            await ReadImagesAsync(external, tally, cancellationToken);
            tally.PendingCategories[external.ExternalId] = product.Category;
            if (existing is null)
            {
                // Held for the rest of a migration rehearsed in the same request (P16-21).
                rehearsed.Bring(product);
                tally.Created++;
            }
            else
                tally.Updated++;
            tally.Pending++;
            return;
        }

        if (existing is null)
        {
            var product = external.ToNewProduct(source, clock, definitions);
            await IngestImagesAsync(product, external, definitions, tally, cancellationToken);
            repository.Add(product);
            tally.PendingCreated.Add(product.Id);
            tally.PendingCategories[external.ExternalId] = product.Category;
            tally.Created++;
        }
        else
        {
            external.ApplyTo(existing, clock, definitions, source);
            await IngestImagesAsync(existing, external, definitions, tally, cancellationToken);
            tally.PendingCategories[external.ExternalId] = existing.Category;
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

        if (tally.Rehearsal)
        {
            tally.MarkFlushed();
            return;
        }

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
    /// stops depending on Shopify or whoever keeping their CDN alive, and phase
    /// 13's image embeddings will be computed over bytes we control
    /// (docs/adr/0011-product-images.md).
    ///
    /// An image that cannot be read aborts nothing: the product goes in without
    /// it and the next import retries.
    ///
    /// **The source's gallery replaces the product's, in the source's order**,
    /// so its first image is the cover. It used to be appended, and the cover is
    /// the lowest sort order: when the seed swapped six placeholders for
    /// illustrations, every re-import put the illustration behind the
    /// placeholder and the storefront showed the placeholder for good (UI
    /// review, 2026-09-11). Replacing happens only when EVERY image the source
    /// lists arrived; a partial read only adds, because a CDN that times out
    /// once must not strip the photos off a live product.
    ///
    /// **What a picture shows is set here too** (phase 13a, I-4d;
    /// `docs/analysis/catalogue-model.md`): each picture the source says shows a
    /// variant — <c>color: negro</c> — is told so, by the catalogue's own words
    /// for it. Each on its own, whether or not every image arrived: the picture
    /// carries what it shows, not its place in a list. What the source does not
    /// say, the product keeps, as with the alt text; and a picture said to show
    /// what no variant is sold in is imported untagged, with a warning — a
    /// source's mistake costs the tag, never the product.
    /// </summary>
    private async Task IngestImagesAsync(
        Product product, ExternalProduct external, AttributeDefinitions definitions, ImportTally tally,
        CancellationToken cancellationToken)
    {
        var ingested = new List<(ImageId Id, LocalizedText? Alt)>(external.Images.Count);
        var shown = new List<(ImageId Id, IReadOnlyDictionary<string, string> Shows)>();

        foreach (var image in external.Images)
        {
            var content = await imageReader.OpenAsync(image, cancellationToken);
            if (content is null)
            {
                tally.Notes.Add(new SourceNote(ImportNoteCode.ImageUnreadable, external.ExternalId, image.Location.ToString()));
                continue;
            }

            await using var stream = content.Content;
            var id = await imageStore.SaveAsync(stream, content.ContentType, cancellationToken);
            ingested.Add((id, image.LocalizedAlt));
            if (image.Shows is { Count: > 0 } shows)
                shown.Add((id, shows));
        }

        if (ingested.Count == external.Images.Count)
        {
            product.ReplaceImages(clock, ingested);
        }
        else
        {
            foreach (var (id, alt) in ingested)
                product.AddImage(clock, id, alt);
        }

        foreach (var (id, shows) in shown)
        {
            try
            {
                product.Show(clock, id, ExternalProductMapper.Coordinates(shows, definitions));
            }
            catch (InvalidOperationException refused)
            {
                logger.LogWarning(
                    "A picture of {ExternalId} was imported without what it shows: {Reason}", external.ExternalId, refused.Message);
            }
        }
    }

    /// <summary>
    /// The rehearsal's pictures (P16-13): each opened, as the import would, so an
    /// unreadable one is said now, and closed again unstored.
    /// </summary>
    private async Task ReadImagesAsync(ExternalProduct external, ImportTally tally, CancellationToken cancellationToken)
    {
        foreach (var image in external.Images)
        {
            if (await imageReader.OpenAsync(image, cancellationToken) is not { } content)
            {
                tally.Notes.Add(new SourceNote(ImportNoteCode.ImageUnreadable, external.ExternalId, image.Location.ToString()));
                continue;
            }

            await content.Content.DisposeAsync();
        }
    }

    /// <summary>
    /// The source's collections as categories (phase 16, P16-11): each is the
    /// category its saved products share, kept by its handle for the redirects.
    /// One whose products share none — or none of whose products came in — is a
    /// note: sending its old address to a category close by would be a guess.
    ///
    /// Read once the products are in, because a collection is read against
    /// what was saved, not what was streamed. Saved with the run, at the end.
    /// </summary>
    private async Task KeepCollectionsAsync(ICatalogSourceConnector connector, ImportTally tally, CancellationToken cancellationToken)
    {
        CategoryTree? tree = null;

        await foreach (var collection in connector.StreamCollectionsAsync(cancellationToken))
        {
            tree ??= await categories.AllAsync(cancellationToken);
            var saved = collection.ProductExternalIds.Where(tally.Categories.ContainsKey).Select(id => tally.Categories[id]);

            if (tree.SharedBy(saved) is { } category)
            {
                if (tally.Rehearsal)
                    rehearsed.Keep(collection.Handle, category);
                else
                    await collections.KeepAsync(connector.Source, collection.Handle, category, cancellationToken);
            }
            else
            {
                tally.Notes.Add(new SourceNote(ImportNoteCode.CollectionNotMapped, collection.Handle, collection.Title));
            }
        }

    }

    /// <summary>
    /// The run, with the products its last batch created, and the collections
    /// kept (P16-11, P16-12). If it cannot be saved the products are already in
    /// and the answer still says so; the run cannot then be published in one go,
    /// and the next import records another.
    /// </summary>
    private async Task SaveRunAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The import run of {Source} and its collections could not be saved", source);
        }
    }

    /// <summary>
    /// A SKU the source renamed on a variant this shop already sells. Ours stays —
    /// Inventory, the cart and every order speak it — and a person is told
    /// (phase 16, spec *Re-importing*, corrected).
    /// </summary>
    private static IEnumerable<SourceNote> RenamedSkus(Product existing, ExternalProduct external, string source) =>
        (external.Variants ?? [])
            .Where(variant => variant is { ExternalId: not null, Sku.Length: > 0 })
            // A SKU its siblings share was never ours to keep, and is noted as shared.
            .Where(variant => external.Variants!.Count(sibling =>
                string.Equals(sibling.Sku?.Trim(), variant.Sku!.Trim(), StringComparison.OrdinalIgnoreCase)) == 1)
            .Select(variant => (Ours: existing.VariantFromSource(source, variant.ExternalId!)?.Sku, Theirs: variant.Sku!.Trim()))
            .Where(skus => skus.Ours is not null && !string.Equals(skus.Ours, skus.Theirs, StringComparison.OrdinalIgnoreCase))
            .Select(skus => new SourceNote(ImportNoteCode.SkuChangedAtSource, external.ExternalId, $"{skus.Ours} → {skus.Theirs}"));

    /// <summary>
    /// The import's counters. A type and not four local variables, because the
    /// flush has to be able to CORRECT them: until the batch is in Postgres,
    /// "created" is an intention and not a fact.
    /// </summary>
    private sealed class ImportTally(ImportRun run, bool rehearsal)
    {
        /// <summary>Nothing is written: products, pictures, the run and the collections are counted and dropped.</summary>
        public bool Rehearsal { get; } = rehearsal;

        private int createdWhenLastFlushed;
        private int updatedWhenLastFlushed;

        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }

        /// <summary>Each product saved, by its external id, with its category: what a collection is read against.</summary>
        public Dictionary<string, string?> Categories { get; } = new(StringComparer.Ordinal);

        /// <summary>The batch's new products, until it is saved: only then has the run brought them.</summary>
        public List<ProductId> PendingCreated { get; } = [];

        /// <summary>The batch's products, until it is saved.</summary>
        public Dictionary<string, string?> PendingCategories { get; } = new(StringComparer.Ordinal);

        /// <summary>What did not come across cleanly. Kept when a batch is lost: it is about the source.</summary>
        public List<SourceNote> Notes { get; } = [];

        public void MarkFlushed()
        {
            this.createdWhenLastFlushed = Created;
            this.updatedWhenLastFlushed = Updated;
            foreach (var (id, category) in PendingCategories)
                Categories[id] = category;
            PendingCategories.Clear();
            foreach (var id in PendingCreated)
                run.Brought(id);
            PendingCreated.Clear();
            Pending = 0;
        }

        public void DiscardPendingBatch()
        {
            Failed += Created - this.createdWhenLastFlushed + (Updated - this.updatedWhenLastFlushed);
            Created = this.createdWhenLastFlushed;
            Updated = this.updatedWhenLastFlushed;
            PendingCategories.Clear();
            PendingCreated.Clear();
            Pending = 0;
        }
    }
}

// The ports this slice uses live in Catalog/Ports: PublishProduct needs the same
// ones, and a slice cannot reference another slice.
