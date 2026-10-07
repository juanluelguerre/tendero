using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>A rejected picture is kept with what was wrong with it, and the picture it was copied from is left to be drawn over (the phase 13a spec, I-2b).</summary>
public sealed class KnownBadTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("known-bad-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private string Picture(string name, byte marker)
    {
        var path = Path.Combine(scratch, name);
        File.WriteAllBytes(path, [marker]);
        return path;
    }

    [Fact]
    public void A_folder_nothing_was_kept_in_holds_nothing()
    {
        Assert.Empty(new KnownBad(Path.Combine(scratch, "none")).Read());
    }

    [Fact]
    public void A_rejected_picture_is_copied_in_and_written_down_with_its_defect_and_the_reason()
    {
        var set = new KnownBad(Path.Combine(scratch, "set"));
        var original = Picture("B10SHO0106.webp", 7);

        var kept = set.Keep(original, "B10SHO0106", Defect.Emblem, "a hand-wash symbol on its side");

        Assert.Equal(new Rejected("B10SHO0106-emblem-1.webp", "B10SHO0106", Defect.Emblem, "a hand-wash symbol on its side"), kept);
        Assert.Equal([kept], set.Read());
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(set.Folder, kept.File)));
        Assert.True(File.Exists(original));
    }

    /// <summary>The redraw came back with the same emblem: both are kept, neither over the other.</summary>
    [Fact]
    public void A_second_rejection_of_the_same_product_and_kind_does_not_replace_the_first()
    {
        var set = new KnownBad(Path.Combine(scratch, "set"));

        var first = set.Keep(Picture("first.webp", 1), "B10SHO0106", Defect.Emblem, "the first drawing");
        var second = set.Keep(Picture("second.webp", 2), "B10SHO0106", Defect.Emblem, "the redraw");

        Assert.Equal("B10SHO0106-emblem-2.webp", second.File);
        Assert.Equal([first, second], set.Read());
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(set.Folder, first.File)));
    }
}
