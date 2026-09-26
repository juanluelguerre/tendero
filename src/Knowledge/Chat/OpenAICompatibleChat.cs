using System.ClientModel;
using System.ClientModel.Primitives;
using ElGuerre.Tendero.Knowledge.Support;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

// The namespace is the fence, as in Search.Embeddings: the OpenAI SDK stays inside
// Knowledge.Chat, and everything else sees IChatClient and the Knowledge ports.
namespace ElGuerre.Tendero.Knowledge.Chat;

/// <summary>
/// The one place that knows the generative model is reached over OpenAI's chat
/// API — llama.cpp's server today; vLLM or a hosted model would be a new endpoint,
/// not new code (ADR 0005: the MEAI abstraction is the port).
/// </summary>
public static class OpenAICompatibleChat
{
    // llama.cpp checks no key, and the SDK refuses to build a client without one.
    private const string UnusedApiKey = "llama-cpp-checks-no-key";

    public static IChatClient Create(Uri endpoint, string model, PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint, "v1"), NetworkTimeout = TimeSpan.FromMinutes(10) };
        if (transport is not null)
            options.Transport = transport;

        return new ChatClient(model, new ApiKeyCredential(UnusedApiKey), options).AsIChatClient();
    }
}

/// <summary>
/// The generative model asked whether the text says the claim, answering one
/// token, read by the probability it gave <em>sí</em> against <em>no</em> — not by
/// a number it writes about itself, which is the confidence roadmap B4 warns is
/// not a probability.
///
/// Log-probabilities are not part of MEAI's options; they are asked for through
/// the OpenAI SDK's own, which MEAI passes through.
/// </summary>
public sealed class ChatYesNoSupportScorer(IChatClient chat) : ISupportScorer
{
    private static readonly string[] Yes = ["sí", "si", "yes"];

    public async Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            Temperature = 0,
            Seed = 42,
            MaxOutputTokens = 1,
            RawRepresentationFactory = _ => new ChatCompletionOptions { IncludeLogProbabilities = true, TopLogProbabilityCount = 10 },
        };

        var response = await chat.GetResponseAsync(
            [
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, "Respondes solo 'sí' o 'no'."),
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, $"Descripción del producto:\n{text}\n\n¿La descripción afirma esto? {claim}"),
            ],
            options, cancellationToken);

        var top = (response.RawRepresentation as ChatCompletion)?.ContentTokenLogProbabilities.FirstOrDefault()?.TopLogProbabilities ?? [];
        double Probability(Func<string, bool> which) =>
            top.Where(token => which(token.Token.Trim().ToLowerInvariant())).Sum(token => Math.Exp(token.LogProbability));

        var yes = Probability(token => Yes.Contains(token));
        var no = Probability(token => token == "no");
        return yes + no > 0 ? yes / (yes + no) : 0.5;
    }
}
