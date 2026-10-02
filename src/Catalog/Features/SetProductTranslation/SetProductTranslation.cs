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

namespace ElGuerre.Tendero.Catalog.Features.SetProductTranslation;

/// <summary>
/// Writes a product's name, and optionally its description, in one supported
/// culture, leaving the others alone. It is what Steward proposes for a product
/// with no English name (phase 12), and the first command a model may propose:
/// it adds the missing language and never rewrites the original.
/// </summary>
public sealed record SetProductTranslationCommand(ProductId ProductId, string Culture, string Name, string? Description)
    : ICommand<SetProductTranslationResult>;

public enum TranslationOutcome
{
    Translated,
    Unchanged,   // the same text again: one change, not two
    Archived,
    NotFound
}

public sealed record SetProductTranslationResult(TranslationOutcome Outcome, ProductId ProductId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        TranslationOutcome.Translated or TranslationOutcome.Unchanged => AuditOutcome.Allowed,
        TranslationOutcome.Archived or TranslationOutcome.NotFound => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome is TranslationOutcome.Translated or TranslationOutcome.Unchanged ? null : Outcome.ToString();
}

public sealed class SetProductTranslationValidator : AbstractValidator<SetProductTranslationCommand>
{
    public SetProductTranslationValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");

        RuleFor(x => x.Culture)
            .Must(CultureNegotiation.Supports)
            .WithMessage($"Culture must be one the shop speaks: {string.Join(", ", CultureNegotiation.Supported)}.");

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name must not be blank.")
            .MaximumLength(Product.NameMaxLength);

        RuleFor(x => x.Description)
            .Must(description => description is null || !string.IsNullOrWhiteSpace(description))
            .WithMessage("Description must be left out or have text.")
            .MaximumLength(Product.DescriptionMaxLength);
    }
}

public sealed class SetProductTranslationHandler(IProductRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SetProductTranslationCommand, SetProductTranslationResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<SetProductTranslationResult> HandleAsync(
        SetProductTranslationCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.translate");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);
        activity?.SetTag("catalog.culture", command.Culture);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Outcome(TranslationOutcome.NotFound, command.ProductId, activity);
        if (product.Status == ProductStatus.Archived)
            return Outcome(TranslationOutcome.Archived, product.Id, activity);

        var culture = Culture.Normalize(command.Culture);
        if (Says(product.Name, culture, command.Name)
            && (command.Description is null || Says(product.Description, culture, command.Description)))
            return Outcome(TranslationOutcome.Unchanged, product.Id, activity);

        product.Localize(clock, culture, command.Name, command.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Outcome(TranslationOutcome.Translated, product.Id, activity);
    }

    private static bool Says(LocalizedText? text, string culture, string value) =>
        text is not null && text.Values.TryGetValue(culture, out var current) && current == value;

    private static SetProductTranslationResult Outcome(TranslationOutcome outcome, ProductId id, Activity? activity)
    {
        activity?.SetTag("catalog.translate_outcome", outcome.ToString());
        return new SetProductTranslationResult(outcome, id);
    }
}

// ---------- The wire ----------

public sealed record SetProductTranslationRequest(string Name, string? Description);

public sealed record SetProductTranslationResponse(string ProductId, TranslationOutcome Outcome);

public sealed record SetProductTranslationConflict(string Title, string Detail);

public sealed class SetProductTranslationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // PUT /api/catalog/products/{id}/translations/en  { "name": "…", "description": "…" }
        app.MapPut("/api/catalog/products/{id:guid}/translations/{culture}",
            async Task<Results<Ok<SetProductTranslationResponse>, NotFound, Conflict<SetProductTranslationConflict>>> (
                   Guid id, string culture, SetProductTranslationRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new SetProductTranslationCommand(new ProductId(id), culture, body.Name, body.Description), ct);

                return result.Outcome switch
                {
                    TranslationOutcome.NotFound => TypedResults.NotFound(),
                    TranslationOutcome.Archived => TypedResults.Conflict(new SetProductTranslationConflict(
                        "The product is archived.",
                        "Archived products are out of the catalogue and are not translated.")),
                    _ => TypedResults.Ok(new SetProductTranslationResponse(result.ProductId.Value.ToString(), result.Outcome))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("SetProductTranslation");
    }
}
