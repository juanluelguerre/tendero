using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The agent registry against a real Postgres (P11-2): the aggregate round-trips
/// with its enums as words, and the directory the principal accessor reads
/// answers the standing on the key — and nothing for an agent nobody registered.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AgentRegistryTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_registered_agent_comes_back_and_its_standing_is_read_on_the_key()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("agent_registry");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var agent = Agent.Register(Clock, new AgentId("claude-desktop"), "Claude Desktop", "Anthropic", AgentTrust.Verified);
        agent.Suspend(Clock, "chargebacks");
        await using (var context = factory.Create())
        {
            context.Agents.Add(agent);
            await context.SaveChangesAsync(ct);
        }

        await using var services = new ServiceCollection().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IAgentDirectory>();

        Assert.Equal(new AgentStanding(AgentTrust.Verified, true), await directory.StandingOfAsync(new AgentId("claude-desktop"), ct));
        Assert.Null(await directory.StandingOfAsync(new AgentId("stranger"), ct));

        var stored = await scope.ServiceProvider.GetRequiredService<IAgentRepository>().FindByIdAsync(new AgentId("claude-desktop"), ct);
        Assert.Equal(("Anthropic", "chargebacks"), (stored!.Operator, stored.SuspensionReason));

        await using var raw = factory.Create();
        var trust = await raw.Database.SqlQueryRaw<string>("select trust as \"Value\" from accounts.agents").SingleAsync(ct);
        Assert.Equal("Verified", trust);
    }
}
