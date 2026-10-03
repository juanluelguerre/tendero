using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>The model-call rows on Postgres (phase 12): every field round-trips, the
/// outcome as text, written on the writer's own context.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AIInvocationRowsTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_model_calls_row_is_kept_as_it_was_written()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("ai_invocations");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        AIInvocation[] rows =
        [
            new(Guid.CreateVersion7(Instant), "steward.turn", "gemini", "gemini-2.5-flash", "steward-v1", "conv-1",
                "4bf92f3577b34da6a3ce929d0e0e4736", 1500, 342, 1305, 4100, AIOutcome.Ok, Instant, "lucia"),
            new(Guid.CreateVersion7(Instant.AddSeconds(1)), "knowledge.extract", "extractor", "Qwen/Qwen3.5-4B", "claims-2026-09-26", null,
                null, 0, 0, 0, 180000, AIOutcome.Timeout, Instant.AddSeconds(1)),
        ];

        await using (var scope = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAIInvocationWriter>().WriteAsync(rows, ct);

        await using var reread = factory.Create();
        Assert.Equal(rows, await reread.AIInvocations.OrderBy(row => row.At).ToListAsync(ct));
        var outcome = await reread.Database.SqlQueryRaw<string>("SELECT outcome AS \"Value\" FROM telemetry.ai_invocations ORDER BY at").ToListAsync(ct);
        Assert.Equal(["Ok", "Timeout"], outcome);

        // A conversation's tokens so far: what a turn's budget is checked against (S-3b).
        await using var reading = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString)
            .BuildServiceProvider().CreateAsyncScope();
        var reader = reading.ServiceProvider.GetRequiredService<IAIInvocationReader>();
        Assert.Equal(1842, await reader.ConversationTokensAsync("conv-1", ct));
        Assert.Equal(0, await reader.ConversationTokensAsync("nobody", ct));

        // A person's tokens since a moment: what the day's budget is checked against (P12-9).
        Assert.Equal(1842, await reader.PersonTokensSinceAsync("lucia", Instant.AddHours(-1), ct));
        Assert.Equal(0, await reader.PersonTokensSinceAsync("lucia", Instant.AddSeconds(1), ct));
        Assert.Equal(0, await reader.PersonTokensSinceAsync("nobody", Instant.AddHours(-1), ct));
    }
}
