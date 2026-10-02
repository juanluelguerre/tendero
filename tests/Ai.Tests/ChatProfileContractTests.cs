using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// What every profile kind must do for Steward (the spec, *Evaluation and tests*):
/// call a tool and read its result, report its usage, and surface a timeout as a
/// timeout. Each kind inherits it; S-3's Scripted kind joins.
/// </summary>
public abstract class ChatProfileContractTests
{
    protected abstract IChatClient? Client(TimeSpan timeout);

    [Fact]
    [Trait("Category", "Model")]
    public async Task A_tool_call_round_trips()
    {
        var chat = Client(TimeSpan.FromMinutes(5));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var called = false;
        var tool = AIFunctionFactory.Create(() => { called = true; return 12; }, "count_products_missing_english",
            "How many products have no English name.");
        var answer = await chat!.AsBuilder().UseFunctionInvocation().Build().GetResponseAsync(
            "How many products have no English name? Use the tool, then answer with the number.",
            new ChatOptions { Tools = [tool], Temperature = 0 }, TestContext.Current.CancellationToken);

        Assert.True(called);
        Assert.Contains("12", answer.Text);
    }

    [Fact]
    [Trait("Category", "Model")]
    public async Task Usage_is_reported()
    {
        var chat = Client(TimeSpan.FromMinutes(5));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var answer = await chat!.GetResponseAsync("Say hello.", new ChatOptions { MaxOutputTokens = 10 }, TestContext.Current.CancellationToken);

        Assert.True(answer.Usage?.InputTokenCount > 0);
        Assert.True(answer.Usage?.OutputTokenCount > 0);
    }

    [Fact]
    [Trait("Category", "Model")]
    public async Task A_timeout_surfaces_as_one()
    {
        var chat = Client(TimeSpan.FromMilliseconds(1));
        Assert.SkipWhen(chat is null, "No model is running for this profile.");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => chat!.GetResponseAsync(
            "Write a long story.", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(failure is TaskCanceledException or TimeoutException || failure.InnerException is TimeoutException,
            $"A timeout came back as {failure.GetType().Name}: {failure.Message}");
    }
}
