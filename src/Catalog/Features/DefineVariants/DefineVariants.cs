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

namespace ElGuerre.Tendero.Catalog.Features.DefineVariants;

/// <summary>
/// Generates a product's variant matrix from its axes.
///
/// A shopkeeper's operation, not a connector's: a source may bring variants or
/// not, but deciding that a shirt sells in three colours by four sizes is a
/// catalogue decision. That is why it lives behind the shopkeeper policy and not
/// inside importing.
///
/// The whole matrix is generated — the cartesian product of the axes — because
/// that is what a shopkeeper expects on declaring "colours × sizes", and
/// retiring the combinations that do not exist is quicker than creating them one
/// by one.
/// </summary>
public sealed record DefineVariantsCommand(
    ProductId ProductId,
    IReadOnlyList<VariantAxis> Axes,
    string? SkuPrefix)
    : ICommand<DefineVariantsResult>;

/// <summary>Un eje y sus opciones: <c>COLOR</c> → NAVY, BLACK.</summary>
public sealed record VariantAxis(string Code, IReadOnlyList<string> Options);

public enum DefineVariantsOutcome { Defined, NotFound, Rejected }

public sealed record DefineVariantsResult(
    DefineVariantsOutcome Outcome, int Created, int Existing, string? Reason);

public sealed record DefineVariantsResponse(int Created, int Existing);

public sealed class DefineVariantsValidator : AbstractValidator<DefineVariantsCommand>
{
    /// <summary>
    /// The cap exists because the cartesian product grows fast, and a slip of the
    /// finger — pasting a list of two hundred sizes — should not write thousands
    /// of rows before anybody notices.
    /// </summary>
    private const int MaximumCombinations = 200;

    public DefineVariantsValidator()
    {
        RuleFor(command => command.ProductId.Value).NotEqual(Guid.Empty);

        RuleFor(command => command.Axes)
            .NotEmpty().WithMessage("At least one axis is required.")
            .Must(axes => axes.All(axis => !string.IsNullOrWhiteSpace(axis.Code)))
                .WithMessage("Every axis needs a code.")
            .Must(axes => axes.All(axis => axis.Options.Count > 0))
                .WithMessage("Every axis needs at least one option.")
            .Must(axes => axes.Select(axis => axis.Code.Trim())
                              .Distinct(StringComparer.OrdinalIgnoreCase).Count() == axes.Count)
                .WithMessage("Axis codes must be distinct.")
            .Must(axes => axes.Aggregate(1, (total, axis) => total * axis.Options.Count) <= MaximumCombinations)
                .WithMessage($"That would create more than {MaximumCombinations} variants.");
    }
}

public sealed class DefineVariantsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/products/{id}/variants
        app.MapPost("/api/catalog/products/{id:guid}/variants",
            async Task<Results<Ok<DefineVariantsResponse>, NotFound, Conflict<string>>> (
                   Guid id, DefineVariantsRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(
                    new DefineVariantsCommand(
                        new ProductId(id),
                        [.. request.Axes.Select(axis => new VariantAxis(axis.Code, axis.Options))],
                        request.SkuPrefix),
                    ct);

                return result.Outcome switch
                {
                    DefineVariantsOutcome.NotFound => TypedResults.NotFound(),
                    DefineVariantsOutcome.Rejected => TypedResults.Conflict(result.Reason!),
                    _ => TypedResults.Ok(new DefineVariantsResponse(result.Created, result.Existing))
                };
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("DefineVariants");
    }
}

/// <summary>The shape that arrives over the wire, without strongly-typed ids.</summary>
public sealed record DefineVariantsRequest(
    IReadOnlyList<VariantAxisRequest> Axes, string? SkuPrefix);

public sealed record VariantAxisRequest(string Code, IReadOnlyList<string> Options);

public sealed class DefineVariantsHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<DefineVariantsCommand, DefineVariantsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<DefineVariantsResult> HandleAsync(
        DefineVariantsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.define_variants");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);

        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return new DefineVariantsResult(DefineVariantsOutcome.NotFound, 0, 0, null);

        var before = product.Variants.Count;

        try
        {
            product.DefineAxes(clock, [.. command.Axes.Select(axis => axis.Code)]);

            var prefix = command.SkuPrefix?.Trim() is { Length: > 0 } given
                ? given
                : DefaultPrefix(product);

            foreach (var combination in Combinations(command.Axes))
            {
                var sku = $"{prefix}-{string.Join('-', combination.Values)}";
                product.AddVariant(clock, sku, product.Price, combination);
            }
        }
        catch (InvalidOperationException rejected)
        {
            // The aggregate defends its invariants; here they are only
            // translated. A 409 with the domain's reason says more than a 500
            // with a stack trace.
            activity?.SetTag("catalog.define_variants_rejected", rejected.Message);
            return new DefineVariantsResult(DefineVariantsOutcome.Rejected, 0, before, rejected.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var created = product.Variants.Count - before;
        activity?.SetTag("catalog.variants_created", created);

        return new DefineVariantsResult(DefineVariantsOutcome.Defined, created, before, null);
    }

    /// <summary>
    /// The product's external id if it has one, and its id otherwise. The SKU is
    /// what other contexts read, so it is worth being recognisable at a glance.
    /// </summary>
    private static string DefaultPrefix(Product product) =>
        product.ExternalReferences.Count > 0
            ? product.ExternalReferences[0].ExternalId
            : product.Id.Value.ToString("N")[..8].ToUpperInvariant();

    /// <summary>The cartesian product of the axes, in the declared order.</summary>
    private static IEnumerable<Dictionary<string, string>> Combinations(IReadOnlyList<VariantAxis> axes)
    {
        IEnumerable<Dictionary<string, string>> combinations =
            [new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)];

        foreach (var axis in axes)
        {
            combinations = combinations.SelectMany(
                _ => axis.Options,
                (partial, option) => new Dictionary<string, string>(partial, StringComparer.OrdinalIgnoreCase)
                {
                    [axis.Code.Trim()] = option.Trim()
                });
        }

        return combinations;
    }
}
