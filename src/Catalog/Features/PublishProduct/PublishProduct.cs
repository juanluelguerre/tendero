using System.Diagnostics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.PublishProduct;

/// <summary>
/// Takes a product out of Draft and makes it visible. It is the step that was
/// missing between importing and searching: the connector leaves products in
/// Draft awaiting review, and search filters on Active, so without this an
/// imported catalogue is an invisible catalogue.
///
/// The backoffice review queue consumes this very command; what is an HTTP call
/// here is a button there, over the same domain operation, without touching the
/// slice.
/// </summary>
public sealed record PublishProductCommand(ProductId ProductId) : ICommand<PublishProductResult>;

public enum PublishOutcome
{
    Published,      // it was in Draft, it is Active now
    AlreadyActive,  // there was nothing to do
    Archived,       // out of the catalogue: restoring it is a different decision
    NotFound
}

public sealed record PublishProductResult(PublishOutcome Outcome, ProductId ProductId, string Status);

/// <summary>
/// The shape that goes out over the wire. It exists apart from the handler's
/// result for two reasons, both found by trying it against the real API:
///
/// `ProductId` is a record struct, so serialised as-is it comes out as
/// <c>{"value":"…"}</c>, while the search endpoint returns the flat id. Two
/// shapes of the same id in one API is a trap for whoever consumes it.
///
/// And the enum comes out as a number: <c>"outcome":1</c> tells an agent nothing
/// and breaks the moment somebody reorders the members.
/// </summary>
public sealed record PublishProductResponse(string ProductId, string Outcome, string Status);

/// <summary>
/// The 409's body. It used to be an anonymous object, which produces the same
/// JSON and no schema at all: a generated client could not see that this
/// endpoint can refuse on the grounds of being archived, nor in what shape.
/// </summary>
public sealed record PublishProductConflict(string Title, string Detail);

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
        // All three results are declared in the signature. The conflict's
        // anonymous object was invisible to any client — it had no name, so it
        // could have no schema — and it is now a record with the same JSON.
        app.MapPost("/api/catalog/products/{id:guid}/publish",
            async Task<Results<Ok<PublishProductResponse>, NotFound, Conflict<PublishProductConflict>>> (
                   Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new PublishProductCommand(new ProductId(id)), ct);

                // The handler knows nothing about HTTP: it decides the domain
                // outcome and it is translated here, the same way GetProductImage
                // translates "not there" into a 404 without the port knowing
                // status codes.
                return result.Outcome switch
                {
                    PublishOutcome.NotFound => TypedResults.NotFound(),
                    PublishOutcome.Archived => TypedResults.Conflict(new PublishProductConflict(
                        "The product is archived.",
                        "Archived products stay out of the catalogue. Restore it before publishing.")),
                    // camelCase, not ToLowerInvariant: "alreadyactive" loses the
                    // word boundary, and the payload's properties already travel
                    // in camelCase, so the value follows the same convention.
                    _ => TypedResults.Ok(new PublishProductResponse(
                        result.ProductId.Value.ToString(),
                        JsonNamingPolicy.CamelCase.ConvertName(result.Outcome.ToString()),
                        result.Status))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("PublishProduct");
    }
}

public sealed class PublishProductHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<PublishProductCommand, PublishProductResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

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

        // Publishing what is already published is not an error, but it is not a
        // change either. Calling Publish() again would emit another
        // ProductUpserted and set the worker rewriting an identical document:
        // noise in the outbox for an operation that altered nothing.
        if (product.Status == ProductStatus.Active)
            return Outcome(PublishOutcome.AlreadyActive, product.Id, product.Status, activity);

        product.Publish(clock);
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
