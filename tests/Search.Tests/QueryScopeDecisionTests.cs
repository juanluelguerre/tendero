using ElGuerre.Tendero.Search.Answers;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

public sealed class QueryScopeDecisionTests
{
    [Theory]
    [InlineData(0.40, 0.60, 0.05, ScopeOutcome.OutOfScope)]
    [InlineData(0.60, 0.40, 0.05, ScopeOutcome.InScope)]
    [InlineData(0.50, 0.52, 0.05, ScopeOutcome.Ambiguous)]
    [InlineData(0.52, 0.50, 0.05, ScopeOutcome.Ambiguous)]
    [InlineData(0.45, 0.50, 0.05, ScopeOutcome.OutOfScope)]   // exactly the margin is enough
    [InlineData(0.50, 0.45, 0.05, ScopeOutcome.InScope)]
    public void The_nearest_anchors_decide_only_when_one_side_clearly_wins(double inside, double outside, double margin, ScopeOutcome expected) =>
        Assert.Equal(expected, ScopeDecision.Decide(inside, outside, margin));

    [Fact]
    public void The_anchor_file_holds_at_least_twenty_categories_each_in_both_languages()
    {
        var file = OutOfScopeAnchors.Load();

        Assert.True(file.Categories.Count >= 20);
        Assert.All(file.Categories, category =>
        {
            Assert.False(string.IsNullOrWhiteSpace(category.Label["es"]));
            Assert.False(string.IsNullOrWhiteSpace(category.Label["en"]));
            Assert.NotEmpty(category.Texts["es"]);
            Assert.NotEmpty(category.Texts["en"]);
        });
        Assert.Equal(file.Categories.Count, file.Categories.Select(category => category.Code).Distinct().Count());
    }

    [Fact]
    public void A_different_taxonomy_has_a_different_fingerprint()
    {
        var one = ScopeFingerprint.OfCategories([("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen")]);
        var renamed = ScopeFingerprint.OfCategories([("KITCHEN", "Cocina", "Kitchens", "Hogar › Cocina", "Home › Kitchens")]);
        var reordered = ScopeFingerprint.OfCategories([("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen")]);

        Assert.NotEqual(one, renamed);
        Assert.Equal(one, reordered);
    }

    [Fact]
    public void The_fingerprint_does_not_depend_on_the_order_categories_are_read_in()
    {
        var a = ("A", "a", "a", "a", "a");
        var b = ("B", "b", "b", "b", "b");

        Assert.Equal(ScopeFingerprint.OfCategories([a, b]), ScopeFingerprint.OfCategories([b, a]));
    }

    // The rename test above changes the English label AND the English path
    // together ("Kitchen" -> "Kitchens" in both), so it cannot tell apart a
    // fingerprint that reads all five fields from one that silently drops
    // either English one — the other still carries the difference. This
    // isolates each field in turn, changing exactly one and leaving the rest
    // at the baseline, so dropping any single field from the hash input is
    // its own failing row rather than hiding behind another field's change.
    [Theory]
    [InlineData("KITCHEN2", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen")]
    [InlineData("KITCHEN", "Cocina2", "Kitchen", "Hogar › Cocina", "Home › Kitchen")]
    [InlineData("KITCHEN", "Cocina", "Kitchen2", "Hogar › Cocina", "Home › Kitchen")]
    [InlineData("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina2", "Home › Kitchen")]
    [InlineData("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen2")]
    public void Every_one_of_the_five_fields_changes_the_fingerprint_on_its_own(string code, string es, string en, string pathEs, string pathEn)
    {
        var baseline = ScopeFingerprint.OfCategories([("KITCHEN", "Cocina", "Kitchen", "Hogar › Cocina", "Home › Kitchen")]);
        var changed = ScopeFingerprint.OfCategories([(code, es, en, pathEs, pathEn)]);

        Assert.NotEqual(baseline, changed);
    }
}
