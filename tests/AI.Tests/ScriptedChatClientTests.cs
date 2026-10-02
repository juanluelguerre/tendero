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
