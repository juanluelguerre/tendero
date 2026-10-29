using System.Text.Json;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Exports.Features.ProductFeed;

/// <summary>
/// The shop's categories as Google's taxonomy ids (E-2): our category code → Google's
/// numeric id, which Merchant Center prefers to the path. A category the file does not
/// name takes the nearest ancestor's, since a coffee maker filed under "Kitchen" is
/// truer than one filed nowhere; a branch with none up it has none, and the feed
/// counts it rather than guessing.
/// </summary>
public sealed class GoogleCategoryMapping(IReadOnlyDictionary<string, int> byCategory)
{
    public static readonly GoogleCategoryMapping None = new(new Dictionary<string, int>());

    /// <summary>The file a person wrote, or <see cref="None"/> when it is not there: the feed still works, uncategorised.</summary>
    public static GoogleCategoryMapping Load(string path) =>
        File.Exists(path)
            ? new GoogleCategoryMapping(JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? [])
            : None;

    /// <summary>Google's id for the deepest step of the branch that has one.</summary>
    public int? Of(IReadOnlyList<ExportedCategoryStep> branch)
    {
        for (var step = branch.Count - 1; step >= 0; step--)
        {
            if (byCategory.TryGetValue(branch[step].Code, out var id))
                return id;
        }

        return null;
    }
}
