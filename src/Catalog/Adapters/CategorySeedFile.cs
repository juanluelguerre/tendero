using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

public sealed class CategorySeedOptions
{
    public const string SectionName = "Catalog:Categories";

    public string FilePath { get; set; } = Path.Combine("seed", "categories.sample.json");
}

/// <summary>
/// The category tree the repository ships. Same reasoning as the attribute
/// definitions: catalogue data, reviewed in a diff and translated without
/// touching the database.
/// </summary>
internal sealed class SeedFileCategoryReader(
    IOptions<CategorySeedOptions> options, TimeProvider clock) : ICategoryReader
{
    private CategoryTree? cached;

    public async Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.FilePath;
        if (!File.Exists(path))
            return this.cached = CategoryTree.Empty;

        return this.cached = await CategorySeedFile.LoadAsync(path, clock, cancellationToken);
    }
}

/// <summary>The category file, read: beside <see cref="AttributeDefinitionSeedFile"/>, and for the same reasons.</summary>
public static class CategorySeedFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<CategoryTree> LoadAsync(string path, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedCategory>>(file, Json, cancellationToken) ?? [];

        // Built in file order, so a parent has to appear before its children.
        // That is a constraint of the file and not of the model, and it is
        // obvious on reading it: the children are indented underneath.
        var built = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
        {
            var parent = entry.Parent is null ? null : built.GetValueOrDefault(entry.Parent);
            var category = Category.Define(clock, entry.Code, new LocalizedText(entry.Name), parent, attributes: entry.Attributes);
            built[category.Code] = category;
        }

        return new CategoryTree([.. built.Values]);
    }

    private sealed record SeedCategory(string Code, Dictionary<string, string> Name, string? Parent = null)
    {
        /// <summary>The attributes this category declares for itself (ADR 0056); its ancestors' are not repeated.</summary>
        public List<string> Attributes { get; init; } = [];
    }
}
