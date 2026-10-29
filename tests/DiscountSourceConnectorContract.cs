using ElGuerre.Tendero.Pricing.Ports;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests.Sources;

/// <summary>
/// The discounts port's executable contract (phase 16, S2b): every discount
/// source inherits this class and passes the same suite. Linked as a file into
/// each test project that has one (Pricing.Tests for its fake, Shopify.Tests for
/// Shopify), like the other sources' suites.
/// </summary>
public abstract class DiscountSourceConnectorContractTests
{
    protected abstract IDiscountSourceConnector CreateConnector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<SourceDiscountRule>> All()
    {
        var discounts = new List<SourceDiscountRule>();
        await foreach (var discount in CreateConnector().StreamDiscountsAsync(Ct))
            discounts.Add(discount);
        return discounts;
    }

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    /// <summary>Each discount once, by an id of the source's, with a title a person can read in a note.</summary>
    [Fact]
    public async Task Discounts_have_an_id_a_title_and_no_id_twice()
    {
        var discounts = await All();

        Assert.NotEmpty(discounts);
        Assert.All(discounts, discount => Assert.False(string.IsNullOrWhiteSpace(discount.ExternalId) || string.IsNullOrWhiteSpace(discount.Title)));
        Assert.Equal(discounts.Count, discounts.Select(discount => discount.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A percentage is from 0 to 100, never a fraction; an amount is never negative; a buy X get Y buys and gives something.</summary>
    [Fact]
    public async Task What_a_discount_takes_off_is_in_the_ports_units() =>
        Assert.All(await All(), discount =>
        {
            switch (discount.Effect)
            {
                case SourcePercentOff(var percent):
                    Assert.InRange(percent, 0.01m, 100m);
                    break;
                case SourceAmountOff(var amount, _):
                    Assert.True(amount.Amount > 0, discount.Title);
                    break;
                case SourceBuyXGetY(var buy, var get, var percent, _):
                    Assert.True(buy > 0 && get > 0, discount.Title);
                    Assert.InRange(percent, 0.01m, 100m);
                    break;
                case SourceFreeShipping or SourceOtherEffect:
                    break;
            }
        });

    /// <summary>A target of SKUs names at least one; an ending comes after its start.</summary>
    [Fact]
    public async Task A_discounts_reach_and_dates_are_whole() =>
        Assert.All(await All(), discount =>
        {
            if (discount.Target is SkuItems(var skus))
                Assert.NotEmpty(skus);
            if (discount.EndsAt is { } ends)
                Assert.True(ends > discount.StartsAt, discount.Title);
        });
}
