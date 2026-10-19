using ElGuerre.Tendero.Search.Images;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// What the search keeps of whatever a detector found (the camera search spec,
/// CS-2), the same over every detector: one object a kind, none under the floor,
/// eight at most, the likeliest first.
/// </summary>
public sealed class ObjectsInPictureTests
{
    private static readonly ObjectKind Lamp = new("DESK_LAMP", "desk lamp", Sold: true);
    private static readonly ObjectKind Pan = new("COOKWARE", "frying pan", Sold: true);
    private static readonly ObjectKind Bottle = new("bottle", "bottle", Sold: false);

    private static DetectedObject Found(ObjectKind kind, double score, int left = 0) =>
        new(kind, new PictureBox(left, 0, 10, 10), score);

    [Fact]
    public void Of_one_kind_only_the_likeliest_box_is_kept()
    {
        var kept = ObjectsInPicture.Choose([Found(Lamp, 0.5, left: 0), Found(Lamp, 0.8, left: 50), Found(Pan, 0.6)], floor: 0.1);

        Assert.Equal([(Lamp, 50), (Pan, 0)], kept.Select(o => (o.Kind, o.Box.Left)));
    }

    [Fact]
    public void A_box_under_the_floor_is_not_counted()
    {
        var kept = ObjectsInPicture.Choose([Found(Lamp, 0.29), Found(Bottle, 0.3)], floor: 0.3);

        Assert.Equal([Bottle], kept.Select(o => o.Kind));
    }

    [Fact]
    public void Eight_at_most_the_likeliest_first()
    {
        var many = Enumerable.Range(0, 12).Select(i => Found(new ObjectKind($"K{i}", $"kind {i}", Sold: true), score: i / 20.0 + 0.1));

        var kept = ObjectsInPicture.Choose(many, floor: 0.0);

        Assert.Equal(ObjectsInPicture.MaxObjects, kept.Count);
        Assert.Equal(["K11", "K10", "K9", "K8", "K7", "K6", "K5", "K4"], kept.Select(o => o.Kind.Code));
    }

    [Fact]
    public void Nothing_found_is_nothing_kept() => Assert.Empty(ObjectsInPicture.Choose([], floor: 0.1));

    [Theory]
    [InlineData(-10, -5, 50, 40, 0, 0, 40, 35)]
    [InlineData(300, 200, 50, 80, 300, 200, 20, 40)]
    [InlineData(10, 10, 20, 20, 10, 10, 20, 20)]
    public void A_box_is_clipped_to_the_picture(int left, int top, int width, int height, int l, int t, int w, int h)
    {
        Assert.Equal(new PictureBox(l, t, w, h), new PictureBox(left, top, width, height).ClippedTo(320, 240));
    }

    /// <summary>A detector that reads words asks each alternative on its own: "bed or chest of drawers" as one query matched neither well (2026-10-19, OWLv2 on ABO).</summary>
    [Fact]
    public void A_kinds_words_are_its_alternatives_split_on_or() =>
        Assert.Equal(["kettle", "toaster", "blender"], new ObjectKind("SMALL_APPLIANCE", "kettle or toaster or blender", Sold: true).Alternatives);

    [Fact]
    public void A_box_wholly_outside_the_picture_clips_to_nothing() =>
        Assert.Null(new PictureBox(400, 10, 20, 20).ClippedTo(320, 240));

    /// <summary>
    /// The blocks follow the picture as it is read (CS-3): by thirds of its height,
    /// then left to right — so two objects side by side keep their order though one
    /// sits a little higher, and the number on each mark is the block's.
    /// </summary>
    [Fact]
    public void Objects_are_read_top_to_bottom_by_thirds_then_left_to_right()
    {
        DetectedObject At(string code, int left, int top, int height = 20) =>
            new(new ObjectKind(code, code, Sold: true), new PictureBox(left, top, 30, height), 0.5);
        var mouse = At("mouse", left: 200, top: 100);
        var keyboard = At("keyboard", left: 40, top: 110);
        var lamp = At("lamp", left: 250, top: 10);
        var rug = At("rug", left: 10, top: 200, height: 30);

        var read = ObjectsInPicture.InReadingOrder([mouse, rug, keyboard, lamp], pictureHeight: 240);

        Assert.Equal(["lamp", "keyboard", "mouse", "rug"], read.Select(found => found.Kind.Code));
    }
}
