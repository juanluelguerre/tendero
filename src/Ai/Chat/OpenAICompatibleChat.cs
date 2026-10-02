using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

// The namespace is the fence, as in Search.Embeddings: the OpenAI SDK stays inside
// AI.Chat, and everything else sees IChatClient (ADR 0053).
namespace ElGuerre.Tendero.AI.Chat;

/// <summary>
/// The one place that knows the generative model is reached over OpenAI's chat
/// API — llama.cpp's server today; vLLM or a hosted model would be a new endpoint,
/// not new code (ADR 0005: the MEAI abstraction is the port).
/// </summary>
public static class OpenAICompatibleChat
{
    // llama.cpp checks no key, and the SDK refuses to build a client without one.
    private const string UnusedApiKey = "llama-cpp-checks-no-key";

    /// <summary>A llama.cpp server's root, as the extractor and SearchEval are given
    /// it: the API is under /v1.</summary>
    public static IChatClient Create(Uri endpoint, string model, PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return CreateAt(new Uri(endpoint, "v1"), model, transport: transport);
    }

    /// <summary>A profile's endpoint is the provider's OpenAI base URL, /v1 included,
    /// used as given (Gemini's is …/v1beta/openai/).</summary>
    public static IChatClient CreateAt(
        Uri baseUrl, string model, string? apiKey = null, TimeSpan? timeout = null, PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        var options = new OpenAIClientOptions { Endpoint = baseUrl, NetworkTimeout = timeout ?? TimeSpan.FromMinutes(10) };
        if (transport is not null)
            options.Transport = transport;

        return new ChatClient(model, new ApiKeyCredential(apiKey ?? UnusedApiKey), options).AsIChatClient();
    }
}
