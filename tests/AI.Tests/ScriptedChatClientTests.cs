using System.Text.Json;
using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// A model that answers from a script (the spec, *Evaluation and tests*): it runs
/// the whole of Steward — agent, tools, proposals — in CI, where no model fits.
/// </summary>
public sealed class ScriptedChatClientTests
{
    private static IChatClient Scripted(TimeSpan? timeout = null) =>
        ScriptedChatClient.FromFile(Path.Combine(AppContext.BaseDirectory, "scripts", "contract.json"), timeout ?? TimeSpan.FromSeconds(10));

    [Fact]
    public async Task A_matching_step_calls_its_tools()
    {
        var answer = await Scripted().GetResponseAsync("How many products have no English name?", cancellationToken: TestContext.Current.CancellationToken);

        var call = Assert.Single(answer.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("count_products_missing_english", call.Name);
        Assert.Equal((120L, 15L), (answer.Usage!.InputTokenCount!.Value, answer.Usage.OutputTokenCount!.Value));
    }

    [Fact]
    public async Task After_the_tool_answers_it_says_its_line_with_the_result()
    {
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "How many products have no English name?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "count_products_missing_english")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", 12)]),
        ];

        var answer = await Scripted().GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("There are 12 products with no English name.", answer.Text);
    }

    /// <summary>A model that never stops calling tools, so a turn's cap can be shown.</summary>
    [Fact]
    public async Task An_always_step_calls_again_after_its_tools_answered()
    {
        var looping = new ScriptedChatClient([new ScriptedTurn("loop", [new ScriptedCall("t", default)], Always: true)], TimeSpan.FromSeconds(5));
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "loop"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-0", "t")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-0", 1)]),
        ];

        var answer = await looping.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(answer.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
    }

    /// <summary>A model that fails after its tools answered: a provider dropping a
    /// turn half way, after a proposal was stored.</summary>
    [Fact]
    public async Task A_then_fail_step_fails_once_its_tools_answered()
    {
        var failing = new ScriptedChatClient([new ScriptedTurn("propose", [new ScriptedCall("t", default)], ThenFail: true)], TimeSpan.FromSeconds(5));
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "propose"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-0", "t")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-0", 1)]),
        ];

        await Assert.ThrowsAsync<HttpRequestException>(() => failing.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A second round of calls after the first round answered, and an argument
    /// taken from what a tool returned (S-6): a script can find a product and then
    /// propose for it, though its id is minted at import and no script can know it.
    /// </summary>
    [Fact]
    public async Task A_second_round_takes_an_argument_from_what_the_first_round_read()
    {
        var script = new ScriptedChatClient(
        [
            new ScriptedTurn("rename", [new ScriptedCall("find_products", Json("""{"text":"Pulse"}"""))],
                Then: [new ScriptedCall("propose", Json("""{"items":[{"productId":"{from:find_products:data.products.0.productId}","name":"Pulse"}]}"""))],
                Say: "Proposed."),
        ], TimeSpan.FromSeconds(5));
        var found = Json("""{"data":{"products":[{"productId":"0199-pulse","name":"Pulse Runner"}]},"rows":1}""");
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "rename the Pulse Runner"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-0", "find_products")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-0", found)]),
        ];

        var second = await script.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);
        var call = Assert.Single(second.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        conversation.Add(new(ChatRole.Assistant, [call]));
        conversation.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, "ok")]));
        var third = await script.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("propose", call.Name);
        var item = ((JsonElement)call.Arguments!["items"]!)[0];
        Assert.Equal(("0199-pulse", "Pulse"), (item.GetProperty("productId").GetString(), item.GetProperty("name").GetString()));
        Assert.Equal("Proposed.", third.Text);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly ChatOptions HeldToASchema = new()
    {
        ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonDocument.Parse("""{"type":"object"}""").RootElement, "answer"),
    };

    private static IChatClient Replying(string reply) =>
        new ScriptedChatClient(
            [JsonSerializer.Deserialize<ScriptedTurn>(
                """{"when":"pan","calls":[{"tool":"search_products","arguments":{}}],"say":"DONE","inputTokens":70,"outputTokens":9,"reply":"""
                + reply + "}",
                ScriptedChatClient.Json)!],
            TimeSpan.FromSeconds(10));

    /// <summary>A call held to a schema has no tools: the step's reply is the answer, and its calls are not made again.</summary>
    [Fact]
    public async Task A_call_that_asks_for_json_is_answered_with_the_steps_reply_and_calls_no_tool()
    {
        var answer = await Replying("""{"kind":"ask","ask":"Which one?"}""")
            .GetResponseAsync("a pan", HeldToASchema, TestContext.Current.CancellationToken);

        Assert.Equal("""{"kind":"ask","ask":"Which one?"}""", answer.Text);
        Assert.Empty(answer.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal((70L, 9L), (answer.Usage!.InputTokenCount!.Value, answer.Usage.OutputTokenCount!.Value));

        // The same step, asked without a schema, still calls its tools first.
        var gathering = await Replying("""{"kind":"ask","ask":"Which one?"}""").GetResponseAsync("a pan", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(gathering.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
    }

    /// <summary>A model that ignores the schema is scripted as a string: what is not JSON reaches the caller as it is.</summary>
    [Fact]
    public async Task A_reply_written_as_a_string_is_answered_as_that_text()
    {
        var answer = await Replying("\"The pan is great.\"").GetResponseAsync("a pan", HeldToASchema, TestContext.Current.CancellationToken);

        Assert.Equal("The pan is great.", answer.Text);
    }

    [Fact]
    public async Task A_step_with_no_reply_was_not_scripted_for_a_schema()
    {
        var answer = await Scripted().GetResponseAsync("How many products have no English name?", HeldToASchema, TestContext.Current.CancellationToken);

        Assert.Equal(ScriptedChatClient.NotScripted, answer.Text);
    }

    [Fact]
    public async Task A_question_it_was_not_scripted_for_says_so()
    {
        var answer = await Scripted().GetResponseAsync("What is the meaning of life?", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("I was not scripted for that.", answer.Text);
        Assert.Equal(ChatFinishReason.Stop, answer.FinishReason);
    }

    [Fact]
    public async Task A_step_slower_than_the_timeout_times_out()
    {
        await Assert.ThrowsAsync<TaskCanceledException>(() => Scripted(TimeSpan.FromMilliseconds(20))
            .GetResponseAsync("Tell me a long story.", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_script_reads_its_arguments_as_json()
    {
        var step = JsonSerializer.Deserialize<ScriptedTurn>(
            """{"when":"x","calls":[{"tool":"t","arguments":{"kind":"MissingName","culture":"en"}}]}""",
            ScriptedChatClient.Json)!;

        Assert.Equal("en", step.Calls![0].Arguments.GetProperty("culture").GetString());
    }
}
