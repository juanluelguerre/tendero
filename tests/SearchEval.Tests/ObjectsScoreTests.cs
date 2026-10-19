using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SearchEval.Objects;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// How a detector's answer to one photograph is scored (the camera search spec,
/// CS-2): the likeliest kind right or not, the expected objects found over a
/// floor, the ones it made up — and a kind on the expected one's branch counts,
/// since a desk lamp is a lamp and a sneaker is a shoe.
/// </summary>
public sealed class ObjectsScoreTests
{
    private static readonly Dictionary<string, string?> Parents = new()
    {
        ["LIGHTING"] = "HOME",
        ["DESK_LAMP"] = "LIGHTING",
        ["FOOTWEAR"] = "APPAREL",
        ["SHOES"] = "FOOTWEAR",
        ["COOKWARE"] = "KITCHEN",
    };

    private static DetectedObject Found(string code, double score) =>
        new(new ObjectKind(code, code, Sold: true), new PictureBox(0, 0, 10, 10), score);

    [Fact]
    public void The_likeliest_kind_on_the_expected_branch_is_right()
    {
        var scored = ObjectsScore.Of(["LIGHTING"], [Found("DESK_LAMP", 0.8), Found("COOKWARE", 0.3)], Parents, floor: 0.5);

        Assert.True(scored.LikeliestRight);
    }

    [Fact]
    public void A_likelier_wrong_kind_is_not_right_even_when_the_expected_one_is_there()
    {
        var scored = ObjectsScore.Of(["COOKWARE"], [Found("DESK_LAMP", 0.8), Found("COOKWARE", 0.7)], Parents, floor: 0.5);

        Assert.False(scored.LikeliestRight);
        Assert.Equal(1, scored.Found);
        Assert.Equal(1, scored.Invented);
    }

    [Fact]
    public void Under_the_floor_nothing_is_found_and_nothing_is_invented()
    {
        var scored = ObjectsScore.Of(["SHOES"], [Found("FOOTWEAR", 0.45), Found("COOKWARE", 0.4)], Parents, floor: 0.5);

        Assert.True(scored.LikeliestRight);
        Assert.Equal((0, 0, 1), (scored.Found, scored.Invented, scored.Expected));
    }

    [Fact]
    public void Of_a_scene_each_expected_object_is_counted_once()
    {
        var scored = ObjectsScore.Of(["LIGHTING", "laptop", "bottle"], [Found("DESK_LAMP", 0.9), Found("laptop", 0.8), Found("book", 0.6)], Parents, floor: 0.5);

        Assert.Equal((2, 1, 3), (scored.Found, scored.Invented, scored.Expected));
    }

    [Fact]
    public void Nothing_found_is_not_right()
    {
        var scored = ObjectsScore.Of(["COOKWARE"], [], Parents, floor: 0.5);

        Assert.False(scored.LikeliestRight);
        Assert.Equal((0, 0, 1), (scored.Found, scored.Invented, scored.Expected));
    }
}
