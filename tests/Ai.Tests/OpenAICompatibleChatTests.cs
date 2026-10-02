using System.ClientModel.Primitives;
using System.Net;
using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>Where a request goes and what it carries, read off the wire: a profile's
/// base URL is used as given, and a key travels as a bearer token.</summary>
public sealed class OpenAICompatibleChatTests
{
    [Fact]
    public async Task A_profile_base_url_is_used_as_given_with_its_key()
    {
        var wire = new RecordingHandler();
        var chat = OpenAICompatibleChat.CreateAt(
            new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"), "gemini-2.5-flash", "secret",
            transport: new HttpClientPipelineTransport(new HttpClient(wire)));

        await chat.GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", wire.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer secret", wire.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task The_extractors_endpoint_still_gets_v1()
    {
        var wire = new RecordingHandler();
        var chat = OpenAICompatibleChat.Create(new Uri("http://localhost:38083"), "qwen", new HttpClientPipelineTransport(new HttpClient(wire)));

        await chat.GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:38083/v1/chat/completions", wire.Request!.RequestUri!.ToString());
    }

    /// <summary>
    /// The SDK retries a failed call three times by default: a model that timed out
    /// at 300 s would take twenty minutes to fail, and be metered as one call.
    /// Whether to try again — the same model or the fallback — is the caller's
    /// decision, so the client makes exactly one request (found by the contract
    /// suite against the local model, 2026-10-02).
    /// </summary>
    [Fact]
    public async Task A_failed_call_is_not_retried_behind_the_callers_back()
    {
        var wire = new RecordingHandler { Status = HttpStatusCode.ServiceUnavailable };
        var chat = OpenAICompatibleChat.CreateAt(new Uri("http://localhost:38085/v1/"), "qwen",
            transport: new HttpClientPipelineTransport(new HttpClient(wire)));

        await Assert.ThrowsAnyAsync<Exception>(() => chat.GetResponseAsync("hola", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, wire.Requests);
    }

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public int Requests { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("""
                    {"id":"1","object":"chat.completion","created":0,"model":"m",
                     "choices":[{"index":0,"message":{"role":"assistant","content":"hola"},"finish_reason":"stop"}],
                     "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
                    """, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
