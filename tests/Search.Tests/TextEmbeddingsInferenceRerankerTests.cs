using System.Net;
using System.Text;
using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Reranking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The reranker adapter against a model server that records what it was sent and
/// answers the way Text Embeddings Inference does: scores for the texts, sorted
/// by score rather than by the order they were given.
/// </summary>
public sealed class TextEmbeddingsInferenceRerankerTests
{
    private const string Revision = "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scores_come_back_in_the_order_the_texts_were_given()
    {
        var server = new ModelServer(Revision, """[{"index":1,"score":0.98},{"index":2,"score":0.41},{"index":0,"score":0.02}]""");

        var scores = await Reranker(server).ScoreAsync("regalo para la cocina", ["mochila", "cafetera", "sartén"], Ct);

        Assert.Equal([0.02, 0.98, 0.41], scores);
    }

    /// <summary>
    /// The request stays inside what TEI accepts and asks for what the strategy
    /// relies on: truncation rather than a refusal for a long description, and
    /// normalised scores rather than logits.
    /// </summary>
    [Fact]
    public async Task The_request_is_the_query_the_texts_truncation_and_normalised_scores()
    {
        var server = new ModelServer(Revision, """[{"index":0,"score":0.5}]""");

        await Reranker(server).ScoreAsync("cafetera", ["Cafetera italiana"], Ct);

        var body = JsonDocument.Parse(server.RerankBodies.Single()).RootElement;
        Assert.Equal(["query", "raw_scores", "return_text", "texts", "truncate"], body.EnumerateObject().Select(field => field.Name).Order());
        Assert.Equal("cafetera", body.GetProperty("query").GetString());
        Assert.Equal(["Cafetera italiana"], body.GetProperty("texts").EnumerateArray().Select(text => text.GetString()));
        Assert.True(body.GetProperty("truncate").GetBoolean());
        Assert.False(body.GetProperty("raw_scores").GetBoolean());
        Assert.False(body.GetProperty("return_text").GetBoolean());
    }

    /// <summary>A score for an index outside what was sent — a negative one, or one past the last text — is not a score for anything.</summary>
    [Fact]
    public async Task A_score_for_a_text_that_was_never_sent_is_ignored()
    {
        var server = new ModelServer(Revision, """[{"index":2,"score":0.77},{"index":-1,"score":0.66},{"index":1,"score":0.20},{"index":0,"score":0.90}]""");

        var scores = await Reranker(server).ScoreAsync("a", ["x", "y"], Ct);

        Assert.Equal([0.90, 0.20], scores);
    }

    [Fact]
    public async Task The_served_revision_is_checked_once_before_the_first_score_is_trusted()
    {
        var server = new ModelServer(Revision, """[{"index":0,"score":0.5}]""");
        var reranker = Reranker(server);

        await reranker.ScoreAsync("a", ["x"], Ct);
        await reranker.ScoreAsync("b", ["y"], Ct);

        Assert.Equal(1, server.InfoRequests);
        Assert.Equal(2, server.RerankBodies.Count);
    }

    [Fact]
    public async Task A_reranker_nobody_measured_is_refused_before_it_scores_anything()
    {
        var server = new ModelServer(new string('0', 40), """[{"index":0,"score":0.5}]""");

        var refusal = await Assert.ThrowsAsync<SearchUnavailableException>(() => Reranker(server).ScoreAsync("a", ["x"], Ct));

        Assert.Contains(new string('0', 40), refusal.Message);
        Assert.Empty(server.RerankBodies);
    }

    [Fact]
    public async Task A_missing_score_is_a_failure_rather_than_a_zero()
    {
        var server = new ModelServer(Revision, """[{"index":0,"score":0.5}]""");

        await Assert.ThrowsAsync<SearchUnavailableException>(() => Reranker(server).ScoreAsync("a", ["x", "y"], Ct));
    }

    [Fact]
    public async Task A_server_that_does_not_answer_is_the_exception_search_survives()
    {
        var server = new ModelServer(Revision, "") { Status = HttpStatusCode.ServiceUnavailable };

        await Assert.ThrowsAsync<SearchUnavailableException>(() => Reranker(server).ScoreAsync("a", ["x"], Ct));
    }

    [Fact]
    public void Adding_a_reranker_registers_this_adapter_without_calling_it()
    {
        using var provider = new ServiceCollection().AddReranker(new Uri("http://127.0.0.1:1")).BuildServiceProvider();

        Assert.IsType<TextEmbeddingsInferenceReranker>(provider.GetRequiredService<IReranker>());
    }

    private static TextEmbeddingsInferenceReranker Reranker(ModelServer server) =>
        new(new HttpClient(server) { BaseAddress = new Uri("http://reranker/") }, RerankerModel.BgeRerankerV2M3);

    private sealed class ModelServer(string revision, string ranks) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int InfoRequests { get; private set; }
        public List<string> RerankBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/info")
            {
                InfoRequests++;
                return Json($$"""{"model_id":"BAAI/bge-reranker-v2-m3","model_sha":"{{revision}}"}""");
            }

            Assert.Equal("/rerank", request.RequestUri.AbsolutePath);
            RerankBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            return Status == HttpStatusCode.OK ? Json(ranks) : new HttpResponseMessage(Status);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
