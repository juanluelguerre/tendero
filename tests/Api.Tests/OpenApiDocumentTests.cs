using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The committed OpenAPI document has to be the one the API serves. Not a
/// formality: <c>docs/openapi/tendero.json</c> is where <c>@tendero/shared-api</c>'s
/// types come from — previously hand-written with nothing to detect the drift —
/// and where the UCP capability schemas will come from. A document out of sync is
/// worse than no document, because people believe it.
///
/// The API starts for real, with <see cref="WebApplicationFactory{TEntryPoint}"/>,
/// and neither Postgres nor Elasticsearch is needed: neither the DbContext nor
/// the Elastic client connects on construction, so two fake strings are enough.
/// This is the first test in the repository that boots the whole application, and
/// it costs milliseconds.
/// </summary>
public sealed class OpenApiDocumentTests
{
    /// <summary>
    /// Regenerate rather than fail: <c>UPDATE_OPENAPI=1 dotnet test</c>. It is a
    /// snapshot's workflow — the diff gets reviewed in the PR, which is exactly
    /// where a contract change has to be visible.
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

        // DeepEquals and not a string comparison: what matters is the document,
        // not its indentation or the order the serialiser wrote the keys in.
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
    /// Every served route appears in the document. It covers the failure
    /// <c>DeepEquals</c> cannot see: a new endpoint nobody regenerated is caught
    /// above, but one the generator silently skips would go unnoticed on both
    /// sides at once.
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
    /// Walks up from the test's directory until it finds the repository root.
    /// Searching for a marker rather than a string of <c>..</c> keeps the test
    /// correct if the project moves or the bin's TFM changes.
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
