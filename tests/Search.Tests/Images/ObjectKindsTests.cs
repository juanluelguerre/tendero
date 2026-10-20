using System.Text.Json;
using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

public sealed class ObjectKindsTests
{
    [Fact]
    public void The_shop_looks_for_its_categories_and_the_objects_it_does_not_sell()
    {
        var kinds = ObjectKinds.Shop;

        Assert.Equal(24, kinds.All.Count(kind => kind.Sold));
        Assert.Equal(13, kinds.All.Count(kind => !kind.Sold));
        Assert.Equal(kinds.All.Count, kinds.All.Select(kind => kind.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(new ObjectKind("DECOR", "vase or mirror or picture frame", Sold: true), kinds.All);
    }

    /// <summary>A sold kind the catalogue has no category for would be a block nobody can fill.</summary>
    [Fact]
    public void Every_kind_sold_is_a_category_of_the_shop()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root!.FullName, "Tendero.slnx")))
            root = root.Parent;
        using var seed = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "seed", "categories.sample.json")));
        var categories = seed.RootElement.EnumerateArray().Select(category => category.GetProperty("code").GetString()).ToHashSet(StringComparer.Ordinal);

        Assert.All(ObjectKinds.Shop.All.Where(kind => kind.Sold), kind => Assert.Contains(kind.Code, categories));
        Assert.All(ObjectKinds.Shop.All.Where(kind => !kind.Sold), kind => Assert.DoesNotContain(kind.Code, categories));
    }

    /// <summary>What the line under the results says the shop does not sell, in the shopper's language (CS-1.6).</summary>
    [Fact]
    public void A_kind_not_sold_is_named_in_both_languages()
    {
        Assert.All(ObjectKinds.Shop.All.Where(kind => !kind.Sold), kind =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ObjectKinds.Shop.NameOf(kind.Code, "es")));
            Assert.False(string.IsNullOrWhiteSpace(ObjectKinds.Shop.NameOf(kind.Code, "en")));
        });
        Assert.Equal("botella", ObjectKinds.Shop.NameOf("bottle", "es"));
        Assert.Equal("screen", ObjectKinds.Shop.NameOf("television", "en"));
        Assert.Null(ObjectKinds.Shop.NameOf("SHIRT", "es"));
    }

    [Fact]
    public void A_file_with_a_kind_not_sold_and_no_name_is_refused()
    {
        var refused = Assert.Throws<InvalidDataException>(() => ObjectKinds.Parse("""
            { "sold": {}, "notSold": { "bottle": { "words": "bottle", "en": "bottle" } } }
            """));

        Assert.Contains("bottle", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The file says which kinds are sold by where it lists them: that is what draws a block or the line (CS-1.6).</summary>
    [Fact]
    public void A_kind_is_sold_or_not_by_the_list_it_is_in()
    {
        var kinds = ObjectKinds.Parse("""
            { "sold": { "SHIRT": "t-shirt" }, "notSold": { "bottle": { "words": "bottle", "es": "botella", "en": "bottle" } } }
            """);

        Assert.Equal([new ObjectKind("SHIRT", "t-shirt", Sold: true), new ObjectKind("bottle", "bottle", Sold: false)], kinds.All);
    }

    [Theory]
    [InlineData("""{ "words": "bottle", "es": "botella" }""")]
    [InlineData("""{ "words": "bottle", "es": "botella", "en": " " }""")]
    [InlineData("""{ "words": "bottle", "es": "", "en": "bottle" }""")]
    public void A_kind_not_sold_missing_either_name_is_refused(string kind)
    {
        Assert.Throws<InvalidDataException>(() => ObjectKinds.Parse($$"""{ "sold": {}, "notSold": { "bottle": {{kind}} } }"""));
    }
}
