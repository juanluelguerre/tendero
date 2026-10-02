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
            ? new ProposalSubject(product.Code, new WithdrawalNow(product.Status.ToString()))
            : null;

    private sealed record WithdrawalNow(string Status);
}
