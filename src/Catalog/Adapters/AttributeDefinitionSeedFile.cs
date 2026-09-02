using System.Text.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

public sealed class AttributeSeedOptions
{
    public const string SectionName = "Catalog:Attributes";

    public string FilePath { get; set; } = Path.Combine("seed", "attributes.sample.json");
}

/// <summary>
/// The definitions the repository ships, read the same way the sample catalogue
/// is.
///
/// A file rather than an `INSERT` in a migration because these labels are
/// catalogue DATA and not schema: they get reviewed in a diff, translated and
/// corrected without touching the database. Same reason the golden set lives in
/// `tools/SearchEval/golden` and not in code.
/// </summary>
public static class AttributeDefinitionSeedFile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<IReadOnlyList<AttributeDefinition>> LoadAsync(
        string path, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        var raw = await JsonSerializer.DeserializeAsync<List<SeedDefinition>>(file, Json, cancellationToken)
            ?? [];

        return [.. raw.Select(entry => Build(entry, clock))];
    }

    public static Task<IReadOnlyList<AttributeDefinition>> LoadAsync(
        IOptions<AttributeSeedOptions> options, TimeProvider clock, CancellationToken cancellationToken = default) =>
        LoadAsync(options.Value.FilePath, clock, cancellationToken);

    private static AttributeDefinition Build(SeedDefinition entry, TimeProvider clock)
    {
        var definition = AttributeDefinition.Define(
            clock,
            entry.Code,
            new LocalizedText(entry.Label),
            entry.Kind,
            // An empty unit in the file means "no unit": cups and pieces are
            // counted, not measured.
            string.IsNullOrWhiteSpace(entry.Unit) ? null : entry.Unit,
            entry.IsVariantAxis,
            entry.IsFacet,
            entry.IsSearchable);

        foreach (var alias in entry.Aliases)
            definition.AddAlias(clock, alias);

        foreach (var option in entry.Options)
            definition.AddOption(clock, option.Code, new LocalizedText(option.Label));

        return definition;
    }

    private sealed record SeedDefinition(
        string Code,
        Dictionary<string, string> Label,
        AttributeKind Kind,
        string? Unit = null,
        bool IsVariantAxis = false,
        bool IsFacet = false,
        bool IsSearchable = true)
    {
        public List<string> Aliases { get; init; } = [];
        public List<SeedOption> Options { get; init; } = [];
    }

    private sealed record SeedOption(string Code, Dictionary<string, string> Label);
}
