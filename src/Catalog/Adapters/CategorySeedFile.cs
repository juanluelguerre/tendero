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
/// El árbol de categorías que trae el repositorio. Mismo criterio que las
/// definiciones de atributo: son datos de catálogo, se revisan en un diff y se
/// traducen sin tocar la base de datos.
/// </summary>
internal sealed class SeedFileCategoryReader(
    IOptions<CategorySeedOptions> options, TimeProvider clock) : ICategoryReader
{
    private CategoryTree? _cached;

    public async Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        var path = options.Value.FilePath;
        if (!File.Exists(path))
            return _cached = CategoryTree.Empty;

        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedCategory>>(
            file, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken) ?? [];

        // Se construye en el orden del fichero, así que un padre tiene que
        // aparecer antes que sus hijos. Es una restricción del fichero y no del
        // modelo, y salta a la vista al leerlo: los hijos van sangrados debajo.
        var built = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
        {
            var parent = entry.Parent is null ? null : built.GetValueOrDefault(entry.Parent);
            var category = Category.Define(clock, entry.Code, new LocalizedText(entry.Name), parent);
            built[category.Code] = category;
        }

        return _cached = new CategoryTree([.. built.Values]);
    }

    private sealed record SeedCategory(string Code, Dictionary<string, string> Name, string? Parent = null);
}
