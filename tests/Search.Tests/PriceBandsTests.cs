using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The price bands (the facets spec, open question 1): three to five, cut at the
/// results' percentiles, at prices a person would say, empty ones dropped. A pure
/// function, so its edges are here rather than behind a cluster.
/// </summary>
public sealed class PriceBandsTests
{
    [Theory]
    [InlineData("37", "40")]
    [InlineData("44", "40")]
    [InlineData("46", "50")]
    [InlineData("45", "40")]      // halfway goes down
    [InlineData("23", "25")]
    [InlineData("12.4", "10")]
    [InlineData("12.6", "15")]
    [InlineData("62", "60")]
    [InlineData("68", "75")]
    [InlineData("8.6", "7.5")]
    [InlineData("110", "100")]
    [InlineData("130", "150")]
    [InlineData("240", "250")]
    [InlineData("875", "750")]
    [InlineData("960", "1000")]
    [InlineData("1000", "1000")]
    [InlineData("2600", "2500")]
    [InlineData("0.8", "0.75")]
    [InlineData("0", "0")]
    public void A_price_rounds_to_the_nearest_one_a_person_would_say(string price, string said)
    {
        Assert.Equal(decimal.Parse(said, System.Globalization.CultureInfo.InvariantCulture),
            PriceBands.Round(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>The spec's shoes, 18.50 to 169: four cuts, five bands.</summary>
    [Fact]
    public void Percentiles_become_up_to_four_rounded_cuts_inside_the_range()
    {
        var cuts = PriceBands.Boundaries(18.50m, 169m, [29.90m, 49.90m, 79.95m, 119m]);

        Assert.Equal([30m, 50m, 75m, 100m], cuts);
        Assert.Equal(5, PriceBands.Between(cuts).Count);
    }

    [Fact]
    public void Percentiles_that_round_alike_give_one_cut_not_three()
    {
        Assert.Equal([25m, 75m], PriceBands.Boundaries(20m, 90m, [24m, 26m, 27m, 80m]));
    }

    /// <summary>A cut on the cheapest or dearest price would leave a band of one price at the edge.</summary>
    [Fact]
    public void A_cut_is_strictly_inside_the_range_and_the_quarters_fill_in_when_too_few_are_left()
    {
        // 30 is the minimum and 90 the maximum, so only 60 survives from the
        // percentiles; the quarters (45 → 40, 60, 75) make it three.
        Assert.Equal([40m, 60m, 75m], PriceBands.Boundaries(30m, 90m, [30m, 30m, 60m, 90m]));
    }

    [Fact]
    public void A_narrow_range_gets_the_one_cut_it_has()
    {
        var cuts = PriceBands.Boundaries(29.90m, 34.90m, [29.90m, 29.90m, 34.90m, 34.90m]);

        Assert.Equal([30m], cuts);
        Assert.Equal([(null, 30m), (30m, null)], PriceBands.Between(cuts));
    }

    [Theory]
    [InlineData("29.90", "29.90")]   // one price
    [InlineData("49.90", "49.90")]   // every price the same
    [InlineData("29.90", "29.95")]   // two prices with no price a person says between them
    public void Without_a_range_to_split_there_are_no_bands(string min, string max)
    {
        var low = decimal.Parse(min, System.Globalization.CultureInfo.InvariantCulture);
        var high = decimal.Parse(max, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Empty(PriceBands.Boundaries(low, high, [low, high]));
        Assert.Empty(PriceBands.Between([]));
    }

    [Fact]
    public void A_wide_range_is_cut_at_its_own_magnitudes()
    {
        Assert.Equal([10m, 100m, 500m, 2500m], PriceBands.Boundaries(5m, 5000m, [12m, 95m, 480m, 2600m]));
    }

    [Fact]
    public void More_than_four_cuts_keep_four_spread_across_them()
    {
        var cuts = PriceBands.Boundaries(10m, 70m, [15m, 25m, 35m, 45m, 55m, 65m]);

        Assert.Equal([15m, 25m, 40m, 60m], cuts);
    }

    [Fact]
    public void The_bands_run_open_below_between_each_cut_and_open_above()
    {
        Assert.Equal(
            [(null, 25m), (25m, 75m), (75m, null)],
            PriceBands.Between([25m, 75m]));
    }

    [Fact]
    public void An_empty_band_is_dropped_unless_it_is_the_one_chosen()
    {
        IReadOnlyList<(decimal? From, decimal? To)> bands = [(null, 25m), (25m, 50m), (50m, null)];

        var unchosen = FacetAssembler.Bands(bands, [3, 0, 2], SearchFilters.None);
        Assert.Equal([(null, 25m), (50m, null)], unchosen.Select(band => (band.From, band.To)));

        var chosen = FacetAssembler.Bands(bands, [3, 0, 2], new SearchFilters { MinPrice = 25m, MaxPrice = 50m });
        Assert.Equal([false, true, false], chosen.Select(band => band.Chosen));
        Assert.Equal(0, chosen[1].Count);
    }

    /// <summary>A percentile that rounds onto the dearest price is not a cut: it would leave a band of one price.</summary>
    [Fact]
    public void A_cut_on_the_dearest_price_is_left_out()
    {
        // 25 survives; 50 is the maximum, so the quarters fill in: 27.5 → 25, 35 → 30, 42.5 → 40.
        Assert.Equal([25m, 30m, 40m], PriceBands.Boundaries(20m, 50m, [25m, 50m, 50m, 50m]));
    }

    /// <summary>A band the aggregation gave no count for counts nothing, and is kept only if chosen.</summary>
    [Fact]
    public void A_band_without_a_count_counts_nothing()
    {
        var bands = FacetAssembler.Bands([(null, 25m), (25m, 50m), (50m, null)], [3], new SearchFilters { MinPrice = 50m });

        Assert.Equal([(3L, false), (0L, true)], bands.Select(band => (band.Count, band.Chosen)));
    }

    /// <summary>Only the band whose both ends match is chosen: a maximum alone does not choose the band that merely starts open.</summary>
    [Fact]
    public void A_band_is_chosen_by_both_its_ends()
    {
        var bands = FacetAssembler.Bands([(null, 25m), (25m, 50m), (50m, null)], [3, 2, 1], new SearchFilters { MaxPrice = 50m });

        Assert.Equal([false, false, false], bands.Select(band => band.Chosen));
    }

    [Fact]
    public void The_lowest_band_is_chosen_by_a_maximum_alone()
    {
        var bands = FacetAssembler.Bands([(null, 25m), (25m, null)], [3, 2], new SearchFilters { MaxPrice = 25m });

        Assert.Equal([true, false], bands.Select(band => band.Chosen));
    }
}
