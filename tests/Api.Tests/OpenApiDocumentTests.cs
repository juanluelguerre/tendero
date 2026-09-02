using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// El documento de OpenAPI commiteado tiene que ser el que la API sirve. No es
/// una formalidad: de <c>docs/openapi/tendero.json</c> salen los tipos de
/// <c>@tendero/shared-api</c>, que antes se escribían a mano sin nada que
/// detectase la deriva, y de él saldrán los esquemas de las capabilities de UCP.
/// Un documento desincronizado es peor que no tenerlo, porque se le cree.
///
/// La API arranca de verdad, con <see cref="WebApplicationFactory{TEntryPoint}"/>,
/// y no hace falta ni Postgres ni Elasticsearch: ni el DbContext ni el cliente de
/// Elastic conectan al construirse, así que dos cadenas falsas bastan. Este es el
/// primer test del repositorio que levanta la aplicación entera, y cuesta
/// milisegundos.
/// </summary>
public sealed class OpenApiDocumentTests
{
    /// <summary>
    /// Regenerar en vez de fallar: <c>UPDATE_OPENAPI=1 dotnet test</c>. Es el
    /// flujo de una snapshot — el diff se revisa en el PR, que es justamente
    /// donde un cambio de contrato tiene que verse.
    /// </summary>
    private const string UpdateVariable = "UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions Formatting = new() { WriteIndented = true };

    [Fact]
    public async Task The_committed_document_is_the_one_the_api_serves()
    {
        var ct = TestContext.Current.CancellationToken;

        var served = await FetchDocumentAsync(ct);
        var committed = CommittedDocumentPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) is not (null or ""))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(committed)!);
            await File.WriteAllTextAsync(committed, Serialise(served), ct);
            return;
        }

        Assert.True(File.Exists(committed),
            $"{committed} does not exist. Generate it with: {UpdateVariable}=1 dotnet test");

        var onDisk = JsonNode.Parse(await File.ReadAllTextAsync(committed, ct));

        // DeepEquals y no comparación de cadenas: lo que importa es el documento,
        // no su sangrado ni el orden en que el serializador escribió las claves.
        Assert.True(
            JsonNode.DeepEquals(onDisk, served),
            $"""
             The OpenAPI document served by the API differs from {committed}.

             This is the contract test doing its job: an endpoint changed shape and
             the committed document — which generates the frontend's types — did not
             follow. Regenerate it and review the diff:

                 {UpdateVariable}=1 dotnet test --project tests/Api.Tests
             """);
    }

    /// <summary>
    /// Toda ruta servida aparece en el documento. Cubre el fallo que
    /// <c>DeepEquals</c> no puede ver: un endpoint nuevo que nadie regeneró se
    /// caza arriba, pero uno que el generador se salta en silencio pasaría
    /// inadvertido en los dos lados a la vez.
    /// </summary>
    [Fact]
    public async Task Every_documented_path_is_under_api()
    {
        var document = await FetchDocumentAsync(TestContext.Current.CancellationToken);
        var paths = document?["paths"]?.AsObject().Select(p => p.Key).ToArray() ?? [];

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.StartsWith("/api/", path, StringComparison.Ordinal));
    }

    private static async Task<JsonNode?> FetchDocumentAsync(CancellationToken ct)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", ct);
        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static string Serialise(JsonNode? document) =>
        document?.ToJsonString(Formatting) + Environment.NewLine;

    /// <summary>
    /// Sube desde el directorio del test hasta encontrar la raíz del repositorio.
    /// Buscar por marcador y no por una ristra de <c>..</c> mantiene el test
    /// correcto si el proyecto cambia de sitio o el TFM del bin cambia.
    /// </summary>
    private static string CommittedDocumentPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs", "openapi", "tendero.json");
    }

}
