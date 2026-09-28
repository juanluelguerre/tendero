using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What `GET /api/search` refuses at the door. Every filter value becomes a
/// clause of the query, and a URL is something anybody can write.
/// </summary>
public sealed class SearchProductsValidatorTests
{
    private static readonly SearchProductsValidator Validator = new();

    private static bool Valid(SearchFilters? filters = null, ProductSort sort = ProductSort.Relevance) =>
        Validator.Validate(new SearchProductsQuery("", "es", 1, 20, Sort: sort, Filters: filters)).IsValid;

    [Theory]
    [InlineData(ProductSort.Relevance)]
    [InlineData(ProductSort.Newest)]
    [InlineData(ProductSort.PriceAsc)]
    [InlineData(ProductSort.PriceDesc)]
    public void The_orders_the_index_can_keep_are_accepted(ProductSort sort) => Assert.True(Valid(sort: sort));

    /// <summary>An enum holds any integer; one it does not name is an order nobody can keep.</summary>
    [Fact]
    public void An_order_the_index_cannot_keep_is_refused() => Assert.False(Valid(sort: (ProductSort)42));

    [Theory]
    [InlineData("COLOR:BLACK", true)]
    [InlineData("SIZE:42", true)]
    [InlineData("INDUCTION:true", true)]
    [InlineData("color:BLACK", false)]   // the code as the catalogue writes it
    [InlineData("COLOR", false)]
    [InlineData("COLOR:", false)]
    [InlineData(":BLACK", false)]
    [InlineData("COLOR:BLACK:RED", false)]
    public void A_facet_filter_is_code_colon_value(string pair, bool valid) =>
        Assert.Equal(valid, Valid(new SearchFilters { Facets = [pair] }));

    [Fact]
    public void A_price_range_is_not_negative_and_not_upside_down()
    {
        Assert.True(Valid(new SearchFilters { MinPrice = 30, MaxPrice = 30 }));
        Assert.True(Valid(new SearchFilters { MaxPrice = 30 }));
        Assert.False(Valid(new SearchFilters { MinPrice = -1 }));
        Assert.False(Valid(new SearchFilters { MinPrice = 80, MaxPrice = 30 }));
    }

    [Fact]
    public void Choices_are_bounded_in_number_and_length()
    {
        Assert.False(Valid(new SearchFilters { Brands = [.. Enumerable.Range(0, SearchFiltersValidator.MaxChoices + 1).Select(index => $"B{index}")] }));
        Assert.False(Valid(new SearchFilters { Brands = [new string('x', 101)] }));
        Assert.False(Valid(new SearchFilters { Within = [""] }));
        Assert.True(Valid(new SearchFilters { Brands = ["Lumen&Co"], Within = ["SHOES", "COOKWARE"] }));
    }
}
