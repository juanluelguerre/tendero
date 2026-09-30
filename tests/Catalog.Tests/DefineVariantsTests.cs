using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.DefineVariants;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

public sealed class DefineVariantsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task It_generates_the_whole_matrix()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new DefineVariantsCommand(
                product.Id,
                [new VariantAxis("COLOR", ["NAVY", "BLACK"]), new VariantAxis("SIZE", ["M", "L", "XL"])],
                SkuPrefix: "SHIRT"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DefineVariantsOutcome.Defined, result.Outcome);
        Assert.Equal(6, result.Created);
        Assert.Equal(6, product.Variants.Count);
        Assert.Equal(1, unitOfWork.SaveCount);

        // The SKU reads at a glance, which is what is asked of it: it is what
        // other contexts — inventory, cart, UCP — use to talk about this.
        Assert.NotNull(product.VariantBySku("SHIRT-NAVY-M"));
        Assert.NotNull(product.VariantBySku("SHIRT-BLACK-XL"));
    }

    /// <summary>
    /// The aggregate defends its invariant and the handler translates it: a 409
    /// with the domain's reason says more than a 500 with a stack trace.
    /// </summary>
    [Fact]
    public async Task Redefining_axes_over_existing_variants_is_rejected_with_a_reason()
    {
        var product = AProduct();
        product.DefineAxes(Clock, ["COLOR"]);
        product.AddVariant(Clock, "SHIRT-NAVY", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY" });

        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new DefineVariantsCommand(product.Id, [new VariantAxis("SIZE", ["M"])], null),
            TestContext.Current.CancellationToken);

        Assert.Equal(DefineVariantsOutcome.Rejected, result.Outcome);
        Assert.Contains("axes cannot change", result.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var handler = new DefineVariantsHandler(
            new InMemoryProductRepository([]), new CountingUnitOfWork(), Clock);

        var result = await handler.HandleAsync(
            new DefineVariantsCommand(ProductId.New(), [new VariantAxis("COLOR", ["NAVY"])], null),
            TestContext.Current.CancellationToken);

        Assert.Equal(DefineVariantsOutcome.NotFound, result.Outcome);
    }

    /// <summary>
    /// The cartesian product grows fast, and pasting a long list by mistake
    /// should not write thousands of rows before anybody notices.
    /// </summary>
    [Fact]
    public void A_matrix_that_would_explode_is_rejected_by_the_validator()
    {
        var validator = new DefineVariantsValidator();
        var manySizes = Enumerable.Range(1, 30).Select(n => n.ToString()).ToArray();

        var result = validator.Validate(new DefineVariantsCommand(
            ProductId.New(),
            [new VariantAxis("COLOR", ["A", "B", "C", "D", "E", "F", "G"]), new VariantAxis("SIZE", manySizes)],
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("more than", StringComparison.Ordinal));
    }

    /// <summary>
    /// A product with no external id names its SKUs after its own id, and after
    /// the id's random end: the start of a version-7 Guid is a timestamp, so two
    /// products created within a minute began the same and their SKUs collided.
    /// </summary>
    [Fact]
    public async Task Two_products_created_together_get_different_skus_without_a_prefix()
    {
        Product Unlinked() => Product.Create(Clock, LocalizedText.From("es", "Camiseta técnica"), new Money(24.90m, "EUR"));
        var (first, second) = (Unlinked(), Unlinked());
        var command = (Product product) => new DefineVariantsCommand(product.Id, [new VariantAxis("SIZE", ["M"])], null);

        await HandlerOver(first, out _).HandleAsync(command(first), TestContext.Current.CancellationToken);
        await HandlerOver(second, out _).HandleAsync(command(second), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Variants.Single().Sku, second.Variants.Single().Sku);
    }

    private static Product AProduct()
    {
        var product = Product.Create(
            Clock, LocalizedText.From("es", "Camiseta técnica"), new Money(24.90m, "EUR"));
        product.LinkExternal(Clock, "seed", "SHIRT01");
        return product;
    }

    private static DefineVariantsHandler HandlerOver(Product product, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new DefineVariantsHandler(new InMemoryProductRepository([product]), unitOfWork, Clock);
    }
}
