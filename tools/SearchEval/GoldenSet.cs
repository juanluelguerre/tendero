using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tendero.SearchEval;

/// <summary>
/// Conjunto anotado de una cultura. Las anotaciones se identifican por
/// <see cref="Judgment.ExternalId"/> y no por el id interno del producto: ese id
/// es un GUID v7 que se genera en cada importación, así que un golden set que lo
/// usara caducaría cada vez que se reimporta el catálogo. El id del origen
/// ("B073WXYZ01") es estable, y además se puede leer y revisar en un PR.
/// </summary>
public sealed record GoldenSet(
    string Culture,
    string Source,
    IReadOnlyList<GoldenQuery> Queries)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<GoldenSet> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<GoldenSet>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"'{path}' is empty or not a golden set.");
    }
}

public sealed record GoldenQuery(string Query, IReadOnlyList<Judgment> Judgments)
{
    public Dictionary<string, int> ToRelevanceMap() =>
        Judgments.ToDictionary(judgment => judgment.ExternalId, judgment => judgment.Relevance);
}

/// <summary>
/// Un juicio de relevancia. <see cref="Why"/> no es un comentario decorativo: es
/// lo que hace la anotación revisable en un PR. Sin él nadie puede juzgar si la
/// nota está bien puesta, y la puerta acaba midiendo los prejuicios de quien anotó.
/// </summary>
public sealed record Judgment(
    string ExternalId,
    [property: JsonPropertyName("relevance")] int Relevance,
    string Why);
