using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// One Steward turn (S-3b), run against scripted models: it reads, proposes and
/// answers; it is bounded in model calls and in tokens; it falls back when its
/// model fails; and when no model answers it says so and stores nothing.
/// </summary>
[Collection(nameof(FakeEntry))]
public sealed class StewardTurnTests
{
    private static readonly TestClock Clock = new();

    private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "scripts", "steward.json");

    public StewardTurnTests()
    {
        FakeEntry.Refusing.Clear();
        FakeEntry.Throwing.Clear();
        FakeEntry.Applied.Clear();
        FakeEntry.NotJson.Clear();
        FakeEntry.OnApply = null;
    }

    private sealed class GapsTool : IReadTool
    {
        public string Name => "look_at_gaps";

        public string Description => "What the catalogue lacks.";

        public JsonElement ArgumentsSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

        public Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReadAnswer(new { items = new[] { "MUG-1" } }, 1, 1));
    }

    private sealed class SpentTokens(long spent) : IAIInvocationReader
    {
        public Task<long> ConversationTokensAsync(string conversationId, CancellationToken cancellationToken = default) => Task.FromResult(spent);
    }

    private sealed class Shop(ServiceProvider provider, InMemoryProposals proposals) : IAsyncDisposable
    {
        public InMemoryProposals Proposals { get; } = proposals;

        public async Task<StewardAnswer> Ask(string question, IReadOnlyList<TurnMessage>? before = null)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new StewardTurnCommand(
                [.. before ?? [], new TurnMessage("user", question)], new ScreenContext("Products", null), "conv-1", "en"),
                TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private static Shop Compose(string model, string? fallback = null, long spent = 0, int contextTokens = 8192)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Steward:Model"] = model,
            ["Steward:Fallback"] = fallback,
            ["Steward:Models:scripted:Kind"] = "Scripted",
            ["Steward:Models:scripted:Script"] = Script,
            ["Steward:Models:scripted:Model"] = "scripted",
            ["Steward:Models:scripted:InputPrice"] = "0.30",
            ["Steward:Models:scripted:OutputPrice"] = "2.50",
            ["Steward:Models:scripted:ContextTokens"] = contextTokens.ToString(),
            // The same script under another name: a fallback that would propose again.
            ["Steward:Models:scripted-too:Kind"] = "Scripted",
            ["Steward:Models:scripted-too:Script"] = Script,
            // A model nobody answers for: the port is closed, the call fails at once.
            ["Steward:Models:broken:Endpoint"] = "http://127.0.0.1:9/v1/",
            ["Steward:Models:broken:Model"] = "broken",
            ["Steward:Models:broken:TimeoutSeconds"] = "2",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var proposals = new InMemoryProposals();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(StewardTurnCommand).Assembly);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IProposalRepository>(proposals);
        services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
        services.AddSingleton<IPrincipalAccessor>(new FixedPrincipal("Lucía"));
        services.AddScoped<IProposableCommand>(_ => new FakeEntry(new() { ["MUG-1"] = "old" }, new ProposalScope()));
        services.AddScoped<IReadTool, GapsTool>();
        services.AddSingleton<IAIInvocationReader>(new SpentTokens(spent));
        services.AddAssist(configuration);
        services.AddModelProfiles(configuration);
        return new Shop(services.BuildServiceProvider(), proposals);
    }

    [Fact]
    public async Task A_turn_reads_proposes_and_answers_with_what_it_cost()
    {
        await using var shop = Compose("scripted");

        var answer = await shop.Ask("Which products have no English name?");

        Assert.Equal(StewardStatus.Answered, answer.Status);
        Assert.StartsWith("I read the gaps and proposed:", answer.Text);
        Assert.Equal(new ToolUse("look_at_gaps", 1, 1), Assert.Single(answer.Reads));
        Assert.Equal(shop.Proposals.All[0].Id.ToString(), Assert.Single(answer.ProposalIds));
        Assert.Equal(("scripted", false), (answer.Profile, answer.FellBack));
        Assert.Equal(2, answer.Cost!.Calls);
        Assert.Equal(3000, answer.Cost.InputTokens);
        Assert.Equal(new ModelProfile { InputPrice = 0.30m, OutputPrice = 2.50m }.CostMicroEuros(3000, 240), answer.Cost.MicroEuros);
    }

    [Fact]
    public async Task A_model_that_fails_falls_back_to_the_other_profile()
    {
        await using var shop = Compose("broken", fallback: "scripted");

        var answer = await shop.Ask("hello");

        Assert.Equal((StewardStatus.Answered, "Hello.", "scripted", true), (answer.Status, answer.Text, answer.Profile, answer.FellBack));
    }

    /// <summary>No model answers: the turn says so, stores nothing, and the rest of
    /// the shop is untouched — invariant 8 for Steward, tested.</summary>
    [Fact]
    public async Task With_no_model_answering_the_turn_is_unavailable_and_stores_nothing()
    {
        await using var shop = Compose("broken");

        var answer = await shop.Ask("Which products have no English name?");

        Assert.Equal(StewardStatus.Unavailable, answer.Status);
        Assert.Empty(shop.Proposals.All);
        Assert.Empty(answer.ProposalIds);
    }

    [Fact]
    public async Task A_model_that_loops_on_tools_stops_at_six_calls_and_still_answers()
    {
        await using var shop = Compose("scripted");

        var answer = await shop.Ask("loop");

        Assert.Equal(StewardStatus.Answered, answer.Status);
        Assert.Equal(6, answer.Cost!.Calls);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
    }

    [Fact]
    public async Task A_tool_the_model_invents_is_an_error_it_reads_and_the_turn_answers()
    {
        await using var shop = Compose("scripted");

        var answer = await shop.Ask("invent a tool");

        Assert.Equal((StewardStatus.Answered, "That tool does not exist."), (answer.Status, answer.Text));
    }

    [Fact]
    public async Task A_conversation_over_its_budget_asks_no_model()
    {
        await using var shop = Compose("scripted", spent: 60_000);

        var answer = await shop.Ask("hello");

        Assert.Equal(StewardStatus.BudgetExceeded, answer.Status);
        Assert.Null(answer.Cost);
    }

    [Fact]
    public async Task A_history_near_the_window_is_flagged_so_the_panel_offers_a_new_conversation()
    {
        await using var shop = Compose("scripted", contextTokens: 1000);

        var answer = await shop.Ask("hello", [new TurnMessage("user", new string('x', 4000)), new TurnMessage("assistant", "Ok.")]);

        Assert.True(answer.NearContextLimit);
    }

    /// <summary>A provider that drops the turn after a proposal was stored: the
    /// proposal stays (it waits for a person in the list), and the turn does not fall
    /// back — a second model would propose it twice.</summary>
    [Fact]
    public async Task A_turn_that_fails_after_proposing_keeps_its_proposal_and_does_not_fall_back()
    {
        await using var shop = Compose("scripted", fallback: "scripted-too");

        var answer = await shop.Ask("drop it");

        Assert.Equal(StewardStatus.Unavailable, answer.Status);
        Assert.Single(shop.Proposals.All);
        Assert.Equal(shop.Proposals.All[0].Id.ToString(), Assert.Single(answer.ProposalIds));
    }

    [Theory]
    [InlineData(0, "en", false)]
    [InlineData(1, "fr", false)]
    [InlineData(1, "en", true)]
    public void A_turn_has_a_question_and_a_language_the_shop_speaks(int messages, string culture, bool valid) =>
        Assert.Equal(valid, new StewardTurnValidator().Validate(new StewardTurnCommand(
            [.. Enumerable.Repeat(new TurnMessage("user", "hello"), messages)], null, "conv-1", culture)).IsValid);
}
