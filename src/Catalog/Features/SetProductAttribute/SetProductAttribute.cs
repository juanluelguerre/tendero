using System.Diagnostics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;

namespace ElGuerre.Tendero.Catalog.Features.SetProductAttribute;

/// <summary>
/// Sets one attribute of a product to a value its definition accepts. The first
/// writer is an approved claim (P10-6): Knowledge never writes to Catalog — the
/// worker, which composes both, turns a person's approval into this command, and
/// Catalog decides whether the value is one it can hold (ADR 0018).
///
/// No endpoint: nothing outside the process sends it today. When the backoffice
/// edits attributes, it gets one over this same command.
/// </summary>
/// <param name="Reason">Why it was set — "claim {id} approved by Ana" — for the audit row.</param>
public sealed record SetProductAttributeCommand(ProductId ProductId, string AttributeCode, string Value, string Reason)
    : ICommand<SetProductAttributeResult>;

public enum SetAttributeOutcome
{
    Set,

    /// <summary>The product already held exactly this value: nothing changed, nothing reindexes.</summary>
    Unchanged,

    ProductNotFound,

    /// <summary>No definition answers to the code: Catalog does not invent attributes.</summary>
    UnknownAttribute,

    /// <summary>The definition does not accept the value — an option it does not have, a number that is not one.</summary>
    ValueRejected,

    /// <summary>The product varies by this attribute: its variants say the value, and the product holds none of its own.</summary>
    VariantAxis,
}

/// <param name="HeldAs">The value's <see cref="AttributeValue.Key"/> once set — what the product holds — or null when refused.</param>
public sealed record SetProductAttributeResult(SetAttributeOutcome Outcome, ProductId ProductId, string AttributeCode, string? HeldAs = null)
    : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome =>
        Outcome is SetAttributeOutcome.Set or SetAttributeOutcome.Unchanged ? AuditOutcome.Allowed : AuditOutcome.Denied;

    string? IAuditedResult.AuditReason =>
        Outcome is SetAttributeOutcome.Set or SetAttributeOutcome.Unchanged ? null : Outcome.ToString();
}

public sealed class SetProductAttributeValidator : AbstractValidator<SetProductAttributeCommand>
{
    public SetProductAttributeValidator()
    {
        RuleFor(x => x.ProductId.Value).NotEqual(Guid.Empty);
        RuleFor(x => x.AttributeCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Value).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

public sealed class SetProductAttributeHandler(
    IProductRepository repository,
    IAttributeDefinitionReader definitions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetProductAttributeCommand, SetProductAttributeResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<SetProductAttributeResult> HandleAsync(SetProductAttributeCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.set_attribute");
        activity?.SetTag("catalog.product_id", command.ProductId.Value);
        activity?.SetTag("catalog.attribute", command.AttributeCode);

        var result = await SetAsync(command, cancellationToken);
        activity?.SetTag("catalog.set_attribute_outcome", result.Outcome.ToString());
        return result;
    }

    private async Task<SetProductAttributeResult> SetAsync(SetProductAttributeCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.FindByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return Result(SetAttributeOutcome.ProductNotFound, command.AttributeCode);

        var definition = (await definitions.AllAsync(cancellationToken)).ByCode(command.AttributeCode);
        if (definition is null)
            return Result(SetAttributeOutcome.UnknownAttribute, command.AttributeCode);

        if (product.VariesBy(definition.Code))
            return Result(SetAttributeOutcome.VariantAxis, definition.Code);

        if (definition.Accept(command.Value) is not { } value)
            return Result(SetAttributeOutcome.ValueRejected, definition.Code);

        // Setting what is already there would raise ProductUpserted and reindex
        // an identical document — the same reasoning as publishing twice.
        if (product.AttributeFor(definition.Code) == value)
            return Result(SetAttributeOutcome.Unchanged, definition.Code, value.Key);

        product.SetAttribute(clock, value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result(SetAttributeOutcome.Set, definition.Code, value.Key);

        SetProductAttributeResult Result(SetAttributeOutcome outcome, string code, string? heldAs = null) =>
            new(outcome, command.ProductId, code, heldAs);
    }
}
