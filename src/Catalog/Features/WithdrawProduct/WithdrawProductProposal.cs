using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.WithdrawProduct;

public sealed record WithdrawProductPayload(Guid ProductId);

/// <summary>Makes withdrawing proposable (phase 12): stale once the product's status moved.</summary>
public sealed class WithdrawProductProposal(IServiceProvider services, IProductRepository products)
    : ProposableCommand<WithdrawProductPayload, WithdrawProductCommand, WithdrawProductResult>(services)
{
    public const string CommandName = "WithdrawProduct";

    public override string Name => CommandName;

    public override LocalizedText Label { get; } = new(new Dictionary<string, string>
    {
        ["en"] = "Withdraw a product",
        ["es"] = "Retirar un producto",
    });

    protected override WithdrawProductCommand ToCommand(WithdrawProductPayload payload) => new(new ProductId(payload.ProductId));

    protected override async Task<ProposalSubject?> ReadAsync(WithdrawProductCommand command, CancellationToken cancellationToken) =>
        await products.FindByIdAsync(command.ProductId, cancellationToken) is { } product
            ? new ProposalSubject(product.Code, new WithdrawalNow(product.Status.ToString()), new ProposalDisplay(
                product.Name,
                LocalizedText.Both("Withdrawal", "Retirada"),
                [new ProposalChange(LocalizedText.Both("Status", "Estado"), StatusWords(product.Status), StatusWords(ProductStatus.Draft))]))
            : null;

    /// <summary>A status as the backoffice's chips say it: "On sale", never "Active".</summary>
    private static LocalizedText StatusWords(ProductStatus status) => status switch
    {
        ProductStatus.Active => LocalizedText.Both("On sale", "A la venta"),
        ProductStatus.Draft => LocalizedText.Both("Draft", "Borrador"),
        _ => LocalizedText.Both("Archived", "Archivado"),
    };

    private sealed record WithdrawalNow(string Status);
}
