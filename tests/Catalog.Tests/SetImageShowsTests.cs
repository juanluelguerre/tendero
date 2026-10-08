using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetImageShows;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Says which of a product's variants a picture shows (ADR 0057): what a
/// shopkeeper chooses beside the picture. The rule of what a picture may show
/// is the aggregate's; the slice finds the product, tells a refusal from a
/// change, and saves only a change.
/// </summary>
public sealed class SetImageShowsTests
{
    private static readonly TestClock Clock = new();
    private static readonly ImageId Cover = new("cover");
    private static readonly ImageId Side = new("side");

    private static Dictionary<string, string> Colour(string value) => new() { ["COLOR"] = value };

    [Fact]
    public async Task A_picture_is_told_the_colour_it_shows_and_the_others_are_left_alone()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(new SetImageShowsCommand(product.Id, Side, Colour("black")), TestContext.Current.CancellationToken);

        Assert.Equal(ImageShowsOutcome.Shown, result.Outcome);
        // As the variant spells it, whatever the case it arrived in.
        Assert.Equal([new AxisValue("COLOR", "BLACK")], product.Images.Single(image => image.Id == Side).Shows);
        Assert.Empty(product.Images.Single(image => image.Id == Cover).Shows);
        Assert.Equal(1, unitOfWork.SaveCount);
        // A search result shows a variant's picture: the product is told again.
        Assert.Contains(product.DomainEvents, raised => raised is ProductUpserted);
    }

    /// <summary>Saying nothing is how a tag is taken off: the picture is every variant's again.</summary>
    [Fact]
    public async Task Showing_nothing_takes_the_tag_off()
    {
        var product = AProduct();
        product.Show(Clock, Side, Colour("BLACK"));
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageShowsCommand(product.Id, Side, new Dictionary<string, string>()), TestContext.Current.CancellationToken);

        Assert.Equal(ImageShowsOutcome.Shown, result.Outcome);
        Assert.Empty(product.Images.Single(image => image.Id == Side).Shows);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>
    /// Whoever corrects one axis knows only that one — a doubt about a picture's
    /// colour, decided — and what the picture shows on the others stays. A
    /// shopkeeper says everything at once, and what they leave out is taken off.
    /// </summary>
    [Fact]
    public async Task One_axis_corrected_keeps_the_others_when_asked_to_and_replaces_them_when_not()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Mochila"), new Money(49m, "EUR"));
        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        product.AddVariant(Clock, "PACK-BLACK-L", new Money(49m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "L" });
        product.AddVariant(Clock, "PACK-GREY-L", new Money(49m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "GREY", ["SIZE"] = "L" });
        product.AddImage(Clock, Side);
        product.Show(Clock, Side, new Dictionary<string, string> { ["COLOR"] = "GREY", ["SIZE"] = "L" });
        var handler = HandlerOver(product, out _);
        var ct = TestContext.Current.CancellationToken;

        await handler.HandleAsync(new SetImageShowsCommand(product.Id, Side, new Dictionary<string, string> { ["color"] = "BLACK" }, KeepOtherAxes: true), ct);
        var kept = product.Images.Single(image => image.Id == Side).Shows;
        await handler.HandleAsync(new SetImageShowsCommand(product.Id, Side, Colour("GREY")), ct);

        Assert.Equal([new AxisValue("COLOR", "BLACK"), new AxisValue("SIZE", "L")], kept);
        Assert.Equal([new AxisValue("COLOR", "GREY")], product.Images.Single(image => image.Id == Side).Shows);
    }

    /// <summary>What it already says is one change: no save, no event, no reindex — for a tag and for none.</summary>
    [Theory]
    [InlineData("BLACK")]
    [InlineData(null)]
    public async Task What_it_already_says_is_not_a_change(string? colour)
    {
        var product = AProduct();
        var shows = colour is null ? new Dictionary<string, string>() : Colour(colour);
        product.Show(Clock, Side, shows);
        product.ClearDomainEvents();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(new SetImageShowsCommand(product.Id, Side, shows), TestContext.Current.CancellationToken);

        Assert.Equal(ImageShowsOutcome.Unchanged, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(product.DomainEvents);
    }

    /// <summary>
    /// A colour no variant is sold in, or an axis the product does not have,
    /// would be a picture that shows on nothing: refused in the aggregate's own
    /// words, and nothing saved.
    /// </summary>
    [Theory]
    [InlineData("COLOR", "OLIVE")]
    [InlineData("MATERIAL", "LEATHER")]
    public async Task What_no_variant_is_sold_in_is_refused_with_the_reason_and_nothing_is_saved(string axis, string value)
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageShowsCommand(product.Id, Side, new Dictionary<string, string> { [axis] = value }), TestContext.Current.CancellationToken);

        Assert.Equal(ImageShowsOutcome.NotSold, result.Outcome);
        Assert.Contains(axis, result.Refusal, StringComparison.Ordinal);
        Assert.Empty(product.Images.Single(image => image.Id == Side).Shows);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_picture_the_product_does_not_have_is_refused_and_nothing_is_saved()
    {
        var product = AProduct();
        var handler = HandlerOver(product, out var unitOfWork);

        var result = await handler.HandleAsync(
            new SetImageShowsCommand(product.Id, new ImageId("another-products"), Colour("BLACK")), TestContext.Current.CancellationToken);

        Assert.Equal(ImageShowsOutcome.NoSuchImage, result.Outcome);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_archived_product_is_refused_and_a_missing_one_is_not_found()
    {
        var archived = AProduct();
        archived.Archive(Clock);
        var handler = HandlerOver(archived, out var unitOfWork);

        var refused = await handler.HandleAsync(new SetImageShowsCommand(archived.Id, Side, Colour("BLACK")), TestContext.Current.CancellationToken);
        var missing = await handler.HandleAsync(
            new SetImageShowsCommand(new ProductId(Guid.NewGuid()), Side, Colour("BLACK")), TestContext.Current.CancellationToken);

        Assert.Equal((ImageShowsOutcome.Archived, ImageShowsOutcome.NotFound), (refused.Outcome, missing.Outcome));
        Assert.Empty(archived.Images.Single(image => image.Id == Side).Shows);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>A refusal is told to the audit log as one; a tag, changed or not, is allowed.</summary>
    [Theory]
    [InlineData(ImageShowsOutcome.Shown, AuditOutcome.Allowed, null)]
    [InlineData(ImageShowsOutcome.Unchanged, AuditOutcome.Allowed, null)]
    [InlineData(ImageShowsOutcome.NoSuchImage, AuditOutcome.Denied, "NoSuchImage")]
    [InlineData(ImageShowsOutcome.NotSold, AuditOutcome.Denied, "NotSold")]
    [InlineData(ImageShowsOutcome.Archived, AuditOutcome.Denied, "Archived")]
    [InlineData(ImageShowsOutcome.NotFound, AuditOutcome.Denied, "NotFound")]
    public void The_audit_log_is_told_whether_it_was_a_refusal(ImageShowsOutcome outcome, AuditOutcome audited, string? reason)
    {
        IAuditedResult result = new SetImageShowsResult(outcome, new ProductId(Guid.NewGuid()));

        Assert.Equal((audited, reason), (result.AuditOutcome, result.AuditReason));
    }

    [Fact]
    public void The_validator_wants_a_product_a_picture_and_whole_pairs_and_takes_nothing_for_an_answer()
    {
        var validator = new SetImageShowsValidator();
        var product = new ProductId(Guid.NewGuid());

        Assert.True(validator.Validate(new SetImageShowsCommand(product, Side, Colour("BLACK"))).IsValid);
        Assert.True(validator.Validate(new SetImageShowsCommand(product, Side, new Dictionary<string, string>())).IsValid);
        Assert.False(validator.Validate(new SetImageShowsCommand(new ProductId(Guid.Empty), Side, Colour("BLACK"))).IsValid);
        Assert.False(validator.Validate(new SetImageShowsCommand(product, new ImageId(" "), Colour("BLACK"))).IsValid);
        Assert.False(validator.Validate(new SetImageShowsCommand(product, Side, Colour(" "))).IsValid);
        Assert.False(validator.Validate(new SetImageShowsCommand(product, Side, new Dictionary<string, string> { [" "] = "BLACK" })).IsValid);
        Assert.False(validator.Validate(new SetImageShowsCommand(product, Side, null!)).IsValid);
    }

    private static SetImageShowsHandler HandlerOver(Product product, out CountingUnitOfWork unitOfWork)
    {
        unitOfWork = new CountingUnitOfWork();
        return new SetImageShowsHandler(new InMemoryProductRepository(product), unitOfWork, Clock);
    }

    /// <summary>A pack in black and grey, with a cover and a side view, neither saying a colour.</summary>
    private static Product AProduct()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Mochila"), new Money(49m, "EUR"));
        product.DefineAxes(Clock, ["COLOR"]);
        product.AddVariant(Clock, "PACK-BLACK", new Money(49m, "EUR"), Colour("BLACK"));
        product.AddVariant(Clock, "PACK-GREY", new Money(49m, "EUR"), Colour("GREY"));
        product.AddImage(Clock, Cover);
        product.AddImage(Clock, Side);
        product.ClearDomainEvents();
        return product;
    }
}
