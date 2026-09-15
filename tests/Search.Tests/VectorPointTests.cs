using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Qdrant;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What a product becomes in the vector store: which point, from which words.
///
/// Both are derived rather than stored, and a derivation that drifts is silent —
/// a point id that changed between two runs leaves the old point behind forever,
/// and a text that stopped including the category makes "cocina" stop reaching
/// the kitchen with every test that only counts points still green.
/// </summary>
public sealed class VectorPointTests
{
    private static readonly ProductId Product = new(Guid.Parse("0191d2a4-5c3e-7000-8000-000000000001"));

    [Fact]
    public void A_product_in_a_culture_is_always_the_same_point()
    {
        // Across calls AND across processes: nothing here may depend on a
        // per-process seed, or a restarted worker would write a second copy.
        Assert.Equal(VectorPointId.For(Product, "es"), VectorPointId.For(Product, "es"));
        Assert.Equal(Guid.Parse("7ab586cc-f77a-8c10-9497-443841277ec0"), VectorPointId.For(Product, "es"));
    }

    [Fact]
    public void Each_culture_is_its_own_point_and_no_two_products_share_one()
    {
        var other = new ProductId(Guid.Parse("0191d2a4-5c3e-7000-8000-000000000002"));

        Assert.NotEqual(VectorPointId.For(Product, "es"), VectorPointId.For(Product, "en"));
        Assert.NotEqual(VectorPointId.For(Product, "es"), VectorPointId.For(other, "es"));
    }

    [Fact]
    public void The_id_says_it_is_derived_rather_than_random()
    {
        var id = VectorPointId.For(Product, "en");

        Assert.Equal(8, id.Version);
        Assert.Equal(0b10, id.Variant >> 2);
    }

    [Fact]
    public void The_text_is_what_a_shopper_reads_most_identifying_first()
    {
        Assert.Equal(
            "Italian coffee maker. Kuisina. Home Kitchen Coffee. capacity 6 cups. Aluminium, for the hob",
            VectorText.For(ADocument()));
    }

    [Fact]
    public void What_a_product_does_not_have_leaves_no_empty_sentence()
    {
        var bare = ADocument() with { Brand = null, AttributesText = " ", Description = null };

        Assert.Equal("Italian coffee maker. Home Kitchen Coffee", VectorText.For(bare));
    }

    /// <summary>
    /// The fingerprint is what lets an unchanged product skip the model server,
    /// so both of its inputs have to move it: a new text and a new revision are
    /// each a reason to embed again, and nothing else is.
    /// </summary>
    [Fact]
    public void The_fingerprint_changes_with_the_text_and_with_the_model_and_nothing_else()
    {
        var model = EmbeddingModelDescriptor.BgeM3;
        var bumped = new EmbeddingModelDescriptor(model.ModelId, new string('a', 40), model.Dimensions);
        var text = VectorText.For(ADocument());

        Assert.Equal(VectorText.Fingerprint(text, model), VectorText.Fingerprint(text, model));
        Assert.NotEqual(VectorText.Fingerprint(text, model), VectorText.Fingerprint(text + ".", model));
        Assert.NotEqual(VectorText.Fingerprint(text, model), VectorText.Fingerprint(text, bumped));
    }

    [Fact]
    public void Aspires_connection_string_and_a_bare_url_both_name_the_endpoint()
    {
        var (endpoint, key) = QdrantConnection.Parse("Endpoint=http://localhost:56334;Key=tendero-dev");
        Assert.Equal((new Uri("http://localhost:56334"), "tendero-dev"), (endpoint, key));

        Assert.Equal((new Uri("http://qdrant:6334"), (string?)null), QdrantConnection.Parse("http://qdrant:6334"));
    }

    [Fact]
    public void A_connection_string_with_no_endpoint_is_refused_without_repeating_its_key()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => QdrantConnection.Parse("Key=do-not-print-me"));

        Assert.DoesNotContain("do-not-print-me", refusal.Message);
        Assert.Contains("Endpoint", refusal.Message);
    }

    private static ProductSearchDocument ADocument() => new()
    {
        Id = "variant",
        ProductId = Product.ToString(),
        Sku = "COFFEE-6",
        Culture = "en",
        Name = "Italian coffee maker",
        Brand = "Kuisina",
        CategoryPathText = "Home Kitchen Coffee",
        AttributesText = "capacity 6 cups",
        Description = "Aluminium, for the hob",
        Slug = "italian-coffee-maker",
        Code = "K7Q2M9",
        PriceCurrency = "EUR",
        Status = "active"
    };
}
