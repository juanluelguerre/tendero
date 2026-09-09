using System.Text.Json;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Ucp.Catalog;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// Tendero's product said in UCP's words, and the one number that is easy to get
/// plausibly wrong.
/// </summary>
public sealed class UcpCatalogMapperTests
{
    private const string BaseUrl = "https://shop.example";

    private static ProductDetail Sleeve => new(
        ProductId: "0199a1f0-0000-7000-8000-000000000001",
        Code: "B13BAG0710",
        Name: "Funda para portatil de 14 pulgadas",
        Slug: "funda-para-portatil-de-14-pulgadas",
        Description: "Funda acolchada de fieltro de lana.",
        Brand: "Nordwind",
        Category: [new CategoryStep("BAGS", "Bolsas"), new CategoryStep("BAGS_TOTE", "Bolsas y mochilas")],
        Images: [new ProductImageView("abc123", "Funda negra", 0)],
        Attributes: [],
        Axes: [new VariantAxisView("COLOR", "color",
            [new VariantOptionView("BLACK", "negro"), new VariantOptionView("GREY", "gris")])],
        Variants:
        [
            new VariantView(
                VariantId: "0199a1f0-0000-7000-8000-000000000002",
                Sku: "B13BAG0710-BLACK",
                PriceAmount: 39.00m,
                PriceCurrency: "EUR",
                AxisValues: new Dictionary<string, string> { ["COLOR"] = "BLACK" },
                Label: "negro",
                ImageId: "abc123",
                Discontinued: false,
                InStock: true,
                Remaining: 4),
            new VariantView(
                VariantId: "0199a1f0-0000-7000-8000-000000000003",
                Sku: "B13BAG0710-GREY",
                PriceAmount: 41.50m,
                PriceCurrency: "EUR",
                AxisValues: new Dictionary<string, string> { ["COLOR"] = "GREY" },
                Label: "gris",
                ImageId: null,
                Discontinued: false,
                InStock: false,
                Remaining: 0)
        ],
        PriceFrom: 39.00m,
        PriceTo: 41.50m,
        PriceCurrency: "EUR",
        Alternates: [],
        MissingCultures: [],
        Status: "Active",
        UpdatedAt: DateTimeOffset.UnixEpoch);

    /// <summary>
    /// **Minor units.** 39,00 € is 3900, and this is the single easiest thing in
    /// the whole binding to get wrong, because a wrong answer looks like a
    /// plausible price — an agent told 39 would buy something for thirty-nine
    /// cents and never notice until the money moved.
    /// </summary>
    [Theory]
    [InlineData("39.00", 3900L)]
    [InlineData("41.50", 4150L)]
    [InlineData("0.99", 99L)]
    [InlineData("1249.95", 124995L)]
    public void Money_is_in_minor_units(string amount, long expected) =>
        Assert.Equal(expected, UcpCatalogMapper.Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "EUR").Amount);

    [Fact]
    public void The_price_range_spans_the_variants()
    {
        var product = UcpCatalogMapper.From(Sleeve, BaseUrl);

        Assert.Equal(3900L, product.PriceRange.Min.Amount);
        Assert.Equal(4150L, product.PriceRange.Max.Amount);
        Assert.Equal("EUR", product.PriceRange.Min.Currency);
    }

    /// <summary>
    /// The identifier is the public CODE, not the internal GUID. It is what an
    /// agent can hold on to between calls, which is the whole argument of ADR
    /// 0026, and it is what `lookup_catalog` takes back.
    /// </summary>
    [Fact]
    public void The_identifier_is_the_one_an_agent_can_hold_on_to()
    {
        var product = UcpCatalogMapper.From(Sleeve, BaseUrl);

        Assert.Equal("B13BAG0710", product.Id);
        Assert.Equal("funda-para-portatil-de-14-pulgadas", product.Handle);
    }

    /// <summary>
    /// Availability is per VARIANT, which is only possible because the variant is
    /// the indexed unit (ADR 0015). A product-level flag would tell an agent the
    /// sleeve is available and let it buy the colour that is not.
    /// </summary>
    [Fact]
    public void Availability_is_per_variant_and_so_is_the_sku()
    {
        var product = UcpCatalogMapper.From(Sleeve, BaseUrl);

        Assert.Equal(["B13BAG0710-BLACK", "B13BAG0710-GREY"], product.Variants.Select(v => v.Id));
        Assert.True(product.Variants[0].Availability!.InStock);
        Assert.False(product.Variants[1].Availability!.InStock);
    }

    /// <summary>
    /// Options come back as words, not codes. Sending an agent a bare
    /// <c>COLOR</c>/<c>BLACK</c> is the same defect the localized taxonomy fixed
    /// for search in phase 2 — a code nobody types, and now a code nobody reads.
    /// </summary>
    [Fact]
    public void Options_are_labelled_in_the_answered_culture()
    {
        var product = UcpCatalogMapper.From(Sleeve, BaseUrl);

        var option = Assert.Single(product.Options!);
        Assert.Equal("color", option.Name);
        Assert.Equal(["negro", "gris"], option.Values.Select(value => value.Label));

        var selected = Assert.Single(product.Variants[0].Options!);
        Assert.Equal("color", selected.Name);
        Assert.Equal("negro", selected.Label);
    }

    [Fact]
    public void Media_urls_are_absolute()
    {
        var product = UcpCatalogMapper.From(Sleeve, BaseUrl);

        Assert.Equal("https://shop.example/api/images/abc123", product.Media![0].Url);
        Assert.Equal("Funda negra", product.Media[0].Alt);
    }

    /// <summary>
    /// And the wire, with the naming policy the rest of this API uses switched
    /// on: <c>price_range</c> is snake_case and would come out as
    /// <c>priceRange</c> if the attributes were not there. Here the naming is
    /// load-bearing rather than incidental, which it was not in the profile.
    /// </summary>
    [Fact]
    public void It_serialises_with_the_specifications_field_names()
    {
        var json = JsonSerializer.Serialize(
            UcpCatalogMapper.From(Sleeve, BaseUrl),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"price_range\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("priceRange", json, StringComparison.Ordinal);
        Assert.Contains("\"in_stock\":", json, StringComparison.Ordinal);
        Assert.Contains("\"amount\":3900", json, StringComparison.Ordinal);
    }
}
