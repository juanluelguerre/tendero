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
