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
    /// Se comprueba que el origen EXISTA, no que tenga forma de origen. Un
    /// <c>^[a-z0-9-]+$</c> aceptaba "shopify" mientras no hubiera conector de
    /// Shopify, y el fallo salía como 500 desde el contenedor de dependencias:
    /// un error del llamante contado como avería del servidor.
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
    ILogger<ImportProductsHandler> logger)
    : ICommandHandler<ImportProductsCommand, ImportProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private const int BatchSize = 200;

    public async Task<ImportProductsResult> HandleAsync(
        ImportProductsCommand command, CancellationToken cancellationToken)
    {
        // El nombre del origen llega en la petición, así que la resolución es
        // dinámica; lo que NO es dinámico es de quién depende este handler.
        // Añadir un origen nuevo sigue siendo registrar una clase y cero `if`s.
        var connector = sources.Get(command.Source);

        using var activity = Telemetry.StartActivity("catalog.import");
        activity?.SetTag("catalog.source", connector.Source);

        var stopwatch = Stopwatch.StartNew();
        var tally = new ImportTally();

        await foreach (var external in connector.StreamProductsAsync(cancellationToken))
        {
            try
            {
                await ImportOneAsync(external, connector.Source, tally, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Un producto ilegible no puede tumbar un catálogo de 147k. Una
                // CANCELACIÓN sí tiene que parar: cuando entraba por aquí se
                // contaba como producto fallido y el bucle seguía girando hasta
                // agotar el origen entero, así que Ctrl+C no cancelaba nada.
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
    /// Un producto del origen: alta o reimportación, idempotente por
    /// (origen, id externo). El mapeo a agregado vive en
    /// <see cref="ExternalProductMapper"/> porque la puerta de calidad
    /// (tools/SearchEval) construye el mismo producto y no puede llamar aquí.
    /// </summary>
    private async Task ImportOneAsync(
        ExternalProduct external, string source, ImportTally tally, CancellationToken cancellationToken)
    {
        var existing = await repository.FindByExternalReferenceAsync(
            source, external.ExternalId, cancellationToken);

        if (existing is null)
        {
            var product = external.ToNewProduct(source);
            await IngestImagesAsync(product, external, cancellationToken);
            repository.Add(product);
            tally.Created++;
        }
        else
        {
            external.ApplyTo(existing);
            await IngestImagesAsync(existing, external, cancellationToken);
            tally.Updated++;
        }

        tally.Pending++;
    }

    /// <summary>
    /// Confirma el lote. Los <c>ProductUpserted</c> viajan a la tabla outbox en
    /// ESTA misma transacción; el worker de indexación hace el resto.
    ///
    /// Si el lote no se puede guardar, se pierde entero, y los productos que ya
    /// se habían contado como creados o actualizados pasan a fallidos. Contarlos
    /// como buenos porque el bucle no lanzó es lo que hacía que la respuesta
    /// dijese <c>"created": 200</c> de una transacción que nunca llegó a Postgres.
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
    /// Ingiere las imágenes en vez de guardar la URL del origen: el catálogo
    /// deja de depender de que Shopify o quien sea mantenga vivo su CDN, y la
    /// fase 4 podrá calcular embeddings sobre bytes que controlamos
    /// (docs/adr/0011-product-images.md).
    ///
    /// Una imagen que no se puede leer no aborta nada: el producto entra sin
    /// ella y la siguiente importación lo reintenta.
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
            product.AddImage(id, image.LocalizedAlt);
        }
    }

    /// <summary>
    /// Los contadores de la importación. Son un tipo y no cuatro variables
    /// locales porque el volcado tiene que poder CORREGIRLOS: hasta que el lote
    /// no está en Postgres, "creado" es una intención, no un hecho.
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

// Los puertos que usa este slice viven en Catalog/Ports: PublishProduct necesita
// los mismos, y un slice no puede referenciar a otro.
