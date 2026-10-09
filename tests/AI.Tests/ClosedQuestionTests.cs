using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// A question with a closed set of answers (phase 13a): the model writes one of
/// the options as plain text — no JSON around it — and code reads which one, and
/// how sure the model was from the probability of the tokens it wrote. Measured
/// on ImplicitAVE: the same question answered inside a JSON schema is right
/// 68.0 % of the time, and as plain text 80.5 %.
/// </summary>
public sealed class ClosedQuestionTests
{
    private static readonly string[] Heels = ["Flat", "Low Heel", "Mid Heel", "High Heel"];

    private static TokenProbability[] Tokens(params (string Token, double Probability)[] tokens) =>
        [.. tokens.Select(token => new TokenProbability(token.Token, token.Probability))];

    [Fact]
    public void The_answer_is_the_option_the_model_wrote_whatever_its_case_quotes_or_full_stop()
    {
        Assert.Equal("Low Heel", ClosedQuestion.Read(" \"low heel\".\n", [], Heels).Option);
    }

    /// <summary>What is not one of the options is no answer: not the nearest one, and not the first.</summary>
    [Theory]
    [InlineData("Kitten Heel")]
    [InlineData("Low Heel or Mid Heel")]
    [InlineData("")]
    public void What_is_not_one_of_the_options_is_no_answer(string said)
    {
        var choice = ClosedQuestion.Read(said, Tokens((said, 0.99)), Heels);

        Assert.Null(choice.Option);
        Assert.Equal(said, choice.Said);
    }

    /// <summary>
    /// How sure: the chance the model gave to the whole answer, token after
    /// token. The white space and the punctuation around it say nothing of the
    /// choice and are left out.
    /// </summary>
    [Fact]
    public void The_confidence_is_the_probability_of_the_tokens_that_carry_the_answer()
    {
        var choice = ClosedQuestion.Read("Low Heel.", Tokens((" ", 0.5), ("Low", 0.8), (" Heel", 0.9), (".", 0.3)), Heels);

        Assert.Equal("Low Heel", choice.Option);
        Assert.Equal(0.72, choice.Confidence!.Value, 6);
    }

    /// <summary>A provider that sends no probabilities gives an answer with no confidence — never one of 1.</summary>
    [Fact]
    public void With_no_probabilities_there_is_an_answer_and_no_confidence()
    {
        var choice = ClosedQuestion.Read("Flat", [], Heels);

        Assert.Equal(("Flat", (double?)null), (choice.Option, choice.Confidence));
    }

    [Fact]
    public void The_question_ends_by_listing_the_options_as_they_are_to_be_written()
    {
        var asked = ClosedQuestion.Among("What is the Heel Height of the product?", Heels);

        Assert.StartsWith("What is the Heel Height of the product? ", asked, StringComparison.Ordinal);
        Assert.Contains("Flat; Low Heel; Mid Heel; High Heel.", asked, StringComparison.Ordinal);
    }

    private sealed class Says(string text) : IChatClient
    {
        public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Asked { get; } = [];

