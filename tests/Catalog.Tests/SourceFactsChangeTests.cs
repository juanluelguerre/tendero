using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// What a source says about a product and its variants — a handle, a variant's
/// id there, its barcode, its tax class (phase 16) — is said again on every
/// import, and only a change is a change: the same value twice raises no
/// <c>ProductUpserted</c>, so re-importing an unchanged store does not set the
/// indexing worker rewriting every document (ADR 0012's reasoning). Found by
/// phase 16's Stryker close: every "did it change?" answer survived.
/// </summary>
public sealed class SourceFactsChangeTests
{
    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(29.90m, "EUR");

    private static Product AProduct()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera"), Price);
        product.AddVariant(Clock, "MOKA-OLV", Price);
        product.ClearDomainEvents();
        return product;
    }

    /// <summary>Whether saying a fact, then the same again, then another, each changed the product.</summary>
    private static bool[] Changes(Action<Product, string> say, string value, string other)
    {
        var product = AProduct();
        return [.. new[] { value, value, other }.Select(fact =>
        {
            product.ClearDomainEvents();
            say(product, fact);
            return product.DomainEvents.Any(change => change is ProductUpserted);
        })];
    }

    private static readonly bool[] OnlyChangesChange = [true, false, true];

    [Fact]
    public void A_tax_class_said_again_changes_nothing() =>
        Assert.Equal(OnlyChangesChange, Changes((product, value) => product.SetVariantTaxClass(Clock, "MOKA-OLV", value), "exempt", "reduced"));

    [Fact]
    public void A_barcode_said_again_changes_nothing() =>
        Assert.Equal(OnlyChangesChange, Changes((product, value) => product.SetVariantBarcode(Clock, "MOKA-OLV", value), "8400000000011", "8400000000035"));

    [Fact]
    public void A_variants_source_id_said_again_changes_nothing() =>
        Assert.Equal(OnlyChangesChange, Changes((product, value) => product.LinkVariantExternal(Clock, "MOKA-OLV", "shopify", value), "44001", "44002"));

    [Fact]
    public void A_products_handle_said_again_changes_nothing() =>
        Assert.Equal(OnlyChangesChange, Changes((product, value) => product.LinkExternal(Clock, "shopify", "8001", value), "moka", "moka-olive"));

    /// <summary>A blank tax class, source or source id is a caller's mistake, never a value.</summary>
    [Fact]
    public void A_blank_fact_is_refused()
    {
        var product = AProduct();

        Assert.Throws<ArgumentException>(() => product.SetVariantTaxClass(Clock, "MOKA-OLV", " "));
        Assert.Throws<ArgumentException>(() => product.LinkVariantExternal(Clock, "MOKA-OLV", " ", "44001"));
        Assert.Throws<ArgumentException>(() => product.LinkVariantExternal(Clock, "MOKA-OLV", "shopify", " "));
    }
}
