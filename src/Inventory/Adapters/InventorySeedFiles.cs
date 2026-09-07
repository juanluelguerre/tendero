using System.Text.Json;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Inventory.Adapters;

public sealed class WarehouseSeedOptions
{
    public const string SectionName = "Inventory:Warehouses";

    public string FilePath { get; set; } = Path.Combine("seed", "warehouses.sample.json");
}

/// <summary>
/// The warehouses the repository ships.
///
/// Same reasoning as the attribute definitions and the price lists: two rows
/// that nothing can edit at runtime, reviewed in a diff and translated without
/// touching the database. When a warehouse becomes something a shopkeeper opens
/// and closes from a screen, the Postgres adapter registers in place of this one
/// and nothing else changes.
/// </summary>
internal sealed class SeedFileWarehouseReader(
    IOptions<WarehouseSeedOptions> options) : IWarehouseReader
{
    private IReadOnlyList<Warehouse>? cached;

    public async Task<IReadOnlyList<Warehouse>> AllAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.FilePath;

        // With no file there are no warehouses, which means nothing can be
        // allocated and every order is refused with a reason. That is a louder
        // failure than the other seed readers allow themselves, and it is the
        // right one: silently selling from a warehouse that does not exist is
        // worse than refusing.
        if (!File.Exists(path))
            return this.cached = [];

        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedWarehouse>>(
            file, Json, cancellationToken) ?? [];

        return this.cached =
        [
            .. raw.Select(entry => new Warehouse(
                entry.Code, new LocalizedText(entry.Name), entry.Priority, entry.IsActive))
        ];
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record SeedWarehouse(
        string Code, Dictionary<string, string> Name, int Priority, bool IsActive = true);
}
