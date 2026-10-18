using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// A sentence that fills a kit's form, over HTTP (phase 13c, K-6). The test
/// host runs with no model, which is the case the page must survive: the box
/// is not drawn, and a sentence sent anyway is answered "not understood", never
/// refused (invariant 8).
/// </summary>
public sealed class KitSentenceEndpointTests(TenderoApiFactory factory) : IClassFixture<TenderoApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_no_model_the_box_is_not_offered_and_a_sentence_is_not_understood()
    {
        using var client = factory.CreateClient();

        var status = JsonNode.Parse(await client.GetStringAsync("/api/kits/understand/status", Ct))!;
        var response = await client.PostAsJsonAsync("/api/kits/understand?culture=es", new { sentence = "un kit de cocina por 200 €" }, Ct);
        var understood = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.False(status["available"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((false, (string?)null), (understood["understood"]!.GetValue<bool>(), understood["template"]?.GetValue<string>()));
        Assert.Contains("Accept-Language", response.Headers.Vary);
    }

    [Fact]
    public async Task An_empty_sentence_is_refused_as_invalid_naming_the_field()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/kits/understand?culture=en", new { sentence = "  " }, Ct);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        Assert.Equal((HttpStatusCode.BadRequest, "Invalid"), (response.StatusCode, problem["code"]!.GetValue<string>()));
        Assert.NotNull(problem["errors"]?["sentence"] ?? problem["errors"]?["Sentence"]);
    }
}
