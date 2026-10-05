using ElGuerre.Tendero.AI.Support;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

// The namespace is the fence, as in Search.Embeddings and AI.Chat: the scorer reads
// OpenAI's raw log-probabilities, so it alone stays here (ADR 0053).
namespace ElGuerre.Tendero.Knowledge.Chat;

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
