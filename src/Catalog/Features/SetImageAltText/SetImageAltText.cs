using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.SetImageAltText;

/// <summary>
/// Says what one of a product's pictures shows, in both of the shop's languages at
/// once (phase 13a, I-3): alt text is user-facing, and a picture described in one
/// language is a picture half the shop cannot read (invariant 6). It is what a
/// shopkeeper types on the product's screen and what the alt-text job proposes
/// for a person to approve — one command, so the model's path and the path with
/// no model end in the same place.
/// </summary>
public sealed record SetImageAltTextCommand(ProductId ProductId, ImageId ImageId, string Spanish, string English)
    : ICommand<SetImageAltTextResult>;

public enum ImageAltOutcome
{
    Described,
    Unchanged,     // the same words again: one change, not two
    NoSuchImage,   // the product exists and this picture is not one of its own
    Archived,
    NotFound
}

public sealed record SetImageAltTextResult(ImageAltOutcome Outcome, ProductId ProductId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome =>
        Outcome is ImageAltOutcome.Described or ImageAltOutcome.Unchanged ? AuditOutcome.Allowed : AuditOutcome.Denied;

    string? IAuditedResult.AuditReason =>
        Outcome is ImageAltOutcome.Described or ImageAltOutcome.Unchanged ? null : Outcome.ToString();
}

public sealed class SetImageAltTextValidator : AbstractValidator<SetImageAltTextCommand>
{
    public SetImageAltTextValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");

        RuleFor(x => x.ImageId.Value)
            .Must(id => !string.IsNullOrWhiteSpace(id))
            .WithMessage("ImageId must name one of the product's pictures.");

        RuleFor(x => x.Spanish)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .WithMessage("Spanish must say what the picture shows.")
            .MaximumLength(Product.ImageAltMaxLength);

        RuleFor(x => x.English)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .WithMessage("English must say what the picture shows.")
            .MaximumLength(Product.ImageAltMaxLength);
    }
}

public sealed class SetImageAltTextHandler(IProductRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SetImageAltTextCommand, SetImageAltTextResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<SetImageAltTextResult> HandleAsync(SetImageAltTextCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.describe_image");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Outcome(ImageAltOutcome.NotFound, command.ProductId, activity);
        if (product.Status == ProductStatus.Archived)
            return Outcome(ImageAltOutcome.Archived, product.Id, activity);

        var alt = LocalizedText.Both(command.English.Trim(), command.Spanish.Trim());
        if (product.Images.FirstOrDefault(image => image.Id == command.ImageId) is not { } picture)
            return Outcome(ImageAltOutcome.NoSuchImage, product.Id, activity);
        if (picture.Says(alt))
            return Outcome(ImageAltOutcome.Unchanged, product.Id, activity);

        product.DescribeImage(clock, command.ImageId, alt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Outcome(ImageAltOutcome.Described, product.Id, activity);
    }

    private static SetImageAltTextResult Outcome(ImageAltOutcome outcome, ProductId id, Activity? activity)
    {
        activity?.SetTag("catalog.describe_image_outcome", outcome.ToString());
        return new SetImageAltTextResult(outcome, id);
    }
}

// ---------- The wire ----------

public sealed record SetImageAltTextRequest(string Es, string En);

public sealed record SetImageAltTextResponse(string ProductId, string ImageId, ImageAltOutcome Outcome);

public static class ImageAltRefusals
{
    /// <summary>An archived product is out of the catalogue: nothing about it changes until it is restored.</summary>
    public static readonly Refusal Archived = new(
        "ProductArchived", 409, "El producto está archivado.", "The product is archived.");
}

public sealed class SetImageAltTextEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // PUT /api/catalog/products/{id}/images/{imageId}/alt  { "es": "…", "en": "…" }
        // The path with no model: what a shopkeeper types is what an approval applies.
        app.MapPut("/api/catalog/products/{id:guid}/images/{imageId}/alt",
            async Task<Results<Ok<SetImageAltTextResponse>, NotFound, ProblemHttpResult>> (
                   Guid id, string imageId, SetImageAltTextRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new SetImageAltTextCommand(new ProductId(id), new ImageId(imageId), body.Es, body.En), ct);

                return result.Outcome switch
                {
                    ImageAltOutcome.NotFound or ImageAltOutcome.NoSuchImage => TypedResults.NotFound(),
                    ImageAltOutcome.Archived => TypedResults.Problem(
                        statusCode: ImageAltRefusals.Archived.Status,
                        extensions: ImageAltRefusals.Archived.Said(
                            es: "Los productos archivados están fuera del catálogo y sus imágenes no se describen.",
                            en: "Archived products are out of the catalogue and their pictures are not described.")),
                    _ => TypedResults.Ok(new SetImageAltTextResponse(result.ProductId.Value.ToString(), imageId, result.Outcome))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithMetadata(new Refuses(ImageAltRefusals.Archived))
            .WithTags("Catalog")
            .WithName("SetImageAltText");
    }
}
