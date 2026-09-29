using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Similarity;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// *Puede que te guste*'s rule (R-8), with no index and no model: neighbours in
/// the viewed product's category, ranked by the attributes they share, never one
/// already viewed nor one sold out, re-sorted only where the vector layer has
/// evidence, and spread across what was viewed.
/// </summary>
public sealed class SimilarProductsTests
{
    internal static ProductProfile Profile(string code, string category, bool inStock = true, params string[] traits) =>
        new(Strategies.Hit($"id-{code}", inStock) with { Code = code }, category, traits);

    [Fact]
    public void Sharing_is_counted_by_attribute_not_by_value_so_sizes_do_not_outvote_the_material()
    {
        var viewed = Profile("BOOTS", "SHOES", true, "MATERIAL:LEATHER", "SIZE:38", "SIZE:39", "SIZE:40", "SIZE:41");
        var sameSizes = Profile("RUNNER", "SHOES", true, "MATERIAL:MESH", "SIZE:38", "SIZE:39", "SIZE:40", "SIZE:41");
        var sameLeather = Profile("LOAFER", "SHOES", true, "MATERIAL:LEATHER", "SIZE:44", "COLOR:BROWN");

        Assert.Equal(1, SimilarProducts.SharedAttributes(viewed, sameSizes));
        Assert.Equal(1, SimilarProducts.SharedAttributes(viewed, sameLeather));
        Assert.Equal(2, SimilarProducts.SharedAttributes(viewed, Profile("CHELSEA", "SHOES", true, "MATERIAL:LEATHER", "SIZE:40")));
    }

