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

namespace ElGuerre.Tendero.Catalog.Features.SetImageShows;

/// <summary>
/// Says which of a product's variants one of its pictures shows (ADR 0057): the
/// colour, usually — <c>COLOR: BLACK</c> — and nothing at all for a picture that
/// is every variant's, the back or a detail. It is what a shopkeeper chooses on
/// the product's screen, beside the picture; an import says the same thing in the
/// source's words. Saying nothing is a value like any other: it is how a tag is
/// taken off.
/// </summary>
/// <param name="Shows">Axis code to the value shown, at most one an axis; empty for a picture of no variant in particular.</param>
/// <param name="KeepOtherAxes">
/// Whether what the picture already shows on the axes not named stays. A
/// shopkeeper says everything a picture shows at once, so no; whoever corrects
/// one axis — a doubt about its colour, decided — knows only that one, so yes.
/// </param>
public sealed record SetImageShowsCommand(
    ProductId ProductId, ImageId ImageId, IReadOnlyDictionary<string, string> Shows, bool KeepOtherAxes = false)
    : ICommand<SetImageShowsResult>;

public enum ImageShowsOutcome
{
    Shown,
    Unchanged,     // what it already said: one change, not two
    NoSuchImage,   // the product exists and this picture is not one of its own
    NotSold,       // an axis the product does not vary by, or a value no variant still sold holds
    Archived,
    NotFound
}

/// <param name="Refusal">Why the aggregate refused, in its own words, when it did.</param>
public sealed record SetImageShowsResult(ImageShowsOutcome Outcome, ProductId ProductId, string? Refusal = null) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome =>
        Outcome is ImageShowsOutcome.Shown or ImageShowsOutcome.Unchanged ? AuditOutcome.Allowed : AuditOutcome.Denied;

    string? IAuditedResult.AuditReason =>
        Outcome is ImageShowsOutcome.Shown or ImageShowsOutcome.Unchanged ? null : Outcome.ToString();
}

public sealed class SetImageShowsValidator : AbstractValidator<SetImageShowsCommand>
{
    public SetImageShowsValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");

        RuleFor(x => x.ImageId.Value)
            .Must(id => !string.IsNullOrWhiteSpace(id))
            .WithMessage("ImageId must name one of the product's pictures.");

        RuleFor(x => x.Shows)
            .NotNull()
            .WithMessage("Shows must be given; empty for a picture that shows no variant in particular.")
            .Must(shows => shows is null || shows.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
            .WithMessage("Shows must name an axis and the value of it the picture shows.");
    }
}

public sealed class SetImageShowsHandler(IProductRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SetImageShowsCommand, SetImageShowsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<SetImageShowsResult> HandleAsync(SetImageShowsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.show_in_image");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Outcome(ImageShowsOutcome.NotFound, command.ProductId, activity);
        if (product.Status == ProductStatus.Archived)
            return Outcome(ImageShowsOutcome.Archived, product.Id, activity);
        if (product.Images.FirstOrDefault(image => image.Id == command.ImageId) is not { } picture)
            return Outcome(ImageShowsOutcome.NoSuchImage, product.Id, activity);

        var before = picture.Shows;
        var shows = command.KeepOtherAxes
            ? before
                .Where(shown => !command.Shows.Keys.Contains(shown.Axis, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(shown => shown.Axis, shown => shown.Value)
                .Concat(command.Shows)
                .ToDictionary(pair => pair.Key, pair => pair.Value)
            : command.Shows;
        try
        {
            product.Show(clock, command.ImageId, shows);
        }
        catch (InvalidOperationException refused)
        {
            // The rule is the aggregate's, and so are the words for it.
            return Outcome(ImageShowsOutcome.NotSold, product.Id, activity, refused.Message);
        }

        if (product.Images.First(image => image.Id == command.ImageId).ShowsExactly(before))
            return Outcome(ImageShowsOutcome.Unchanged, product.Id, activity);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Outcome(ImageShowsOutcome.Shown, product.Id, activity);
    }

    private static SetImageShowsResult Outcome(ImageShowsOutcome outcome, ProductId id, Activity? activity, string? refusal = null)
    {
        activity?.SetTag("catalog.show_in_image_outcome", outcome.ToString());
        return new SetImageShowsResult(outcome, id, refusal);
    }
}

// ---------- The wire ----------

/// <param name="Shows">Axis code to the value shown; empty takes the tag off.</param>
public sealed record SetImageShowsRequest(IReadOnlyDictionary<string, string> Shows);

public sealed record SetImageShowsResponse(string ProductId, string ImageId, ImageShowsOutcome Outcome);

public static class ImageShowsRefusals
{
    /// <summary>An archived product is out of the catalogue: nothing about it changes until it is restored.</summary>
    public static readonly Refusal Archived = new(
        "ProductArchived", 409, "El producto está archivado.", "The product is archived.");

    /// <summary>A picture shows what a variant still sold holds, and nothing else (ADR 0056).</summary>
    public static readonly Refusal NotSold = new(
        "NotSold", 409, "Ninguna variante del producto es eso.", "No variant of the product is that.");
}

public sealed class SetImageShowsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // PUT /api/catalog/products/{id}/images/{imageId}/shows  { "shows": { "COLOR": "BLACK" } }
        app.MapPut("/api/catalog/products/{id:guid}/images/{imageId}/shows",
            async Task<Results<Ok<SetImageShowsResponse>, NotFound, ProblemHttpResult>> (
                   Guid id, string imageId, SetImageShowsRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new SetImageShowsCommand(new ProductId(id), new ImageId(imageId), body.Shows), ct);

                return result.Outcome switch
                {
                    ImageShowsOutcome.NotFound or ImageShowsOutcome.NoSuchImage => TypedResults.NotFound(),
                    ImageShowsOutcome.Archived => TypedResults.Problem(
                        statusCode: ImageShowsRefusals.Archived.Status,
                        extensions: ImageShowsRefusals.Archived.Said(
                            es: "Los productos archivados están fuera del catálogo y sus imágenes no se cambian.",
                            en: "Archived products are out of the catalogue and their pictures are not changed.")),
                    ImageShowsOutcome.NotSold => TypedResults.Problem(
                        statusCode: ImageShowsRefusals.NotSold.Status,
                        extensions: ImageShowsRefusals.NotSold.Said(
                            es: "Una imagen solo puede mostrar lo que tiene una variante que todavía se vende.",
                            en: "A picture can only show what a variant still sold holds.",
                            reason: result.Refusal)),
                    _ => TypedResults.Ok(new SetImageShowsResponse(result.ProductId.Value.ToString(), imageId, result.Outcome))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithMetadata(new Refuses(ImageShowsRefusals.Archived, ImageShowsRefusals.NotSold))
            .WithTags("Catalog")
            .WithName("SetImageShows");
    }
}
