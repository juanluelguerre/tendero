using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace ElGuerre.Tendero.AI.Chat;

/// <summary>One token of an answer and the chance the model gave it.</summary>
public sealed record TokenProbability(string Token, double Probability);

/// <summary>What a model answered to a closed question.</summary>
/// <param name="Option">The option it chose, as the caller wrote it — or null when what it wrote is none of them.</param>
/// <param name="Confidence">The chance it gave the answer it wrote, 0 to 1 — or null when the provider sent no probabilities. Never a number the model says about itself.</param>
/// <param name="Said">What it wrote, as it wrote it.</param>
public sealed record Choice(string? Option, double? Confidence, string Said);

/// <summary>What a model answered to "how many".</summary>
/// <param name="Number">The number it wrote — or null when what it wrote is not one.</param>
/// <param name="Confidence">The chance it gave the answer it wrote, as for a <see cref="Choice"/>.</param>
public sealed record Counted(int? Number, double? Confidence, string Said);

/// <summary>
/// A question with a closed set of answers, asked so that the answer comes with
/// how sure the model was (phase 13a).
///
/// **Plain text, not JSON.** The options are listed in the question and the
/// model writes one of them, three tokens or so. Held to a JSON schema instead,
/// the same model on the same 1,610 photographs is right 68.0 % of the time
/// against 80.5 %: the grammar forces an opening brace where the model wanted
/// to write the answer, and it never recovers the thought. Code reads which
/// option was written, and an answer that is none of them is no answer.
///
/// **The confidence is the answer's own probability**, read from the tokens'
/// log-probabilities — the OpenAI SDK's, passed through MEAI, as the support
/// scorer reads its yes and no. It ranks answers well: at 0.95 and above, 45 %
/// of that exam at 95.7 % right. A threshold is the caller's to choose, with a
/// margin: tuned on half the examples to give 90 %, it gave 88.6 % on the rest.
/// </summary>
public static class ClosedQuestion
{
    /// <summary>What a model wraps an answer in: quotes and a full stop.</summary>
    private static readonly char[] Wrapping = ['"', '\'', '.'];

    /// <summary>The longest option is a few words; anything longer is not one.</summary>
    public const int MaxAnswerTokens = 16;

    /// <summary>The question as it is sent: what is asked, then the options as they are to be written.</summary>
    public static string Among(string question, IReadOnlyList<string> options) =>
        $"{question} Answer with exactly one of these options, written as it is here, and nothing else: {string.Join("; ", options)}.";

    /// <param name="shown">What the question is about — a picture, most often — sent before it.</param>
    /// <param name="options">The caller's own options (metering, the model's id); temperature, length and format are set here.</param>
    public static async Task<Choice> AskAsync(
        IChatClient model, IReadOnlyList<AIContent> shown, string question, IReadOnlyList<string> choices, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(choices);

        var (said, tokens) = await AnswerAsync(model, shown, Among(question, choices), options, cancellationToken);
        return Read(said, tokens, choices);
    }

    /// <summary>
    /// "How many", answered with a number and how sure the model was of it. The
    /// question is sent as it is given: it is the caller's to say that the answer
    /// is the number alone.
    /// </summary>
    public static async Task<Counted> CountAsync(
        IChatClient model, IReadOnlyList<AIContent> shown, string question, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shown);

        var (said, tokens) = await AnswerAsync(model, shown, question, options, cancellationToken);
        return ReadNumber(said, tokens);
    }

    /// <summary>One short plain answer at temperature zero, with the probability of each of its tokens.</summary>
    private static async Task<(string Said, IReadOnlyList<TokenProbability> Tokens)> AnswerAsync(
        IChatClient model, IReadOnlyList<AIContent> shown, string question, ChatOptions? options, CancellationToken cancellationToken)
    {
        var asked = options?.Clone() ?? new ChatOptions();
        asked.Temperature = 0;
        asked.MaxOutputTokens = MaxAnswerTokens;
        asked.ResponseFormat = null;
        asked.RawRepresentationFactory = _ => new ChatCompletionOptions { IncludeLogProbabilities = true, TopLogProbabilityCount = 1 };

        var response = await model.GetResponseAsync(
            [new ChatMessage(ChatRole.User, [.. shown, new TextContent(question)])], asked, cancellationToken);

        return (
            response.Text,
            [
                .. ((response.RawRepresentation as ChatCompletion)?.ContentTokenLogProbabilities ?? [])
                    .Select(token => new TokenProbability(token.Token, Math.Exp(token.LogProbability))),
            ]);
    }

    /// <summary>Which option an answer is, and how sure the model was of it.</summary>
    public static Choice Read(string said, IReadOnlyList<TokenProbability> tokens, IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(choices);

        var written = said.Trim().Trim(Wrapping).Trim();
        var option = choices.FirstOrDefault(choice => string.Equals(choice, written, StringComparison.OrdinalIgnoreCase));

        return new Choice(option, ConfidenceOf(tokens), said);
    }

    /// <summary>The number an answer is, and how sure the model was of it. Digits alone: "three" and "3 pans" are not a number.</summary>
    public static Counted ReadNumber(string said, IReadOnlyList<TokenProbability> tokens)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(tokens);

        var written = said.Trim().Trim(Wrapping).Trim();
        int? number = int.TryParse(written, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        return new Counted(number, ConfidenceOf(tokens), said);
    }

    /// <summary>
    /// The chance the model gave the whole answer, token after token — of the
    /// tokens that carry it: white space and punctuation say nothing of the
    /// choice. Null with no tokens: no probabilities is not certainty.
    /// </summary>
    private static double? ConfidenceOf(IReadOnlyList<TokenProbability> tokens)
    {
        double[] carrying = [.. tokens.Where(token => token.Token.Any(char.IsLetterOrDigit)).Select(token => token.Probability)];
        return carrying.Length == 0 ? null : carrying.Aggregate(1.0, (joint, probability) => joint * probability);
    }
}
