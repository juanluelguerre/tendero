using System.Text.Json;
using ElGuerre.Tendero.Search.Images;

namespace ElGuerre.Tendero.SearchEval.Objects;

/// <summary>One photograph the objects suite asks about, and the kinds of what is in it.</summary>
/// <param name="Source"><c>abo</c> (a studio photograph of one product) or <c>author</c> (a real scene): always reported apart.</param>
public sealed record PictureCase(string Id, string Source, string File, IReadOnlyList<string> Expected);

/// <summary>
/// The suite's material, read from the repository: the cases (ABO's manifest, and
/// the author's when they exist), the kinds every detector is given, and the
/// category tree a kind's branch is read from.
/// </summary>
public static class PictureCases
{
    private static readonly string Folder = Path.Combine("tools", "SearchEval", "Objects");

    private sealed record AboManifest(IReadOnlyList<AboCase> Cases);

    private sealed record AboCase(string Image, string Kind, string Name);

    private sealed record Category(string Code, string? Parent);

    /// <summary>ABO's cases whose photograph <c>prepare.mjs abo</c> has written; the rest are counted as missing, not scored.</summary>
    public static IReadOnlyList<PictureCase> Abo(string pictures)
    {
        var manifest = JsonSerializer.Deserialize<AboManifest>(System.IO.File.ReadAllText(Path.Combine(Folder, "abo.json")), JsonSerializerOptions.Web)!;
        return [.. manifest.Cases.Select(c => new PictureCase(c.Image, "abo", Path.Combine(pictures, "abo", $"{c.Image}.jpg"), [c.Kind]))];
    }

    /// <summary>The author's scenes, when <c>author.json</c> exists beside ABO's manifest (each case: a file under design/unpublished/camera-photos and its kinds).</summary>
    public static IReadOnlyList<PictureCase> Author()
    {
        var path = Path.Combine(Folder, "author.json");
        if (!System.IO.File.Exists(path))
            return [];
        var cases = JsonSerializer.Deserialize<IReadOnlyList<AuthorCase>>(System.IO.File.ReadAllText(path), JsonSerializerOptions.Web)!;
        return [.. cases.Select(c => new PictureCase(c.File, "author", Path.Combine("design", "unpublished", "camera-photos", c.File), c.Expected))];
    }

    private sealed record AuthorCase(string File, IReadOnlyList<string> Expected);

    /// <summary>The shop's own kinds (<c>src/Search/Images/object-kinds.json</c>): the suite measures what the shop asks.</summary>
    public static IReadOnlyList<ObjectKind> Kinds() => ObjectKinds.Shop.All;

    public static IReadOnlyDictionary<string, string?> Parents(string categories) =>
        JsonSerializer.Deserialize<IReadOnlyList<Category>>(System.IO.File.ReadAllText(categories), JsonSerializerOptions.Web)!
            .ToDictionary(category => category.Code, category => category.Parent);
}
