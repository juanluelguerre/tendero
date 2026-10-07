using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>A picture a person rejected, kept with what was wrong with it.</summary>
/// <param name="File">Its file name inside the folder.</param>
/// <param name="ItemId">The product it was drawn for.</param>
/// <param name="Note">What the person saw, in their words.</param>
public sealed record Rejected(string File, string ItemId, Defect Defect, string Note);

/// <summary>
/// The pictures a batch rejected, kept instead of deleted (phase 13a, I-2b): a
/// folder beside the tool, and a manifest that says what is wrong with each. They
/// are never in `seed/images/`, so none can reach the catalogue.
///
/// The first batch's slipper, with its hand-wash emblem, was redrawn over and is
/// gone; that is the picture this folder exists not to lose again.
/// </summary>
public sealed class KnownBad(string folder)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private string Manifest => Path.Combine(folder, "manifest.json");

    public string Folder => folder;

    public IReadOnlyList<Rejected> Read() =>
        File.Exists(Manifest)
            ? JsonSerializer.Deserialize<Rejected[]>(File.ReadAllText(Manifest), JsonOptions) ?? []
            : [];

    /// <summary>Copies the picture in under a name of its own and writes it down. The original stays where it was, to be drawn over.</summary>
    public Rejected Keep(string picture, string itemId, Defect defect, string note)
    {
        Directory.CreateDirectory(folder);

        var kept = Read();
        var kind = defect.ToString().ToLowerInvariant();
        var number = kept.Count(rejected => string.Equals(rejected.ItemId, itemId, StringComparison.OrdinalIgnoreCase) && rejected.Defect == defect) + 1;
        var rejected = new Rejected($"{itemId}-{kind}-{number}{Path.GetExtension(picture)}", itemId, defect, note);

        File.Copy(picture, Path.Combine(folder, rejected.File));
        File.WriteAllText(Manifest, JsonSerializer.Serialize<Rejected[]>([.. kept, rejected], JsonOptions) + "\n");

        return rejected;
    }
}
