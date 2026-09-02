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
/// Genera la matriz de variantes de un producto a partir de sus ejes.
///
/// Es una operación del tendero, no del conector: un origen puede traer
/// variantes o no traerlas, pero decidir que una camiseta se vende en tres
/// colores por cuatro tallas es una decisión de catálogo. Por eso vive detrás de
/// la política de tendero y no dentro de la importación.
///
/// La matriz se genera entera —el producto cartesiano de los ejes— porque es lo
/// que un tendero espera al declarar "colores × tallas", y retirar las
/// combinaciones que no existen es más rápido que crearlas una a una.
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
    /// El tope existe porque el producto cartesiano crece rápido y un error de
    /// dedo —pegar una lista de doscientas tallas— no debería escribir miles de
    /// filas antes de que nadie lo note.
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

/// <summary>La forma que entra por el cable, sin ids fuertemente tipados.</summary>
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
            // El agregado defiende sus invariantes; aquí sólo se traducen. Un
            // 409 con el motivo del dominio dice más que un 500 con una traza.
            activity?.SetTag("catalog.define_variants_rejected", rejected.Message);
            return new DefineVariantsResult(DefineVariantsOutcome.Rejected, 0, before, rejected.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var created = product.Variants.Count - before;
        activity?.SetTag("catalog.variants_created", created);

        return new DefineVariantsResult(DefineVariantsOutcome.Defined, created, before, null);
    }

    /// <summary>
    /// El id externo del producto si lo tiene, y si no, su id. El SKU es lo que
    /// otros contextos leen, así que conviene que se reconozca de un vistazo.
    /// </summary>
    private static string DefaultPrefix(Product product) =>
        product.ExternalReferences.Count > 0
            ? product.ExternalReferences[0].ExternalId
            : product.Id.Value.ToString("N")[..8].ToUpperInvariant();

    /// <summary>Producto cartesiano de los ejes, en el orden declarado.</summary>
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
