using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What the OpenAI SDK really sends to Text Embeddings Inference.
///
/// "OpenAI-compatible" is a surface, not a contract. TEI implements a subset of
/// the embeddings API, and the SDK is written for OpenAI; the day a new SDK
/// version adds a field TEI does not know, the failure is a 422 at run time and
/// nothing at build time. These tests capture the request the adapter sends and
/// hold it to the fields TEI 1.9.3 declares — <c>input</c>, <c>model</c>,
/// <c>user</c>, <c>encoding_format</c> and <c>dimensions</c> — with no model
/// server and no Docker.
/// </summary>
public sealed class TextEmbeddingsInferenceTests
{
    private static readonly string[] FieldsTeiAccepts = ["input", "model", "user", "encoding_format", "dimensions"];

    [Fact]
    public async Task The_request_goes_to_the_openai_compatible_route_and_names_the_pinned_model()
    {
        var server = new FakeModelServer();

        await Generate(server, "cafetera italiana");

        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/embeddings", request.Path);
        Assert.Equal("BAAI/bge-m3", request.Body["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_request_carries_only_fields_text_embeddings_inference_accepts()
    {
        var server = new FakeModelServer();

        await Generate(server, "cafetera italiana");

        var fields = Assert.Single(server.Requests).Body.Select(field => field.Key);
        Assert.All(fields, field => Assert.Contains(field, FieldsTeiAccepts));
    }

    [Fact]
    public async Task No_truncation_is_asked_of_a_model_that_was_not_trained_for_it()
    {
        var server = new FakeModelServer();

        await Generate(server, "cafetera italiana");

        Assert.False(Assert.Single(server.Requests).Body.ContainsKey("dimensions"));
    }

    [Fact]
    public async Task The_vector_comes_back_exactly_as_the_server_encoded_it()
    {
        var server = new FakeModelServer();

        var embedding = await Generate(server, "cafetera italiana");

        Assert.Equal(FakeModelServer.Vector, embedding.Vector.ToArray());
    }

    private static async Task<Microsoft.Extensions.AI.Embedding<float>> Generate(FakeModelServer server, string text)
    {
        var generator = TextEmbeddingsInference.CreateGenerator(
            new Uri("http://model-server.test"),
            EmbeddingModelDescriptor.BgeM3,
            new HttpClientPipelineTransport(new HttpClient(server)));

        var embeddings = await generator.GenerateAsync([text], cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(embeddings);
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, JsonObject Body);

    /// <summary>
    /// Answers the way TEI does: floats by default, base64 of little-endian
    /// float32 when the request asks for <c>encoding_format: base64</c>, which the
    /// OpenAI SDK does unless told otherwise.
    /// </summary>
    private sealed class FakeModelServer : HttpMessageHandler
    {
        public static readonly float[] Vector = [0.25f, -0.5f, 0.125f, 1f];

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath, body));

            var base64 = body["encoding_format"]?.GetValue<string>() == "base64";
            JsonNode embedding = base64
                ? JsonValue.Create(Convert.ToBase64String(MemoryMarshal.AsBytes(Vector.AsSpan())))
                : new JsonArray([.. Vector.Select(value => (JsonNode)JsonValue.Create(value))]);

            var response = new JsonObject
            {
                ["object"] = "list",
                ["model"] = body["model"]!.GetValue<string>(),
                ["data"] = new JsonArray(new JsonObject { ["object"] = "embedding", ["index"] = 0, ["embedding"] = embedding }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 3, ["total_tokens"] = 3 },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
