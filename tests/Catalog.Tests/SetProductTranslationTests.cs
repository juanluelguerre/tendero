using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Writes one culture's text and leaves the others alone — the command Steward
/// proposes for a product with no English name (phase 12).
/// </summary>
public sealed class SetProductTranslationTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Adds_the_english_name_and_keeps_the_spanish_one()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Italian moka pot, 12 cups", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Translated, result.Outcome);
        Assert.Equal("Italian moka pot, 12 cups", product.Name.Values["en"]);
        Assert.Equal("Cafetera italiana 12 tazas", product.Name.Values["es"]);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Writes_the_description_in_that_culture_only_when_one_is_given()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out _);

        await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Moka pot", "Aluminium, for gas and electric hobs."), TestContext.Current.CancellationToken);

        Assert.Equal("Aluminium, for gas and electric hobs.", product.Description!.Values["en"]);
        Assert.Equal("Aluminio, para gas y vitrocerámica.", product.Description.Values["es"]);
    }

    /// <summary>"The same text twice is one change": no save, no event, so the
    /// index is not rewritten for nothing.</summary>
    [Fact]
    public async Task The_same_text_twice_is_one_change()
    {
        var product = AProduct();
        product.Localize(Clock, "en", "Moka pot");
        product.ClearDomainEvents();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "EN", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task An_archived_product_is_refused()
    {
        var product = AProduct();
        product.Archive(Clock);
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(product.Id, "en", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.Archived, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_missing_product_is_not_found()
    {
        var handler = HandlerOver(AProduct(), out _);

        var result = await handler.HandleAsync(
            new SetProductTranslationCommand(new ProductId(Guid.NewGuid()), "en", "Moka pot", null), TestContext.Current.CancellationToken);

        Assert.Equal(TranslationOutcome.NotFound, result.Outcome);
    }

    [Theory]
    [InlineData("fr", "Moka pot", "culture")]
    [InlineData("en", "   ", "name")]
    [InlineData("en", "", "name")]
    public void The_validator_refuses_an_unsupported_culture_and_a_blank_name(string culture, string name, string field)
    {
        var errors = new SetProductTranslationValidator()
            .Validate(new SetProductTranslationCommand(new ProductId(Guid.NewGuid()), culture, name, null)).Errors;

        Assert.Contains(errors, error => error.PropertyName.Contains(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_validator_holds_the_catalogues_lengths()
    {
        var errors = new SetProductTranslationValidator().Validate(new SetProductTranslationCommand(
            new ProductId(Guid.NewGuid()), "en", new string('a', Product.NameMaxLength + 1), new string('b', Product.DescriptionMaxLength + 1))).Errors;

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void A_blank_description_is_refused_and_a_missing_one_is_not()
    {
        var validator = new SetProductTranslationValidator();
        var id = new ProductId(Guid.NewGuid());

        Assert.False(validator.Validate(new SetProductTranslationCommand(id, "en", "Moka pot", "  ")).IsValid);
        Assert.True(validator.Validate(new SetProductTranslationCommand(id, "en", "Moka pot", null)).IsValid);
    }

    private static SetProductTranslationHandler HandlerOver(Product product, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new SetProductTranslationHandler(new InMemoryProductRepository(product), unitOfWork, Clock);
    }

    private static Product AProduct() => Product.Create(
        Clock,
        LocalizedText.From("es", "Cafetera italiana 12 tazas"),
        new Money(29.90m, "EUR"),
        LocalizedText.From("es", "Aluminio, para gas y vitrocerámica."));
}
