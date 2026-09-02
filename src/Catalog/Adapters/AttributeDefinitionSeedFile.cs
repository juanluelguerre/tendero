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
/// Las definiciones que trae el repositorio, leídas del mismo modo que el
/// catálogo de muestra.
///
/// Existe el fichero y no un `INSERT` en una migración porque estas etiquetas
/// son DATOS de catálogo, no esquema: se revisan en un diff, se traducen y se
/// corrigen sin tocar la base de datos. Es la misma razón por la que el golden
/// set vive en `tools/SearchEval/golden` y no en código.
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
            // Una unidad vacía en el fichero es "sin unidad": tazas y piezas se
            // cuentan, no se miden.
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