        /// <summary>What the provider's own client answered with, when the test says: where the probabilities travel.</summary>
        public object? Raw { get; init; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(([.. messages], options));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { RawRepresentation = Raw });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// One message: what is shown, then the question with its options. Asked for
    /// a short answer at temperature zero, with no schema — the schema is what
    /// cost twelve points — and whatever the caller's options carried is kept.
    /// </summary>
    [Fact]
    public async Task The_model_is_shown_the_picture_and_asked_once_for_a_short_plain_answer()
    {
        var model = new Says("High Heel");
        var picture = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var mine = new ChatOptions { ModelId = "kept", ResponseFormat = ChatResponseFormat.Json };

        var choice = await ClosedQuestion.AskAsync(
            model, [picture], "What is the Heel Height of the product?", Heels, mine, TestContext.Current.CancellationToken);

        Assert.Equal("High Heel", choice.Option);
        var (messages, options) = Assert.Single(model.Asked);
        var message = Assert.Single(messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Same(picture, message.Contents[0]);
        Assert.Equal(ClosedQuestion.Among("What is the Heel Height of the product?", Heels), ((TextContent)message.Contents[1]).Text);
        Assert.Equal(("kept", 0f, (ChatResponseFormat?)null), (options!.ModelId, options.Temperature, options.ResponseFormat));
        Assert.Equal(ClosedQuestion.MaxAnswerTokens, options.MaxOutputTokens);
        Assert.NotNull(options.RawRepresentationFactory);
        // The caller's own options are not written on.
        Assert.Null(mine.Temperature);
    }

    /// <summary>
    /// The confidence is read off what the SERVER sent back: the probability it
    /// gave each token of the answer. Reading a list of tokens is tested above;
    /// this is the step before it — that the tokens are taken from the response
    /// at all, and not replaced by none.
    /// </summary>
    // The factory that builds a provider's answer for a test is marked experimental by its SDK.
#pragma warning disable OPENAI001
    [Fact]
    public async Task The_confidence_comes_from_the_probabilities_the_server_sent_with_its_answer()
    {
        var model = new Says("Low Heel")
        {
            Raw = OpenAI.Chat.OpenAIChatModelFactory.ChatCompletion(contentTokenLogProbabilities:
            [
                OpenAI.Chat.OpenAIChatModelFactory.ChatTokenLogProbabilityDetails("Low", MathF.Log(0.8f)),
                OpenAI.Chat.OpenAIChatModelFactory.ChatTokenLogProbabilityDetails(" Heel", MathF.Log(0.9f)),
            ]),
        };

        var choice = await ClosedQuestion.AskAsync(model, [], "What is the Heel Height?", Heels, cancellationToken: TestContext.Current.CancellationToken);
        var counted = await ClosedQuestion.CountAsync(
            new Says("3") { Raw = OpenAI.Chat.OpenAIChatModelFactory.ChatCompletion(contentTokenLogProbabilities: [OpenAI.Chat.OpenAIChatModelFactory.ChatTokenLogProbabilityDetails("3", MathF.Log(0.6f))]) },
            [], "How many pans?", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Low Heel", choice.Option);
        Assert.Equal(0.72, choice.Confidence!.Value, 4);
        Assert.Equal((3, 0.6), (counted.Number, Math.Round(counted.Confidence!.Value, 4)));
    }

#pragma warning restore OPENAI001

    /// <summary>A model that is not there, and nothing to show it, are the caller's mistakes and are said at the door.</summary>
    [Fact]
    public async Task What_is_asked_needs_a_model_something_to_show_and_the_choices()
    {
        var model = new Says("Flat");
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => ClosedQuestion.AskAsync(null!, [], "?", Heels, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClosedQuestion.AskAsync(model, null!, "?", Heels, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClosedQuestion.AskAsync(model, [], "?", null!, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClosedQuestion.CountAsync(null!, [], "?", cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClosedQuestion.CountAsync(model, null!, "?", cancellationToken: ct));
        Assert.Throws<ArgumentNullException>(() => ClosedQuestion.Read(null!, [], Heels));
        Assert.Throws<ArgumentNullException>(() => ClosedQuestion.Read("Flat", null!, Heels));
        Assert.Throws<ArgumentNullException>(() => ClosedQuestion.Read("Flat", [], null!));
        Assert.Throws<ArgumentNullException>(() => ClosedQuestion.ReadNumber(null!, []));
        Assert.Throws<ArgumentNullException>(() => ClosedQuestion.ReadNumber("3", null!));
    }

    // ---------- how many ----------

    [Fact]
    public void A_count_is_the_number_the_model_wrote_and_the_probability_of_its_digits()
    {
        var counted = ClosedQuestion.ReadNumber(" 3.\n", Tokens((" ", 0.4), ("3", 0.97), (".", 0.2)));

        Assert.Equal((3, 0.97), (counted.Number, counted.Confidence));
    }

    /// <summary>Digits alone are a number: a word, or a number with something after it, is not one.</summary>
    [Theory]
    [InlineData("three")]
    [InlineData("3 pans")]
    [InlineData("-1")]
    [InlineData("")]
    public void What_is_not_a_number_is_no_count(string said)
    {
        Assert.Null(ClosedQuestion.ReadNumber(said, []).Number);
    }

    /// <summary>The question is sent as it is given — it says itself that the answer is the number alone — after the picture.</summary>
    [Fact]
    public async Task How_many_is_asked_as_the_question_is_written_and_answered_with_a_number()
    {
        var model = new Says("4");
        var picture = new DataContent(new byte[] { 1, 2, 3 }, "image/png");

        var counted = await ClosedQuestion.CountAsync(model, [picture], "How many? Answer with the number only.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((4, (double?)null), (counted.Number, counted.Confidence));
        var (messages, options) = Assert.Single(model.Asked);
        Assert.Equal("How many? Answer with the number only.", ((TextContent)Assert.Single(messages).Contents[1]).Text);
        Assert.Equal((0f, (ChatResponseFormat?)null, (int?)ClosedQuestion.MaxAnswerTokens), (options!.Temperature, options.ResponseFormat, options.MaxOutputTokens));
    }
}
