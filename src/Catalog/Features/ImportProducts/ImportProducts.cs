using System.Diagnostics;
using Carter;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tendero.Catalog.Connectors;
using Tendero.Catalog.Domain;
using Tendero.Catalog.Ports;
using Tendero.SharedKernel;

namespace Tendero.Catalog.Features.ImportProducts;

// NOTA: ICommand/ICommandHandler/ICommandDispatcher son los del MediatR
// custom que ya usas; adapta los nombres a tu implementación.

public sealed record ImportProductsCommand(string Source) : ICommand<ImportProductsResult>;

public sealed record ImportProductsResult(int Created, int Updated, int Failed, double ElapsedSeconds);

public sealed class ImportProductsValidator : AbstractValidator<ImportProductsCommand>
{
    public ImportProductsValidator()
    {
        RuleFor(x => x.Source)
            .NotEmpty()
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Source must be a lowercase connector key, e.g. 'seed' or 'shopify'.");
    }
}

public sealed class ImportProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/import  { "source": "seed" }
        app.MapPost("/api/catalog/import",
            async (ImportProductsCommand command, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(command, ct);
                return Results.Ok(result);
            })
            .WithTags("Catalog")
            .WithName("ImportProducts");
    }
}

public sealed class ImportProductsHandler(
    IServiceProvider services,
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    IExternalImageReader imageReader,
    IImageStore imageStore,
    ILogger<ImportProductsHandler> logger)
    : ICommandHandler<ImportProductsCommand, ImportProductsResult>
{
    private static readonly ActivitySource Telemetry = new("Tendero.Catalog");
    private const int BatchSize = 200;

    public async Task<ImportProductsResult> HandleAsync(
        ImportProductsCommand command, CancellationToken cancellationToken)
    {
        // Keyed DI: el nombre del conector llega en la request, la resolución
        // es del contenedor. Añadir un origen nuevo = registrar una clase, cero ifs.
        var connector = services.GetRequiredKeyedService<ICatalogSourceConnector>(command.Source);

        using var activity = Telemetry.StartActivity("catalog.import");
        activity?.SetTag("catalog.source", connector.Source);

        var stopwatch = Stopwatch.StartNew();
        int created = 0, updated = 0, failed = 0, pending = 0;

        await foreach (var external in connector.StreamProductsAsync(cancellationToken))
        {
            try
            {
                var existing = await repository.FindByExternalReferenceAsync(
                    connector.Source, external.ExternalId, cancellationToken);

                if (existing is null)
                {
                    var product = Product.Create(
                        external.LocalizedName, external.Price, external.LocalizedDescription);
                    product.UpdateDetails(
                        external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
                    product.LinkExternal(connector.Source, external.ExternalId);
                    await ApplyMediaAsync(product, external, cancellationToken);
                    repository.Add(product);
                    created++;
                }
                else
                {
                    existing.UpdateDetails(
                        external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
                    existing.SetPrice(external.Price);
                    await ApplyMediaAsync(existing, external, cancellationToken);
                    updated++;
                }

                // Cada SaveChanges vuelca también los ProductUpserted a la tabla
                // Outbox en la MISMA transacción; el worker de indexación hará el resto.
                if (++pending >= BatchSize)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    pending = 0;
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "Failed to import product {ExternalId} from {Source}",
                    external.ExternalId, connector.Source);
            }
        }

        if (pending > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        activity?.SetTag("catalog.import.created", created);
        activity?.SetTag("catalog.import.updated", updated);
        activity?.SetTag("catalog.import.failed", failed);

        logger.LogInformation(
            "Import from {Source}: {Created} created, {Updated} updated, {Failed} failed in {Elapsed:0.0}s",
            connector.Source, created, updated, failed, stopwatch.Elapsed.TotalSeconds);

        return new ImportProductsResult(created, updated, failed, stopwatch.Elapsed.TotalSeconds);
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
    private async Task ApplyMediaAsync(
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

        foreach (var (name, value) in external.Attributes)
            product.SetAttribute(name, value);
    }
}

// Los puertos que usa este slice viven en Catalog/Ports: PublishProduct necesita
// los mismos, y un slice no puede referenciar a otro.
