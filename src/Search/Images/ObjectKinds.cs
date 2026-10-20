using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// What a photograph is searched for (the camera search, CS-2): the shop's
/// categories a picture can show and the common objects it does not sell, as the
/// detector asks for them, with the name a kind not sold is given in the line
/// under the results. A sold kind is named by its category, which the catalogue
/// owns; a kind not sold has no category, so it carries its names here.
///
/// Read from <c>object-kinds.json</c>, embedded beside this class — the one file
/// the shop and the suite that measured it both read.
/// </summary>
public sealed class ObjectKinds
{
    private readonly Dictionary<string, LocalizedText> notSold;

    private ObjectKinds(IReadOnlyList<ObjectKind> all, Dictionary<string, LocalizedText> notSold) => (this.All, this.notSold) = (all, notSold);

    /// <summary>The shop's, as the embedded file declares them.</summary>
    public static ObjectKinds Shop { get; } = Parse(Embedded());

    /// <summary>Sold first, in the file's order: the order the detector is asked in.</summary>
    public IReadOnlyList<ObjectKind> All { get; }

    /// <summary>A kind not sold's name in the culture asked, or null for a sold kind, whose name is its category's.</summary>
    public string? NameOf(string code, string culture) => this.notSold.TryGetValue(code, out var name) ? name.In(culture) : null;

    private sealed record File(Dictionary<string, string> Sold, Dictionary<string, NotSold> NotSold);

    private sealed record NotSold(string Words, string? Es, string? En);

    public static ObjectKinds Parse(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The object kinds file is empty.");

        Dictionary<string, LocalizedText> names = new(StringComparer.Ordinal);
        foreach (var (code, kind) in file.NotSold)
        {
            // Invariant 6: the line under the results is said in both languages, or the file is wrong.
            if (string.IsNullOrWhiteSpace(kind.Es) || string.IsNullOrWhiteSpace(kind.En))
                throw new InvalidDataException($"The kind not sold '{code}' needs its name in es and en.");
            names[code] = LocalizedText.Both(kind.En, kind.Es);
        }

        return new ObjectKinds(
        [
            .. file.Sold.Select(kind => new ObjectKind(kind.Key, kind.Value, Sold: true)),
            .. file.NotSold.Select(kind => new ObjectKind(kind.Key, kind.Value.Words, Sold: false)),
        ], names);
    }

    private static string Embedded()
    {
        using var stream = typeof(ObjectKinds).Assembly.GetManifestResourceStream("ElGuerre.Tendero.Search.Images.object-kinds.json")
            ?? throw new InvalidOperationException("object-kinds.json is not embedded in the Search assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