    [Fact]
    public void Neighbours_share_the_category_and_rank_by_what_they_share()
    {
        var viewed = Profile("MOKA", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM", "INDUCTION:false");
        List<ProductProfile> pool =
        [
            viewed,
            Profile("DRIP", "COFFEE_MAKER", true, "MATERIAL:PLASTIC"),
            Profile("STEEL", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM", "INDUCTION:false"),
            Profile("ALU", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM"),
            Profile("PAN", "COOKWARE", true, "MATERIAL:ALUMINIUM", "INDUCTION:false"),
        ];

        var neighbourhood = Assert.Single(SimilarProducts.Neighbourhoods([viewed], pool));

        Assert.Equal(["STEEL", "ALU", "DRIP"], neighbourhood.Candidates.Select(candidate => candidate.Product.Code));
        Assert.Equal([2, 1, 0], neighbourhood.Candidates.Select(candidate => candidate.Shared));
        Assert.All(neighbourhood.Candidates, candidate => Assert.Equal("MOKA", candidate.BecauseOf));
    }

    /// <summary>What the shopper already viewed is never suggested back, whichever viewed product would have reached it.</summary>
    [Fact]
    public void A_viewed_product_is_never_suggested()
    {
        var moka = Profile("MOKA", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM");
        var steel = Profile("STEEL", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM");
        var drip = Profile("DRIP", "COFFEE_MAKER");

        var neighbourhoods = SimilarProducts.Neighbourhoods([moka, steel], [moka, steel, drip]);

        Assert.All(neighbourhoods, neighbourhood => Assert.Equal(["DRIP"], neighbourhood.Candidates.Select(candidate => candidate.Product.Code)));
    }

    [Fact]
    public void A_sold_out_product_is_never_suggested()
    {
        var moka = Profile("MOKA", "COFFEE_MAKER", true, "MATERIAL:ALUMINIUM");

        var neighbourhood = Assert.Single(SimilarProducts.Neighbourhoods(
            [moka], [Profile("STEEL", "COFFEE_MAKER", false, "MATERIAL:ALUMINIUM"), Profile("DRIP", "COFFEE_MAKER")]));

        Assert.Equal(["DRIP"], neighbourhood.Candidates.Select(candidate => candidate.Product.Code));
    }

    [Fact]
    public void A_product_filed_nowhere_has_no_neighbours()
    {
        var loose = new ProductProfile(Strategies.Hit("id-LOOSE") with { Code = "LOOSE" }, null, []);
        var other = new ProductProfile(Strategies.Hit("id-OTHER") with { Code = "OTHER" }, null, []);

        Assert.Empty(Assert.Single(SimilarProducts.Neighbourhoods([loose], [other])).Candidates);
    }

    [Fact]
    public void The_re_rank_moves_only_what_it_has_a_vector_for_and_keeps_the_rule_order_otherwise()
    {
        var neighbourhood = new Neighbourhood(Profile("MOKA", "COFFEE_MAKER"),
        [
            new Suggestion(Profile("A", "COFFEE_MAKER"), "MOKA", 3),
            new Suggestion(Profile("B", "COFFEE_MAKER"), "MOKA", 2),
            new Suggestion(Profile("C", "COFFEE_MAKER"), "MOKA", 1),
            new Suggestion(Profile("D", "COFFEE_MAKER"), "MOKA", 0),
        ]);

        // C nearest, A and D alike, B has no vector.
        var reordered = SimilarProducts.Reorder(neighbourhood, new Dictionary<string, double> { ["id-C"] = 0.9, ["id-A"] = 0.5, ["id-D"] = 0.5 });

        Assert.Equal(["C", "A", "D", "B"], reordered.Candidates.Select(candidate => candidate.Product.Code));
    }

    /// <summary>The row takes turns: the first of each viewed product before the second of any.</summary>
    [Fact]
    public void The_row_is_spread_across_what_was_viewed_and_a_shared_neighbour_is_suggested_once()
    {
        var boots = new Neighbourhood(Profile("BOOTS", "SHOES"),
            [new Suggestion(Profile("S1", "SHOES"), "BOOTS", 2), new Suggestion(Profile("S2", "SHOES"), "BOOTS", 1), new Suggestion(Profile("S3", "SHOES"), "BOOTS", 0)]);
        var runner = new Neighbourhood(Profile("RUNNER", "SHOES"),
            [new Suggestion(Profile("S1", "SHOES"), "RUNNER", 2), new Suggestion(Profile("T2", "SHOES"), "RUNNER", 1)]);
        var lamp = new Neighbourhood(Profile("LAMP", "DESK_LAMP"),
            [new Suggestion(Profile("L1", "DESK_LAMP"), "LAMP", 1), new Suggestion(Profile("L2", "DESK_LAMP"), "LAMP", 0)]);

        var row = SimilarProducts.Spread([boots, runner, lamp], limit: 5);

        Assert.Equal(["S1", "L1", "S2", "T2", "L2"], row.Select(suggestion => suggestion.Product.Code));
        Assert.Equal(["BOOTS", "LAMP", "BOOTS", "RUNNER", "LAMP"], row.Select(suggestion => suggestion.BecauseOf));
    }

    [Fact]
    public void Nothing_viewed_is_an_empty_row()
    {
        Assert.Empty(SimilarProducts.Spread([], limit: 8));
    }

    /// <summary>A profile is the product: sold out is no variant in stock, and its traits are every variant's.</summary>
    [Fact]
    public void A_profile_reads_every_variant_and_its_card_is_one_somebody_can_buy()
    {
        static ProductSearchDocument Variant(string id, string product, decimal price, bool inStock, params string[] axes) => new()
        {
            Id = id,
            ProductId = product,
            Sku = $"{id}-SKU",
            Culture = "es",
            Name = product,
            Slug = product,
            Code = product.ToUpperInvariant(),
            Category = "SHOES",
            PriceCurrency = "EUR",
            Status = "active",
            PriceAmount = price,
            InStock = inStock,
            AxisValues = axes,
            FacetValues = ["MATERIAL:LEATHER"],
        };

        var profiles = ProductProfile.Of(
        [
            Variant("v1", "boots", 59m, inStock: false, "SIZE:38"),
            Variant("v2", "boots", 69m, inStock: true, "SIZE:42"),
            Variant("v3", "boots", 64m, inStock: true, "SIZE:40"),
            Variant("w1", "clogs", 30m, inStock: false, "SIZE:40"),
        ]);

        Assert.Equal(["boots", "clogs"], profiles.Select(profile => profile.ProductId));
        Assert.Equal(("v3", true), (profiles[0].Card.MatchedVariantId, profiles[0].InStock));
        Assert.Equal(["MATERIAL:LEATHER", "SIZE:38", "SIZE:40", "SIZE:42"], profiles[0].Traits);
        Assert.False(profiles[1].InStock);
    }
}
