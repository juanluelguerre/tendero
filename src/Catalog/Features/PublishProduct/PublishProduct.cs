using System.Diagnostics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.PublishProduct;

/// <summary>
/// Saca un producto de Draft y lo hace visible. Es el paso que faltaba entre
/// importar y buscar: el conector deja los productos en Draft esperando
/// revisión, y la búsqueda filtra por Active, así que sin esto un catálogo
/// importado es un catálogo invisible.
///
/// La cola de revisión del backoffice (fase 2) consumirá este mismo comando;
/// lo que aquí es una llamada HTTP será allí un botón sobre la misma operación
/// de dominio, sin tocar el slice.
/// </summary>
public sealed record PublishProductCommand(ProductId ProductId) : ICommand<PublishProductResult>;

public enum PublishOutcome
{
    Published,      // estaba en Draft, ahora es Active
    AlreadyActive,  // no había nada que hacer
    Archived,       // fuera de catálogo: reactivar es otra decisión, no ésta
    NotFound
}

public sealed record PublishProductResult(PublishOutcome Outcome, ProductId ProductId, string Status);

/// <summary>
/// La forma que sale por el cable. Existe separada del resultado del handler por
/// dos motivos que se vieron al probarlo contra la API de verdad:
///
/// `ProductId` es un record struct, así que serializado tal cual sale como
/// <c>{"value":"…"}</c>, mientras que el endpoint de búsqueda devuelve el id
/// plano. Dos formas del mismo id en la misma API es una trampa para quien la
/// consuma.
///
/// Y el enum sale como número: <c>"outcome":1</c> no le dice nada a un agente y
/// se rompe en cuanto alguien reordene los miembros.
/// </summary>
public sealed record PublishProductResponse(string ProductId, string Outcome, string Status);

public sealed class PublishProductValidator : AbstractValidator<PublishProductCommand>
{
    public PublishProductValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");
    }
}

public sealed class PublishProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/products/{id}/publish
        app.MapPost("/api/catalog/products/{id:guid}/publish",
            async (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new PublishProductCommand(new ProductId(id)), ct);

                // El handler no sabe de HTTP: decide el resultado de dominio y
                // aquí se traduce, igual que GetProductImage traduce "no está"
                // a 404 sin que el puerto conozca códigos de estado.
                return result.Outcome switch
                {
                    PublishOutcome.NotFound => Results.NotFound(),
                    PublishOutcome.Archived => Results.Conflict(new
                    {
                        title = "The product is archived.",
                        detail = "Archived products stay out of the catalogue. Restore it before publishing."
                    }),
                    // camelCase, no ToLowerInvariant: "alreadyactive" pierde el
                    // limite de palabra y las propiedades del payload ya van en
                    // camelCase, asi que el valor sigue la misma convencion.
                    _ => Results.Ok(new PublishProductResponse(
                        result.ProductId.Value.ToString(),
                        JsonNamingPolicy.CamelCase.ConvertName(result.Outcome.ToString()),
                        result.Status))
                };
            })
            .WithTags("Catalog")
            .WithName("PublishProduct");
    }
}

public sealed class PublishProductHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<PublishProductCommand, PublishProductResult>
{
    private static readonly ActivitySource Telemetry = new("ElGuerre.Tendero.Catalog");

    public async Task<PublishProductResult> HandleAsync(
        PublishProductCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.publish");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Outcome(PublishOutcome.NotFound, command.ProductId, "unknown", activity);

        if (product.Status == ProductStatus.Archived)
            return Outcome(PublishOutcome.Archived, product.Id, product.Status, activity);

        // Publicar lo ya publicado no es un error, pero tampoco es un cambio.
        // Llamar a Publish() de nuevo emitiría otro ProductUpserted y pondría al
        // worker a reescribir un documento idéntico: ruido en el outbox por una
        // operación que no ha alterado nada.
        if (product.Status == ProductStatus.Active)
            return Outcome(PublishOutcome.AlreadyActive, product.Id, product.Status, activity);

        product.Publish();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Outcome(PublishOutcome.Published, product.Id, product.Status, activity);
    }

    private static PublishProductResult Outcome(
        PublishOutcome outcome, ProductId id, ProductStatus status, Activity? activity) =>
        Outcome(outcome, id, status.ToString().ToLowerInvariant(), activity);

    private static PublishProductResult Outcome(
        PublishOutcome outcome, ProductId id, string status, Activity? activity)
    {
        activity?.SetTag("catalog.publish_outcome", outcome.ToString());
        return new PublishProductResult(outcome, id, status);
    }
}
