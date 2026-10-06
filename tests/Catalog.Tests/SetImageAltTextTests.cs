using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetImageAltText;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Says what one of a product's pictures shows, in both of the shop's languages
/// (phase 13a, I-3). It is what a shopkeeper types on the product's screen, and
/// what the alt-text job proposes for a person to approve: the same command.
/// </summary>
public sealed class SetImageAltTextTests
{
    private static readonly TestClock Clock = new();
    private static readonly ImageId Cover = new("cover");
    private static readonly ImageId Side = new("side");

    [Fact]
    public async Task Describes_one_picture_in_both_languages_and_leaves_the_others_alone()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageAltTextCommand(product.Id, Side, "Cafetera vista de lado.", "Moka pot seen from the side."),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageAltOutcome.Described, result.Outcome);
        var side = product.Images.Single(image => image.Id == Side);
        Assert.Equal(("Cafetera vista de lado.", "Moka pot seen from the side."), (side.Alt!.Values["es"], side.Alt.Values["en"]));
        // Its place in the gallery is its own, and the cover was not touched.
        Assert.Equal(1, side.SortOrder);
        Assert.Null(product.Images.Single(image => image.Id == Cover).Alt);
        Assert.Equal(1, unitOfWork.SaveCount);
        // The storefront serves alt text from the index's source: the product is told again.
        Assert.Contains(product.DomainEvents, raised => raised is ProductUpserted);
    }

    /// <summary>The same words twice is one change: no save, no event, no reindex.</summary>
    [Fact]
    public async Task The_same_words_twice_is_one_change()
    {
        var product = AProduct();
        product.DescribeImage(Clock, Cover, LocalizedText.Both("A moka pot.", "Una cafetera."));
        product.ClearDomainEvents();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageAltTextCommand(product.Id, Cover, "Una cafetera.", "A moka pot."), TestContext.Current.CancellationToken);

        Assert.Equal(ImageAltOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task A_picture_the_product_does_not_have_is_refused_and_nothing_is_saved()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageAltTextCommand(product.Id, new ImageId("another-products"), "Una cafetera.", "A moka pot."),
            TestContext.Current.CancellationToken);

        Assert.Equal(ImageAltOutcome.NoSuchImage, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_archived_product_is_refused_and_a_missing_one_is_not_found()
    {
        var archived = AProduct();
        archived.Archive(Clock);
        var handler = HandlerOver(archived, out var unitOfWork);

        var refused = await handler.HandleAsync(
            new SetImageAltTextCommand(archived.Id, Cover, "Una cafetera.", "A moka pot."), TestContext.Current.CancellationToken);
        var missing = await handler.HandleAsync(
            new SetImageAltTextCommand(new ProductId(Guid.NewGuid()), Cover, "Una cafetera.", "A moka pot."), TestContext.Current.CancellationToken);

        Assert.Equal((ImageAltOutcome.Archived, ImageAltOutcome.NotFound), (refused.Outcome, missing.Outcome));
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>A refusal is told to the audit log as one; a description, changed or not, is allowed.</summary>
    [Theory]
    [InlineData(ImageAltOutcome.Described, AuditOutcome.Allowed, null)]
    [InlineData(ImageAltOutcome.Unchanged, AuditOutcome.Allowed, null)]
    [InlineData(ImageAltOutcome.NoSuchImage, AuditOutcome.Denied, "NoSuchImage")]
    [InlineData(ImageAltOutcome.Archived, AuditOutcome.Denied, "Archived")]
    [InlineData(ImageAltOutcome.NotFound, AuditOutcome.Denied, "NotFound")]
    public void The_audit_log_is_told_whether_it_was_a_refusal(ImageAltOutcome outcome, AuditOutcome audited, string? reason)
    {
        IAuditedResult result = new SetImageAltTextResult(outcome, new ProductId(Guid.NewGuid()));

        Assert.Equal((audited, reason), (result.AuditOutcome, result.AuditReason));
    }

    /// <summary>Both languages or neither is described: a picture said in one is a picture half the shop cannot read.</summary>
    [Theory]
    [InlineData("", "A moka pot.", "Spanish")]
    [InlineData("Una cafetera.", "   ", "English")]
    public void The_validator_wants_both_languages(string spanish, string english, string field)
    {
        var errors = new SetImageAltTextValidator()
            .Validate(new SetImageAltTextCommand(new ProductId(Guid.NewGuid()), Cover, spanish, english)).Errors;

        Assert.Contains(errors, error => error.PropertyName.Contains(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_validator_holds_the_length_and_wants_a_product_and_a_picture()
    {
        var validator = new SetImageAltTextValidator();
        var tooLong = new string('a', Product.ImageAltMaxLength + 1);

        Assert.Equal(2, validator.Validate(new SetImageAltTextCommand(new ProductId(Guid.NewGuid()), Cover, tooLong, tooLong)).Errors.Count);
        Assert.False(validator.Validate(new SetImageAltTextCommand(new ProductId(Guid.Empty), Cover, "Una cafetera.", "A moka pot.")).IsValid);
        Assert.False(validator.Validate(new SetImageAltTextCommand(new ProductId(Guid.NewGuid()), new ImageId(" "), "Una cafetera.", "A moka pot.")).IsValid);
        Assert.True(validator.Validate(new SetImageAltTextCommand(new ProductId(Guid.NewGuid()), Cover,
            new string('a', Product.ImageAltMaxLength), "A moka pot.")).IsValid);
    }

    // ---------- the import does not undo it ----------

    /// <summary>
    /// The seed carries no alt text, and an import rebuilds the gallery from the
    /// source: before this, importing again erased what a person had approved.
    /// What the source does not say, the product keeps.
    /// </summary>
    [Fact]
    public void An_import_that_carries_no_alt_text_keeps_the_one_the_product_has()
    {
        var product = AProduct();
        product.DescribeImage(Clock, Cover, LocalizedText.Both("A moka pot.", "Una cafetera."));
        product.ClearDomainEvents();

        product.ReplaceImages(Clock, [(Cover, null), (Side, null)]);

        Assert.Equal("Una cafetera.", product.Images.Single(image => image.Id == Cover).Alt!.Values["es"]);
        // The same gallery again is still not a change.
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void An_import_that_does_carry_alt_text_is_the_sources_word()
    {
        var product = AProduct();
        product.DescribeImage(Clock, Cover, LocalizedText.Both("A moka pot.", "Una cafetera."));

        product.ReplaceImages(Clock, [(Cover, LocalizedText.From("es", "Cafetera italiana de aluminio.")), (Side, null)]);

        Assert.Equal("Cafetera italiana de aluminio.", product.Images.Single(image => image.Id == Cover).Alt!.Values["es"]);
    }

    private static SetImageAltTextHandler HandlerOver(Product product, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new SetImageAltTextHandler(new InMemoryProductRepository(product), unitOfWork, Clock);
    }

    private static Product AProduct()
    {
        var product = Product.Create(
            Clock,
            LocalizedText.From("es", "Cafetera italiana 12 tazas"),
            new Money(29.90m, "EUR"),
            LocalizedText.From("es", "Aluminio, para gas y vitrocerámica."));
        product.AddImage(Clock, Cover);
        product.AddImage(Clock, Side);
        product.ClearDomainEvents();
        return product;
    }
}
