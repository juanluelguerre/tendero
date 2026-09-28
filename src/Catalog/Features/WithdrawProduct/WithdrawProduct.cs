using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.WithdrawProduct;

/// <summary>
/// Takes a product off sale and back to the review queue, the inverse of
/// PublishProduct. A shop needs it the day a supplier recalls something or a
/// price turns out wrong: archiving is for good, and until this existed a live
/// product could only be left on sale or archived. It was found missing when the
/// redesign's walkthrough needed drafts to review and the laboratory had none.
/// </summary>
public sealed record WithdrawProductCommand(ProductId ProductId) : ICommand<WithdrawProductResult>;

public enum WithdrawOutcome
{
    Withdrawn,      // it was Active, it is in Draft now
    AlreadyDraft,   // there was nothing to do
    Archived,       // out of the catalogue already
    NotFound
}

/// <param name="Status">Where the product stands after the command; null when there is no product.</param>
public sealed record WithdrawProductResult(WithdrawOutcome Outcome, ProductId ProductId, ProductStatus? Status)
    : IAuditedResult
{
    /// <summary>Withdrawing what is already a draft is idempotence, as publishing
    /// twice is (ADR 0012). An archived product is a refusal.</summary>
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        WithdrawOutcome.Withdrawn or WithdrawOutcome.AlreadyDraft => AuditOutcome.Allowed,
        WithdrawOutcome.Archived or WithdrawOutcome.NotFound => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome is WithdrawOutcome.Withdrawn or WithdrawOutcome.AlreadyDraft ? null : Outcome.ToString();
}

/// <summary>The wire's shape: a flat id and a named outcome, as PublishProductResponse.</summary>
public sealed record WithdrawProductResponse(string ProductId, WithdrawOutcome Outcome, ProductStatus Status);

public sealed record WithdrawProductConflict(string Title, string Detail);

public sealed class WithdrawProductValidator : AbstractValidator<WithdrawProductCommand>
{
    public WithdrawProductValidator()
    {
        RuleFor(x => x.ProductId.Value)
            .NotEqual(Guid.Empty)
            .WithMessage("ProductId must be a non-empty product identifier.");
    }
}

public sealed class WithdrawProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/products/{id}/withdraw
        app.MapPost("/api/catalog/products/{id:guid}/withdraw",
            async Task<Results<Ok<WithdrawProductResponse>, NotFound, Conflict<WithdrawProductConflict>>> (
                   Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new WithdrawProductCommand(new ProductId(id)), ct);

                return result.Outcome switch
                {
                    WithdrawOutcome.NotFound => TypedResults.NotFound(),
                    WithdrawOutcome.Archived => TypedResults.Conflict(new WithdrawProductConflict(
                        "The product is archived.",
                        "Archived products are already out of the catalogue.")),
                    // Past the two refusals there is always a product, so a status.
                    _ => TypedResults.Ok(new WithdrawProductResponse(
                        result.ProductId.Value.ToString(),
                        result.Outcome,
                        result.Status!.Value))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("WithdrawProduct");
    }
}

public sealed class WithdrawProductHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IActorAccessor actors)
    : ICommandHandler<WithdrawProductCommand, WithdrawProductResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<WithdrawProductResult> HandleAsync(
        WithdrawProductCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.withdraw");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Outcome(WithdrawOutcome.NotFound, command.ProductId, null, activity);

        if (product.Status == ProductStatus.Archived)
            return Outcome(WithdrawOutcome.Archived, product.Id, product.Status, activity);

        // Nothing to change, and no ProductUpserted to send the worker rewriting
        // an index that already leaves this product out.
        if (product.Status == ProductStatus.Draft)
            return Outcome(WithdrawOutcome.AlreadyDraft, product.Id, product.Status, activity);

        product.Withdraw(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        ProductsWithdrawn.Record(actors.Current);

        return Outcome(WithdrawOutcome.Withdrawn, product.Id, product.Status, activity);
    }

    private static WithdrawProductResult Outcome(
        WithdrawOutcome outcome, ProductId id, ProductStatus? status, Activity? activity)
    {
        activity?.SetTag("catalog.withdraw_outcome", outcome.ToString());
        return new WithdrawProductResult(outcome, id, status);
    }
}

/// <summary>
/// `tendero.catalog.products.withdrawn` (the tracking plan): how often a live
/// product has to come back to review, and who pulls it. Only a real Active →
/// Draft counts.
/// </summary>
internal static class ProductsWithdrawn
{
    private static readonly Meter Meter = new(TelemetrySources.Catalog);

    private static readonly Counter<long> Withdrawn = Meter.CreateCounter<long>(
        "tendero.catalog.products.withdrawn", unit: "{product}", description: "Products moved from Active back to Draft.");

    public static void Record(Actor actor) => Withdrawn.Add(1, actor.KindLabel);
}
