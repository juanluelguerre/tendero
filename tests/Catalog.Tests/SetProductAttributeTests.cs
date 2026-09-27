using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetProductAttribute;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// An approved claim reaches the catalogue through Catalog's own command (P10-6),
/// and Catalog decides whether the value is one of its own: the definition, not
/// the claim, has the last word (ADR 0018).
/// </summary>
public sealed class SetProductAttributeTests
{
    private static readonly TestClock Clock = new();

    private static readonly AttributeDefinitions Definitions = new(
    [
        Material(),
        AttributeDefinition.Define(Clock, "CAPACITY_L", LocalizedText.From("es", "capacidad").With("en", "capacity"), AttributeKind.Number, unit: "L"),
        AttributeDefinition.Define(Clock, "INDUCTION", LocalizedText.From("es", "inducción").With("en", "induction"), AttributeKind.Boolean),
        AttributeDefinition.Define(Clock, "LAPTOP_MAX_IN", LocalizedText.From("es", "portátil máximo").With("en", "laptop up to"), AttributeKind.Text),
        AttributeDefinition.Define(Clock, "CARE", LocalizedText.From("es", "cuidados").With("en", "care"), AttributeKind.LocalizedText),
    ]);

    [Theory]
    [InlineData("MATERIAL", "vidrio", "GLASS")]
    [InlineData("MATERIAL", "GLASS", "GLASS")]
    [InlineData("CAPACITY_L", "20 L", "20")]
    [InlineData("CAPACITY_L", "1.5", "1.5")]
    [InlineData("INDUCTION", "sí", "True")]
    [InlineData("INDUCTION", "no", "False")]
    [InlineData("LAPTOP_MAX_IN", "15\"", "15\"")]
    public void A_definition_accepts_what_it_can_hold(string code, string raw, string held)
    {
        var value = Definitions.ByCode(code)!.Accept(raw);

        Assert.NotNull(value);
        Assert.Equal(held, value.OptionCode ?? value.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? value.Flag?.ToString() ?? value.RawText);
    }

    /// <summary>
    /// Each refusal is one a claim could bring: an option the shop does not sell,
    /// a number with somebody else's unit — "20 kg" is not a capacity in litres —
    /// a yes/no that is neither, and one string for an attribute that promises two
    /// languages.
    /// </summary>
    [Theory]
    [InlineData("MATERIAL", "unobtainium")]
    [InlineData("CAPACITY_L", "20 kg")]
    [InlineData("CAPACITY_L", "1,5")]
    [InlineData("INDUCTION", "quizá")]
    [InlineData("LAPTOP_MAX_IN", "  ")]
    [InlineData("CARE", "lavar a mano")]
    public void A_definition_refuses_what_it_cannot_hold(string code, string raw) =>
        Assert.Null(Definitions.ByCode(code)!.Accept(raw));

    [Fact]
    public async Task An_accepted_value_is_set_and_the_product_saved()
    {
        var product = AProduct();
        var (handler, unitOfWork) = HandlerOver(product);

        var result = await handler.HandleAsync(Command(product, "material", "vidrio"), TestContext.Current.CancellationToken);

        Assert.Equal((SetAttributeOutcome.Set, "MATERIAL"), (result.Outcome, result.AttributeCode));
        Assert.Equal("GLASS", product.AttributeFor("MATERIAL")?.OptionCode);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Contains(product.DomainEvents, e => e is ProductUpserted);
    }

    [Fact]
    public async Task The_value_already_held_changes_nothing_and_reindexes_nothing()
    {
        var product = AProduct();
        product.SetAttribute(Clock, AttributeValue.Option("MATERIAL", "GLASS"));
        product.ClearDomainEvents();
        var (handler, unitOfWork) = HandlerOver(product);

        var result = await handler.HandleAsync(Command(product, "MATERIAL", "glass"), TestContext.Current.CancellationToken);

        Assert.Equal(SetAttributeOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    [Theory]
    [InlineData("COLOUR_OF_MOOD", "azul", SetAttributeOutcome.UnknownAttribute)]
    [InlineData("MATERIAL", "unobtainium", SetAttributeOutcome.ValueRejected)]
    public async Task What_the_catalogue_does_not_define_is_refused_and_nothing_is_saved(string code, string value, SetAttributeOutcome expected)
    {
        var product = AProduct();
        var (handler, unitOfWork) = HandlerOver(product);

        var result = await handler.HandleAsync(Command(product, code, value), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(AuditOutcome.Denied, ((IAuditedResult)result).AuditOutcome);
        Assert.Null(product.AttributeFor(code));
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_unknown_product_is_refused()
    {
        var (handler, _) = HandlerOver();

        var result = await handler.HandleAsync(
            new SetProductAttributeCommand(ProductId.New(), "MATERIAL", "vidrio", "claim approved by Ana"), TestContext.Current.CancellationToken);

        Assert.Equal(SetAttributeOutcome.ProductNotFound, result.Outcome);
    }

    private static SetProductAttributeCommand Command(Product product, string code, string value) =>
        new(product.Id, code, value, "claim approved by Ana");

    private static (SetProductAttributeHandler, CountingUnitOfWork) HandlerOver(params Product[] products)
    {
        var unitOfWork = new CountingUnitOfWork();
        return (new SetProductAttributeHandler(new InMemoryProductRepository(products), new FixedDefinitions(), unitOfWork, Clock), unitOfWork);
    }

    private static Product AProduct() =>
        Product.Create(Clock, LocalizedText.From("es", "Cafetera de goteo"), new Money(49.90m, "EUR"));

    private static AttributeDefinition Material()
    {
        var material = AttributeDefinition.Define(Clock, "MATERIAL", LocalizedText.From("es", "material").With("en", "material"), AttributeKind.Option);
        material.AddOption(Clock, "GLASS", LocalizedText.From("es", "vidrio").With("en", "glass"));
        return material;
    }

    private sealed class FixedDefinitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Definitions);
    }
}
