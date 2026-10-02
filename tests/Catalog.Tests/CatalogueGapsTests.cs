using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.CatalogueGaps;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// What the catalogue lacks, one rule per kind (S-3a): Steward's
/// <c>catalogue_gaps</c> and a screen's question alike. Fifty rows at most, always
/// with the total.
/// </summary>
public sealed class CatalogueGapsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void A_name_with_no_english_is_a_gap_in_english_and_not_in_spanish()
    {
        var product = Spanish("Flexo articulado");

        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingName, "en"));
        Assert.False(CatalogueGapRules.Has(product, CatalogueGap.MissingName, "es"));
    }

    [Fact]
    public void A_product_with_no_picture_is_missing_an_image()
    {
        var product = Spanish("Flexo");
        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingImage, null));

        product.AddImage(Clock, new ImageId("flexo-1"));
        Assert.False(CatalogueGapRules.Has(product, CatalogueGap.MissingImage, null));
    }

    /// <summary>An alt text with no English is a gap; a product with no picture at
    /// all is the other gap, not this one.</summary>
    [Fact]
    public void A_picture_described_only_in_spanish_is_missing_its_english_alt()
    {
        var bare = Spanish("Flexo");
        var described = Spanish("Mochila");
        described.AddImage(Clock, new ImageId("mochila-1"), LocalizedText.From("es", "Mochila gris"));

        Assert.False(CatalogueGapRules.Has(bare, CatalogueGap.MissingImageAlt, "en"));
        Assert.True(CatalogueGapRules.Has(described, CatalogueGap.MissingImageAlt, "en"));
        Assert.False(CatalogueGapRules.Has(described, CatalogueGap.MissingImageAlt, "es"));
    }

    /// <summary>A picture with no alternative text at all lacks it in every language.</summary>
    [Fact]
    public void A_picture_with_no_alt_at_all_is_missing_it()
    {
        var product = Spanish("Flexo");
        product.AddImage(Clock, new ImageId("flexo-1"));

        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingImageAlt, "es"));
    }

    [Fact]
    public void A_draft_is_waiting_and_an_active_product_is_not()
    {
        var draft = Spanish("Flexo");
        var active = Spanish("Mochila");
        active.Publish(Clock);

        Assert.True(CatalogueGapRules.Has(draft, CatalogueGap.DraftWaiting, null));
        Assert.False(CatalogueGapRules.Has(active, CatalogueGap.DraftWaiting, null));
    }

    [Fact]
    public async Task Sixty_gaps_are_fifty_rows_and_a_total_of_sixty_in_code_order()
    {
        var products = Enumerable.Range(0, 60).Select(index => Spanish($"Producto {index}")).ToArray();

        var gaps = await new CatalogueGapsHandler(new InMemoryProductCatalogReader(products))
            .HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingName, "en"), TestContext.Current.CancellationToken);

        Assert.Equal((60, ReadLimits.MaxRows), (gaps.Total, gaps.Rows.Count));
        Assert.Equal(gaps.Rows.Select(row => row.Code).Order(StringComparer.Ordinal), gaps.Rows.Select(row => row.Code));
        Assert.StartsWith("Producto", gaps.Rows[0].Name);
    }

    [Fact]
    public async Task An_archived_product_is_no_gap()
    {
        var archived = Spanish("Flexo");
        archived.Archive(Clock);

        var gaps = await new CatalogueGapsHandler(new InMemoryProductCatalogReader(archived))
            .HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingName, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(0, gaps.Total);
    }

    [Theory]
    [InlineData(CatalogueGap.MissingName, null, 50, false)]
    [InlineData(CatalogueGap.MissingName, "fr", 50, false)]
    [InlineData(CatalogueGap.MissingName, "en", 50, true)]
    [InlineData(CatalogueGap.MissingImageAlt, null, 50, false)]
    [InlineData(CatalogueGap.MissingImage, null, 50, true)]
    [InlineData(CatalogueGap.DraftWaiting, null, 50, true)]
    [InlineData(CatalogueGap.DraftWaiting, "en", 50, false)]
    [InlineData(CatalogueGap.DraftWaiting, null, 51, false)]
    [InlineData(CatalogueGap.DraftWaiting, null, 0, false)]
    public void A_gap_by_culture_needs_one_the_shop_speaks_and_the_others_take_none(
        CatalogueGap kind, string? culture, int limit, bool valid) =>
        Assert.Equal(valid, new CatalogueGapsValidator().Validate(new CatalogueGapsQuery(kind, culture, limit)).IsValid);

    private static Product Spanish(string name) =>
        Product.Create(Clock, LocalizedText.From("es", name), new Money(10m, "EUR"));
}
