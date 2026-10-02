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

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
