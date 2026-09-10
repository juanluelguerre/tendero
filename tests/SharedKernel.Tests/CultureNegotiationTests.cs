using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The ADR 0013 chain — explicit parameter, then <c>Accept-Language</c>, then
/// <c>es</c> — which every endpoint returning localized text goes through.
///
/// It had no tests of its own. Every localized endpoint trusted it, and the
/// part most likely to be wrong — parsing a header a browser writes and an
/// agent may not — was never run against a single header.
/// </summary>
public sealed class CultureNegotiationTests
{
    // ---------- The parameter ----------

    [Fact]
    public void An_explicit_culture_wins_over_the_header()
    {
        Assert.Equal("en", CultureNegotiation.Resolve("en", "es"));
    }

    [Fact]
    public void An_explicit_culture_is_normalised()
    {
        Assert.Equal("en", CultureNegotiation.Resolve("EN-gb", null));
    }

    /// <summary>A culture the shop does not speak is not an error: the chain
    /// carries on to the header, as if the parameter had not been sent.</summary>
    [Fact]
    public void An_unsupported_culture_falls_through_to_the_header()
    {
        Assert.Equal("en", CultureNegotiation.Resolve("fr", "en"));
    }

    // ---------- The default ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_nothing_to_go_on_the_answer_is_spanish(string? header)
    {
        Assert.Equal("es", CultureNegotiation.Resolve(null, header));
    }

    [Fact]
    public void A_header_naming_only_languages_the_shop_does_not_speak_is_answered_in_spanish()
    {
        Assert.Equal("es", CultureNegotiation.Resolve(null, "fr-FR, de;q=0.9, *;q=0.5"));
    }

    // ---------- The header ----------

    [Fact]
    public void The_header_is_ordered_by_quality_not_by_position()
    {
        Assert.Equal("en", CultureNegotiation.Resolve(null, "es;q=0.5, en;q=0.9"));
    }

    /// <summary>RFC 9110: a language with no <c>q</c> has quality 1.</summary>
    [Fact]
    public void A_language_with_no_quality_counts_as_one()
    {
        Assert.Equal("en", CultureNegotiation.Resolve(null, "es;q=0.9, en"));
    }

    [Fact]
    public void The_first_language_the_shop_speaks_wins_over_a_better_one_it_does_not()
    {
        Assert.Equal("en", CultureNegotiation.Resolve(null, "fr, en;q=0.5"));
    }

    /// <summary>Equal qualities keep the order the browser wrote them in: the
    /// sort has to be stable, or the answer depends on the sort's mood.</summary>
    [Theory]
    [InlineData("en, es", "en")]
    [InlineData("es, en", "es")]
    [InlineData("en;q=0.8, es;q=0.8", "en")]
    public void Equal_qualities_keep_the_order_they_were_written_in(string header, string expected)
    {
        Assert.Equal(expected, CultureNegotiation.Resolve(null, header));
    }

    [Fact]
    public void A_region_subtag_is_reduced_to_its_language()
    {
        Assert.Equal("en", CultureNegotiation.Resolve(null, "en-GB,en;q=0.9"));
    }

    [Fact]
    public void The_quality_parameter_is_read_whatever_its_case()
    {
        Assert.Equal("en", CultureNegotiation.Resolve(null, "es;Q=0.1, en;Q=0.9"));
    }

    /// <summary>A quality nobody can read is not a reason to refuse the
    /// request; it counts as the default, 1.</summary>
    [Fact]
    public void An_unreadable_quality_counts_as_one()
    {
        Assert.Equal("es", CultureNegotiation.Resolve(null, "es;q=abc, en;q=0.5"));
    }

    /// <summary>
    /// RFC 9110 §12.4.2: <c>q=0</c> means "not acceptable". A reader who says
    /// they do NOT want English must not be answered in English because it
    /// happened to be the only supported language in the header.
    /// </summary>
    [Fact]
    public void A_language_with_quality_zero_is_refused_rather_than_chosen()
    {
        Assert.Equal("es", CultureNegotiation.Resolve(null, "en;q=0, fr"));
    }

    // ---------- Supports ----------

    [Theory]
    [InlineData("es", true)]
    [InlineData("EN", true)]
    [InlineData("en-US", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void Supports_answers_for_the_two_languages_the_shop_speaks(string? culture, bool expected)
    {
        Assert.Equal(expected, CultureNegotiation.Supports(culture));
    }
}
